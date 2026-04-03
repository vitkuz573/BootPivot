using BootPivot.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BootPivot.Windows.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBootPivotWindows(this IServiceCollection services)
    {
        services.AddSingleton<IProcessExecutor, ProcessExecutor>();
        services.AddSingleton<IBootPivotDriver, WindowsBootPivotDriver>();
        return services;
    }
}
