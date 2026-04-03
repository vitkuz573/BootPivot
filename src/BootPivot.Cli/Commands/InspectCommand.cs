using System.CommandLine;
using System.Text.Json;
using BootPivot.Cli.Infrastructure;
using BootPivot.Core.Abstractions;

namespace BootPivot.Cli.Commands;

public sealed class InspectCommand
{
    private readonly IBootPivotService service;

    public InspectCommand(IBootPivotService service)
    {
        this.service = service;
    }

    public Command Build()
    {
        var command = new Command("inspect", "Inspect environment readiness for BootPivot operations.");
        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output as JSON."
        };
        command.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var json = parseResult.GetValue(jsonOption);
            var inspectResult = await service.InspectAsync(cancellationToken);
            if (json)
            {
                var payload = JsonSerializer.Serialize(inspectResult, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(payload);
            }
            else
            {
                Console.WriteLine(inspectResult.Message);
                Console.WriteLine($"Platform: {inspectResult.Platform}");
                Console.WriteLine($"Supported: {(inspectResult.IsSupported ? "yes" : "no")}");
                Console.WriteLine($"Elevated: {(inspectResult.IsElevated ? "yes" : "no")}");
                Console.WriteLine($"BCD edit available: {(inspectResult.BcdEditAvailable ? "yes" : "no")}");
                Console.WriteLine($"Working root: {inspectResult.WorkingRoot}");

                if (inspectResult.Diagnostics.Count > 0)
                {
                    Console.WriteLine("Diagnostics:");
                    foreach (var diagnostic in inspectResult.Diagnostics)
                    {
                        Console.WriteLine($"  - {diagnostic}");
                    }
                }
            }

            var exitCode = ExitCodeMapper.FromStatus(inspectResult.Status);
            Environment.ExitCode = exitCode;
            return exitCode;
        });

        return command;
    }
}
