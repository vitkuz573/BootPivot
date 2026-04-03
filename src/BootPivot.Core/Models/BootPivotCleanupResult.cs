namespace BootPivot.Core.Models;

public sealed record BootPivotCleanupResult(
    BootPivotStatus Status,
    string Message,
    int DeletedCount,
    IReadOnlyList<string> DeletedPaths);
