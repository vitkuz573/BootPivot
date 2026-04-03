namespace BootPivot.Core.Models;

public sealed record BootPivotInspectResult(
    BootPivotStatus Status,
    string Message,
    string Platform,
    bool IsSupported,
    bool IsElevated,
    bool BcdEditAvailable,
    string WorkingRoot,
    IReadOnlyList<string> Diagnostics);
