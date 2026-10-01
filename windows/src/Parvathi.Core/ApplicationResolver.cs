using System.Globalization;
using System.Text;

namespace Parvathi.Core;

public static class ApplicationResolver
{
    private static readonly IReadOnlyDictionary<string, string[]> Aliases = new Dictionary<string, string[]>
    {
        ["chrome"] = ["google chrome"], ["code"] = ["visual studio code"],
        ["vs code"] = ["visual studio code"], ["vscode"] = ["visual studio code"],
        ["edge"] = ["microsoft edge"], ["word"] = ["microsoft word", "word"],
        ["excel"] = ["microsoft excel", "excel"], ["powerpoint"] = ["microsoft powerpoint", "powerpoint"],
        ["terminal"] = ["windows terminal", "terminal"], ["notepad"] = ["notepad", "windows notepad"],
        ["settings"] = ["settings", "windows settings"]
    };

    public static AppDescriptor Resolve(string query, IReadOnlyList<AppDescriptor> applications)
    {
        var needle = Normalize(query);
        if (needle.Length is 0 or > 160) throw new PipelineException("Specify the name of an installed application.");
        var apps = applications.DistinctBy(a => a.LaunchTarget, StringComparer.OrdinalIgnoreCase).ToArray();
        var exact = apps.Where(a => Normalize(a.Name) == needle || string.Equals(a.Id, query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length > 0) return Unique(exact, query);
        if (Aliases.TryGetValue(needle, out var names))
        {
            var matches = apps.Where(a => names.Contains(Normalize(a.Name))).ToArray();
            if (matches.Length > 0) return Unique(matches, query);
        }
        var prefixes = apps.Where(a => Normalize(a.Name).StartsWith(needle, StringComparison.Ordinal)).ToArray();
        if (prefixes.Length > 0) return Unique(prefixes, query);
        var partials = apps.Where(a => Normalize(a.Name).Contains(needle, StringComparison.Ordinal)).ToArray();
        if (partials.Length > 0) return Unique(partials, query);
        throw new PipelineException($"I couldn’t find an installed application named ‘{query}’. Try its full name from the Start menu, or install the application first.");
    }

    private static AppDescriptor Unique(AppDescriptor[] matches, string query)
    {
        if (matches.Length == 1) return matches[0];
        var choices = string.Join(", ", matches.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Take(6).Select(a => a.Name));
        throw new PipelineException($"‘{query}’ matches several applications: {choices}. Which application do you mean? Say its full name.");
    }

    private static string Normalize(string input)
    {
        var name = (input ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        name = new string(name.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC);
        if (name.EndsWith(".exe", StringComparison.Ordinal)) name = name[..^4];
        return string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
