namespace BootPivot.Core.Models;

public sealed record BootPivotStageDriverRequest(
    string SessionId,
    string WorkingRoot,
    string ImagePath,
    int ImageIndex,
    string Label,
    string LoaderScriptContent,
    string? LoaderCommand,
    bool DryRun);
