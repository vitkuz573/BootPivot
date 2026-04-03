namespace BootPivot.Core.Models;

public sealed record BootPivotPivotResult(
    BootPivotStatus Status,
    string Message,
    string? BootEntryId,
    IReadOnlyList<string> ExecutedCommands);
