namespace BootPivot.Core.Models;

public sealed record BootPivotStageOptions(
    string ImagePath,
    int ImageIndex = 1,
    string Label = "BootPivot Session",
    string? SessionId = null,
    string? WorkingRoot = null,
    string? LoaderCommand = null,
    bool DryRun = false);
