using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Parvathi.Windows.Services;

/// <summary>Explicit, bounded, hash-pinned installation of the official Apache-2.0 English model.</summary>
public sealed class ModelManager
{
    public const string ModelName = "vosk-model-small-en-us-0.15";
    public const string DownloadUrl = "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip";
    // Independently computed from the official archive on 2026-10-01. Changes fail closed.
    public const string ExpectedSha256 = "30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498";
    public const long ExpectedBytes = 41_205_931;
    public const string LicenseUrl = "https://alphacephei.com/vosk/models";
    private const long MaximumExtractedBytes = 100_000_000;
    private const string ManifestName = ".parvathi-verified.json";
    public static string DefaultModelPath => Path.Combine(SettingsStore.DataDirectory, "models", ModelName);
    public string ModelPath { get; }
    private readonly SemaphoreSlim installGate = new(1, 1);

    public ModelManager(string? modelPath = null) => ModelPath = string.IsNullOrWhiteSpace(modelPath) ? DefaultModelPath : Path.GetFullPath(modelPath);
    public bool IsInstalled
    {
        get
        {
            try
            {
                var manifest = ReadManifest(ModelPath);
                return manifest is not null && manifest.Files.Count > 5 && manifest.Files.All(file =>
                {
                    var target = SafePath(ModelPath, file.Path);
                    return File.Exists(target) && new FileInfo(target).Length == file.Bytes;
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return false; }
        }
    }

    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await installGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var parent = Path.GetDirectoryName(ModelPath)!;
        var scratch = Path.Combine(parent, ".download-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (IsInstalled)
            {
                try { await ValidateInstalledAsync(ModelPath, cancellationToken).ConfigureAwait(false); progress?.Report(1); return; }
                catch (InvalidDataException) { /* Explicit download also repairs a damaged model. */ }
            }
            Directory.CreateDirectory(scratch);
            var zipPath = Path.Combine(scratch, "model.zip");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            var ct = timeout.Token;
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != ExpectedBytes)
                throw new InvalidDataException("The model download size changed. Install a newer Parvathi release; this download has been rejected.");
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                byte[] buffer = new byte[65536];
                long total = 0;
                int bytes;
                while ((bytes = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
                {
                    total += bytes;
                    if (total > ExpectedBytes) throw new InvalidDataException("The model exceeded its expected size. Download stopped.");
                    await output.WriteAsync(buffer.AsMemory(0, bytes), ct).ConfigureAwait(false);
                    progress?.Report(total / (double)ExpectedBytes * .85);
                }
                if (total != ExpectedBytes) throw new InvalidDataException("The model download was incomplete. Check your connection and try again.");
            }
            await using (var file = File.OpenRead(zipPath))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false));
                if (!actual.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model integrity verification failed. The download was discarded; try again or update Parvathi.");
            }
            progress?.Report(.9);
            var expanded = Path.Combine(scratch, "expanded");
            Directory.CreateDirectory(expanded);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.Entries.Count > 1000 || archive.Entries.Sum(entry => entry.Length) > MaximumExtractedBytes)
                    throw new InvalidDataException("Model archive exceeds extraction limits.");
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (unixType == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Model archives may not contain symbolic links.");
                    var destination = SafePath(expanded, entry.FullName);
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var source = entry.Open();
                    await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                    await source.CopyToAsync(target, ct).ConfigureAwait(false);
                    if (target.Length != entry.Length) throw new InvalidDataException("Model entry was truncated.");
                }
            }
            var staged = Path.Combine(expanded, ModelName);
            if (!File.Exists(Path.Combine(staged, "am", "final.mdl")) || !File.Exists(Path.Combine(staged, "conf", "model.conf")))
                throw new InvalidDataException("Downloaded model is missing required recognition files.");
            var records = new List<ModelFile>();
            foreach (var file in Directory.EnumerateFiles(staged, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                await using var stream = File.OpenRead(file);
                records.Add(new(Path.GetRelativePath(staged, file).Replace('\\', '/'), stream.Length,
                    Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false))));
            }
            await File.WriteAllTextAsync(Path.Combine(staged, ManifestName), JsonSerializer.Serialize(new ModelManifest(ExpectedSha256, records)), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(ModelPath))
            {
                // Keep an invalid/custom folder for diagnosis; never recursively delete a user-selected path.
                Directory.Move(ModelPath, ModelPath + ".previous-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            }
            Directory.Move(staged, ModelPath);
            progress?.Report(1);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Model download timed out after ten minutes. Check your connection and retry."); }
        catch (HttpRequestException ex)
        { throw new InvalidOperationException("Could not download the offline model from alphacephei.com. Check your internet connection or proxy, then retry.", ex); }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            installGate.Release();
        }
    }

    public static async Task ValidateInstalledAsync(string path, CancellationToken ct)
    {
        var manifest = ReadManifest(path) ?? throw new InvalidDataException("The offline model is not installed or is invalid. Download the English model in Settings.");
        if (manifest.Files.Count is < 5 or > 1000) throw new InvalidDataException("The model manifest is invalid. Download the model again.");
        foreach (var record in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            var file = SafePath(path, record.Path);
            if (!File.Exists(file) || new FileInfo(file).Length != record.Bytes || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The offline model is incomplete. Download it again in Settings.");
            await using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
            if (!hash.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The offline model failed integrity verification. Remove the model folder and download it again in Settings.");
        }
    }

    private static ModelManifest? ReadManifest(string directory)
    {
        var path = Path.Combine(directory, ManifestName);
        if (!File.Exists(path) || new FileInfo(path).Length > 1_000_000) return null;
        var value = JsonSerializer.Deserialize<ModelManifest>(File.ReadAllText(path));
        return value?.ArchiveSha256 == ExpectedSha256 && value.Files is not null ? value : null;
    }
    private static string SafePath(string root, string entry)
    {
        if (string.IsNullOrEmpty(entry) || entry.Contains(':') || entry.Contains('\\') || entry.StartsWith('/') || entry.Split('/').Any(p => p is "." or ".."))
            throw new InvalidDataException("Unsafe path in model archive or manifest.");
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, entry));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Model entry escapes its installation directory.");
        return full;
    }
    private sealed record ModelFile(string Path, long Bytes, string Sha256);
    private sealed record ModelManifest(string ArchiveSha256, List<ModelFile> Files);
}
