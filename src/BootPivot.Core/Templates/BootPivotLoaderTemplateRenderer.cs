using System.Globalization;

namespace BootPivot.Core.Templates;

public static class BootPivotLoaderTemplateRenderer
{
    public static string Render(
        string template,
        string imagePath,
        int imageIndex,
        string bootLabel,
        string? loaderCommand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(bootLabel);

        var rendered = template
            .Replace("<image_path>", imagePath, StringComparison.Ordinal)
            .Replace("<image_index>", imageIndex.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("<boot_label>", bootLabel, StringComparison.Ordinal);

        var resolvedLoaderCommand = loaderCommand?.Trim() ?? string.Empty;
        rendered = rendered.Replace("<some_var>", resolvedLoaderCommand, StringComparison.Ordinal);

        return rendered;
    }
}
