using System.Text.RegularExpressions;

namespace BootPivot.Windows;

internal static class BcdCurrentPathParser
{
    private static readonly Regex PathRegex = new(
        "^\\s*path\\s+(.+?)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public static string? Parse(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var match = PathRegex.Match(line);
            if (match.Success)
            {
                var path = match.Groups[1].Value.Trim();
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
        }

        return null;
    }
}
