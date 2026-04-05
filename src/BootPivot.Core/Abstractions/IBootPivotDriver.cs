using BootPivot.Core.Models;

namespace BootPivot.Core.Abstractions;

public interface IBootPivotDriver
{
    Task<BootPivotInspectResult> InspectAsync(string workingRoot, CancellationToken cancellationToken);

    Task<BootPivotImageInfoResult> GetImageInfoAsync(string imagePath, CancellationToken cancellationToken);

    Task<BootPivotStageResult> StageAsync(BootPivotStageDriverRequest request, CancellationToken cancellationToken);

    Task<BootPivotPivotResult> PivotAsync(BootPivotPivotDriverRequest request, CancellationToken cancellationToken);

    Task<BootPivotCleanupResult> CleanupAsync(BootPivotCleanupDriverRequest request, CancellationToken cancellationToken);
}
