using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Parvathi.Core;

/// <summary>A bounded, deterministic grammar. No model output is ever executable code.</summary>
public static class CommandRouter
{
    public static CommandAction Route(string text)
    {
        var command = text?.Trim() ?? "";
        if (command.Length == 0) throw new PipelineException("Say one supported command, such as ‘Open Chrome’.");

        // The rest of a Type command is data, even if it contains another command.
        var supplied = Capture(@"^type(?:\s*:\s*|\s+)([\s\S]+)$", command);
        if (supplied is not null) return Validate(new TypeText(supplied));
        if (Encoding.UTF8.GetByteCount(command) > 4096)
            throw new PipelineException("That command is too long. Say a shorter command.");
        if (command.Any(char.IsControl)) throw new PipelineException("Say one command at a time.");
        if (Matches(@"(?:;\s*|&&\s*|\|\|\s*|\bthen\s+|\band\s+)(?:open|switch|launch|search|set|mute|unmute|type|run|delete|send|buy)\b", command))
            throw new PipelineException("Say one command at a time. Multi-step commands are not enabled.");

        if (Matches(@"^(?:mute|mute (?:the )?(?:system |computer )?(?:audio|sound|volume))[.!?]?$", command))
            return new SetMuted(true);
        if (Matches(@"^(?:unmute|unmute (?:the )?(?:system |computer )?(?:audio|sound|volume))[.!?]?$", command))
            return new SetMuted(false);
        var amount = Capture(@"^(?:set\s+)?(?:the\s+)?(?:system\s+)?volume\s+(?:to\s+)?(.+?)(?:\s*percent|\s*%)?[.!?]?$", command);
        if (amount is not null)
        {
            var percent = ParsePercentage(amount);
            if (percent is null) throw VolumeError();
            return Validate(new SetVolume(percent.Value));
        }
        var query = Capture(@"^search\s+(?:(?:the\s+)?web\s+)?for\s+(.+)$", command);
        if (query is not null) return Validate(new SearchWeb(query.Trim()));
        var address = Capture(@"^(?:open\s+(?:the\s+)?(?:website|url)|go\s+to|visit)\s+(.+)$", command);
        if (address is not null) return new OpenWebsite(ValidatedWebsite(address));
        var target = Capture(@"^(?:open|launch|switch\s+to|focus)\s+(?:(?:the\s+)?app(?:lication)?\s+)?(.+)$", command);
        if (target is not null)
        {
            target = target.Trim();
            if (LooksLikeWebsite(target)) return new OpenWebsite(ValidatedWebsite(target));
            return Validate(new OpenApplication(target.TrimEnd('.', '!', '?').Trim()));
        }
        throw new PipelineException("I couldn’t match that command. Try ‘Open Chrome’, ‘Search the web for …’, ‘Set volume to 30 percent’, ‘Mute’, or ‘Type: …’.");
    }

    /// <summary>Validate again at the OS boundary, including actions constructed outside the grammar.</summary>
    public static CommandAction Validate(CommandAction action)
    {
        switch (action)
        {
            case OpenApplication app:
                if (string.IsNullOrWhiteSpace(app.Query) || app.Query.Length > 160 || app.Query.Any(char.IsControl)
                    || Matches(@"(?:\band\b|\bthen\b|[;/\\|<>`$:])", app.Query))
                    throw new PipelineException("Specify one installed application by name, such as ‘Open Chrome’.");
                break;
            case OpenWebsite website:
                if (website.Url is null || !website.Url.IsAbsoluteUri)
                    throw new PipelineException("Provide a complete HTTP or HTTPS website address.");
                _ = ValidatedWebsite(website.Url.OriginalString);
                break;
            case SearchWeb search:
                if (string.IsNullOrWhiteSpace(search.Query) || Encoding.UTF8.GetByteCount(search.Query) > 2000 || search.Query.Any(char.IsControl))
                    throw new PipelineException("Provide a search phrase shorter than 2,000 bytes without control characters.");
                break;
            case SetVolume volume:
                if (volume.Percent is < 0 or > 100) throw VolumeError();
                break;
            case SetMuted: break;
            case TypeText type:
                if (string.IsNullOrWhiteSpace(type.Text) || Encoding.UTF8.GetByteCount(type.Text) > 50_000)
                    throw new PipelineException("Supply text to type, shorter than 50,000 bytes.");
                if (type.Text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
                    throw new PipelineException("The text contains unsupported control characters.");
                break;
            default: throw new PipelineException("That computer action is not supported.");
        }
        return action;
    }

    public static Uri ValidatedWebsite(string input)
    {
        var address = input?.Trim() ?? "";
        if (address.Length == 0 || Encoding.UTF8.GetByteCount(address) > 2048
            || address.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || address.Contains('\\'))
            throw new PipelineException("Provide one website address without spaces, such as https://example.com.");
        var value = Matches(@"^[A-Za-z][A-Za-z0-9+.-]*:", address) ? address : "https://" + address;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not "https" and not "http"
            || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || uri.Port is < 1 or > 65535
            || uri.Host.Contains("..", StringComparison.Ordinal) || uri.Host.Contains('%')
            || uri.HostNameType == UriHostNameType.Unknown)
            throw new PipelineException("Only HTTP and HTTPS websites with valid hostnames and without embedded credentials are supported.");
        return uri;
    }

    private static bool LooksLikeWebsite(string target)
    {
        if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return true;
        var candidate = target.TrimEnd('.', '!', '?');
        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        return !candidate.Contains(' ') && candidate.Contains('.');
    }

    private static int? ParsePercentage(string input)
    {
        var value = input.Trim().ToLowerInvariant();
        if (Matches(@"^\d{1,3}$", value) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric)) return numeric;
        string[] units = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
        string[] tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];
        if (value is "one hundred" or "a hundred" or "hundred") return 100;
        var unit = Array.IndexOf(units, value);
        if (unit >= 0) return unit;
        var ten = Array.IndexOf(tens, value);
        if (ten >= 2) return ten * 10;
        var words = value.Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 2 && (ten = Array.IndexOf(tens, words[0])) >= 2
            && (unit = Array.IndexOf(units, words[1])) is >= 1 and <= 9) return ten * 10 + unit;
        return null;
    }

    private static PipelineException VolumeError() => new("Choose a volume from 0 to 100 percent, such as ‘Set volume to 30 percent’.");
    private static Match Match(string pattern, string text) => Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static bool Matches(string pattern, string text) => Match(pattern, text).Success;
    private static string? Capture(string pattern, string text) { var match = Match(pattern, text); return match.Success ? match.Groups[1].Value : null; }
}

/// <summary>Mode is a user choice. Speech content cannot switch it.</summary>
public static class ModeRouter
{
    public static CommandAction? Route(InteractionMode mode, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new PipelineException("No speech was recognized. Try recording again.");
        if (Encoding.UTF8.GetByteCount(text) > 100_000) throw new PipelineException("The transcript is too long. Record a shorter passage.");
        if (!Enum.IsDefined(mode)) throw new PipelineException("Choose a supported interaction mode.");
        return mode == InteractionMode.Command ? CommandRouter.Route(text) : null;
    }
}
