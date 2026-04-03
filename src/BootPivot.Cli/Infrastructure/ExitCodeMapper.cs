using BootPivot.Core.Models;

namespace BootPivot.Cli.Infrastructure;

internal static class ExitCodeMapper
{
    public static int FromStatus(BootPivotStatus status)
    {
        return status switch
        {
            BootPivotStatus.Success => 0,
            BootPivotStatus.ValidationError => 2,
            BootPivotStatus.NotSupported => 3,
            BootPivotStatus.PermissionDenied => 4,
            BootPivotStatus.NotFound => 5,
            _ => 1
        };
    }
}
