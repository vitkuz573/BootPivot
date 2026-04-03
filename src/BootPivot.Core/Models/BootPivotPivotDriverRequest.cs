namespace BootPivot.Core.Models;

public sealed record BootPivotPivotDriverRequest(
    string SessionId,
    string WorkingRoot,
    bool ApplyChanges,
    bool Reboot);
