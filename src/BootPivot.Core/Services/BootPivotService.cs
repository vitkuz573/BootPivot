using System.Text.RegularExpressions;
using BootPivot.Core.Abstractions;
using BootPivot.Core.Models;
using BootPivot.Core.Templates;

namespace BootPivot.Core.Services;

public sealed class BootPivotService : IBootPivotService
{
    private const int MaxLabelLength = 80;
    private static readonly Regex SessionIdRegex = new(
        "^[a-zA-Z0-9_-]{3,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly IBootPivotDriver driver;

    public BootPivotService(IBootPivotDriver driver)
    {
        this.driver = driver;
    }

    public Task<BootPivotInspectResult> InspectAsync(CancellationToken cancellationToken)
    {
        var workingRoot = ResolveWorkingRoot(null);
        return driver.InspectAsync(workingRoot, cancellationToken);
    }

    public async Task<BootPivotImageInfoResult> GetImageInfoAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return new BootPivotImageInfoResult(
                BootPivotStatus.ValidationError,
                "Image path is required.",
                string.Empty,
                false,
                Array.Empty<BootPivotWimImageInfo>(),
                Array.Empty<string>());
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(imagePath.Trim());
        }
        catch (Exception ex)
        {
            return new BootPivotImageInfoResult(
                BootPivotStatus.ValidationError,
                $"Image path is invalid. {ex.Message}",
                imagePath,
                false,
                Array.Empty<BootPivotWimImageInfo>(),
                Array.Empty<string>());
        }

        return await driver.GetImageInfoAsync(fullPath, cancellationToken);
    }

    public async Task<BootPivotStageResult> StageAsync(BootPivotStageOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ImagePath))
        {
            return ValidationStageFailure("Image path is required.");
        }

        if (options.ImageIndex <= 0)
        {
            return ValidationStageFailure("Image index must be greater than 0.");
        }

        var label = string.IsNullOrWhiteSpace(options.Label)
            ? "BootPivot Session"
            : options.Label.Trim();

        if (label.Length > MaxLabelLength)
        {
            return ValidationStageFailure($"Label is too long. Maximum length is {MaxLabelLength}.");
        }

        var sessionId = string.IsNullOrWhiteSpace(options.SessionId)
            ? BuildSessionId()
            : options.SessionId.Trim();

        if (!SessionIdRegex.IsMatch(sessionId))
        {
            return ValidationStageFailure("Session id must match ^[a-zA-Z0-9_-]{3,64}$.");
        }

        string imagePath;
        try
        {
            imagePath = Path.GetFullPath(options.ImagePath.Trim());
        }
        catch (Exception ex)
        {
            return ValidationStageFailure($"Image path is invalid. {ex.Message}");
        }

        var imageInfo = await driver.GetImageInfoAsync(imagePath, cancellationToken);
        if (imageInfo.Status != BootPivotStatus.Success)
        {
            return new BootPivotStageResult(
                imageInfo.Status,
                $"Image validation failed. {imageInfo.Message}",
                null,
                Array.Empty<string>());
        }

        if (imageInfo.IndexValidationAvailable
            && imageInfo.Images.Count > 0
            && imageInfo.Images.All(image => image.Index != options.ImageIndex))
        {
            var availableIndexes = string.Join(", ", imageInfo.Images.Select(static image => image.Index).OrderBy(static i => i));
            return ValidationStageFailure(
                $"Image index {options.ImageIndex} was not found in '{imagePath}'. Available indexes: {availableIndexes}.");
        }

        var workingRoot = ResolveWorkingRoot(options.WorkingRoot);
        var loaderScript = BootPivotLoaderTemplateRenderer.Render(
            BootPivotLoaderTemplate.Default,
            imagePath,
            options.ImageIndex,
            label,
            options.LoaderCommand);

        var request = new BootPivotStageDriverRequest(
            sessionId,
            workingRoot,
            imagePath,
            options.ImageIndex,
            label,
            loaderScript,
            options.LoaderCommand,
            options.SystemPartition,
            options.BootSdiPath,
            options.WinloadPath,
            imageInfo.Images,
            options.DryRun);

        return await driver.StageAsync(request, cancellationToken);
    }

    public Task<BootPivotPivotResult> PivotAsync(BootPivotPivotOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.SessionId))
        {
            return Task.FromResult(new BootPivotPivotResult(
                BootPivotStatus.ValidationError,
                "Session id is required.",
                null,
                Array.Empty<string>()));
        }

        var sessionId = options.SessionId.Trim();
        if (!SessionIdRegex.IsMatch(sessionId))
        {
            return Task.FromResult(new BootPivotPivotResult(
                BootPivotStatus.ValidationError,
                "Session id must match ^[a-zA-Z0-9_-]{3,64}$.",
                null,
                Array.Empty<string>()));
        }

        var workingRoot = ResolveWorkingRoot(options.WorkingRoot);
        var request = new BootPivotPivotDriverRequest(
            sessionId,
            workingRoot,
            options.ApplyChanges,
            options.Reboot);

        return driver.PivotAsync(request, cancellationToken);
    }

    public Task<BootPivotCleanupResult> CleanupAsync(BootPivotCleanupOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.SessionId)
            && !SessionIdRegex.IsMatch(options.SessionId.Trim()))
        {
            return Task.FromResult(new BootPivotCleanupResult(
                BootPivotStatus.ValidationError,
                "Session id must match ^[a-zA-Z0-9_-]{3,64}$.",
                0,
                Array.Empty<string>()));
        }

        if (options.OlderThanDays is <= 0)
        {
            return Task.FromResult(new BootPivotCleanupResult(
                BootPivotStatus.ValidationError,
                "--older-than-days must be greater than 0.",
                0,
                Array.Empty<string>()));
        }

        var workingRoot = ResolveWorkingRoot(options.WorkingRoot);
        TimeSpan? olderThan = options.OlderThanDays.HasValue
            ? TimeSpan.FromDays(options.OlderThanDays.Value)
            : null;

        var request = new BootPivotCleanupDriverRequest(
            workingRoot,
            options.SessionId?.Trim(),
            olderThan,
            options.DryRun);

        return driver.CleanupAsync(request, cancellationToken);
    }

    private static BootPivotStageResult ValidationStageFailure(string message)
    {
        return new BootPivotStageResult(
            BootPivotStatus.ValidationError,
            message,
            null,
            Array.Empty<string>());
    }

    private static string BuildSessionId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{timestamp}-{suffix}";
    }

    private static string ResolveWorkingRoot(string? overrideRoot)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot.Trim());
        }

        var commonDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonDataPath))
        {
            commonDataPath = Environment.CurrentDirectory;
        }

        return Path.Combine(commonDataPath, "BootPivot", "sessions");
    }
}
