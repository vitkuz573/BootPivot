namespace BootPivot.Core.Models;

public sealed record BootPivotCleanupDriverRequest(
    string WorkingRoot,
    string? SessionId,
    TimeSpan? OlderThan,
    bool DryRun);
