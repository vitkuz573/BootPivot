using System.Text.RegularExpressions;
using BootPivot.Core.Models;

namespace BootPivot.Windows;

internal static class DismWimInfoParser
{
    private static readonly Regex IndexRegex = new(
        "^\\s*Index\\s*:\\s*(\\d+)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex NameRegex = new(
        "^\\s*Name\\s*:\\s*(.*)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex DescriptionRegex = new(
        "^\\s*Description\\s*:\\s*(.*)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<BootPivotWimImageInfo> Parse(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var result = new List<BootPivotWimImageInfo>();
        int? currentIndex = null;
        string? currentName = null;
        string? currentDescription = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var indexMatch = IndexRegex.Match(line);
            if (indexMatch.Success)
            {
                FlushCurrent();
                if (int.TryParse(indexMatch.Groups[1].Value, out var parsedIndex))
                {
                    currentIndex = parsedIndex;
                    currentName = null;
                    currentDescription = null;
                }

                continue;
            }

            var nameMatch = NameRegex.Match(line);
            if (nameMatch.Success)
            {
                currentName = NormalizeValue(nameMatch.Groups[1].Value);
                continue;
            }

            var descriptionMatch = DescriptionRegex.Match(line);
            if (descriptionMatch.Success)
            {
                currentDescription = NormalizeValue(descriptionMatch.Groups[1].Value);
            }
        }

        FlushCurrent();
        return result;

        void FlushCurrent()
        {
            if (!currentIndex.HasValue)
            {
                return;
            }

            result.Add(new BootPivotWimImageInfo(currentIndex.Value, currentName, currentDescription));
            currentIndex = null;
            currentName = null;
            currentDescription = null;
        }
    }

    private static string? NormalizeValue(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
