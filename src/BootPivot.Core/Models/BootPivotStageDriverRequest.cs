namespace BootPivot.Core.Models;

public sealed record BootPivotStageDriverRequest(
    string SessionId,
    string WorkingRoot,
    string ImagePath,
    int ImageIndex,
    string Label,
    string LoaderScriptContent,
    string? LoaderCommand,
    string? SystemPartition,
    string? BootSdiPath,
    string? WinloadPath,
    IReadOnlyList<BootPivotWimImageInfo> Images,
    bool DryRun);
