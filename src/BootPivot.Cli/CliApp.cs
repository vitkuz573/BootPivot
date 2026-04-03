using System.CommandLine;
using System.CommandLine.Parsing;
using BootPivot.Cli.Commands;
using BootPivot.Core.Abstractions;
using BootPivot.Core.Services;
using BootPivot.Windows.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BootPivot.Cli;

public static class CliApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBootPivotService, BootPivotService>();
        services.AddBootPivotWindows();

        services.AddSingleton<InspectCommand>();
        services.AddSingleton<StageCommand>();
        services.AddSingleton<PivotCommand>();
        services.AddSingleton<CleanupCommand>();

        using var serviceProvider = (ServiceProvider)new DefaultServiceProviderFactory(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            }).CreateServiceProvider(services);

        var rootCommand = new RootCommand("BootPivot CLI");
        rootCommand.Add(serviceProvider.GetRequiredService<InspectCommand>().Build());
        rootCommand.Add(serviceProvider.GetRequiredService<StageCommand>().Build());
        rootCommand.Add(serviceProvider.GetRequiredService<PivotCommand>().Build());
        rootCommand.Add(serviceProvider.GetRequiredService<CleanupCommand>().Build());

        var parserConfiguration = new ParserConfiguration();
        var parseResult = CommandLineParser.Parse(rootCommand, args, parserConfiguration);

        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler? cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            return await parseResult.InvokeAsync(new InvocationConfiguration(), cancellationSource.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
