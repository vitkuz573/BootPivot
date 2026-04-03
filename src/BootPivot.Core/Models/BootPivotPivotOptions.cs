namespace BootPivot.Core.Models;

public sealed record BootPivotPivotOptions(
    string SessionId,
    string? WorkingRoot = null,
    bool ApplyChanges = false,
    bool Reboot = false);
