namespace BootPivot.Core.Models;

public sealed record BootPivotCleanupOptions(
    string? SessionId = null,
    string? WorkingRoot = null,
    int? OlderThanDays = null,
    bool DryRun = false);
