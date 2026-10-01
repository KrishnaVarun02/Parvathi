using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Parvathi.Windows.Services;

/// <summary>DPAPI CurrentUser storage exclusively for OpenAI. Ollama receives no API key.</summary>
public sealed class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Parvathi.Windows.OpenAI.v1");
    private readonly string path;
    public CredentialStore(string? directory = null) => path = Path.Combine(directory ?? SettingsStore.DataDirectory, "openai.credential.dpapi");

    public void Save(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 4096 || key.Any(char.IsControl)) throw new ArgumentException("Enter a valid OpenAI API key.");
        var plain = Encoding.UTF8.GetBytes(key.Trim());
        try
        {
            var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Windows could not securely save the OpenAI credential. Check access to your user profile and try again.", ex); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public string? Load()
    {
        if (!File.Exists(path)) return null;
        byte[]? plain = null;
        try
        {
            if (new FileInfo(path).Length > 16384) throw new CryptographicException("Credential record is invalid.");
            plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Windows could not unlock the OpenAI credential for this user. Remove it in Settings and enter your key again.", ex); }
        finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }
    public void Delete() { if (File.Exists(path)) File.Delete(path); }
}
