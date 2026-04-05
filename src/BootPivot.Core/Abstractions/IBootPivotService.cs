using BootPivot.Core.Models;

namespace BootPivot.Core.Abstractions;

public interface IBootPivotService
{
    Task<BootPivotInspectResult> InspectAsync(CancellationToken cancellationToken);

    Task<BootPivotImageInfoResult> GetImageInfoAsync(string imagePath, CancellationToken cancellationToken);

    Task<BootPivotStageResult> StageAsync(BootPivotStageOptions options, CancellationToken cancellationToken);

    Task<BootPivotPivotResult> PivotAsync(BootPivotPivotOptions options, CancellationToken cancellationToken);

    Task<BootPivotCleanupResult> CleanupAsync(BootPivotCleanupOptions options, CancellationToken cancellationToken);
}
