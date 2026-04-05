namespace BootPivot.Core.Models;

public sealed record BootPivotImageInfoResult(
    BootPivotStatus Status,
    string Message,
    string ImagePath,
    bool IndexValidationAvailable,
    IReadOnlyList<BootPivotWimImageInfo> Images,
    IReadOnlyList<string> Diagnostics);
