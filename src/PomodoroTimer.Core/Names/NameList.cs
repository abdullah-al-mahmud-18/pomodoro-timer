namespace PomodoroTimer.Core.Names;

/// <summary>
/// The session names the user keeps in names.txt next to the db: one name per line. The file is the source of
/// truth — the app reads it at startup and never writes names into it.
/// </summary>
public static class NameList
{
    /// <summary>Trimmed, non-blank lines in file order, without case-insensitive duplicates (the first spelling wins).</summary>
    public static IReadOnlyList<string> Parse(string text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            // Trim also drops '\r' (CRLF files) and a leading BOM.
            var name = line.Trim().Trim('﻿').Trim();
            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>Reads the names file, creating it empty first if it doesn't exist so the user can find and edit it.</summary>
    public static IReadOnlyList<string> LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, string.Empty);
            return Array.Empty<string>();
        }

        return Parse(File.ReadAllText(path));
    }

    /// <summary>The list's own spelling of <paramref name="input"/> (matched case-insensitively), or null if it isn't in the list.</summary>
    public static string? Find(IEnumerable<string> names, string? input)
    {
        var trimmed = input?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : names.FirstOrDefault(name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
