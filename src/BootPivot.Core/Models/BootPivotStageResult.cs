namespace BootPivot.Core.Models;

public sealed record BootPivotStageResult(
    BootPivotStatus Status,
    string Message,
    BootPivotSessionManifest? Manifest,
    IReadOnlyList<string> PlannedCommands);
