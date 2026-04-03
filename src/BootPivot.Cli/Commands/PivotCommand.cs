using System.CommandLine;
using System.Text.Json;
using BootPivot.Cli.Infrastructure;
using BootPivot.Core.Abstractions;
using BootPivot.Core.Models;

namespace BootPivot.Cli.Commands;

public sealed class PivotCommand
{
    private readonly IBootPivotService service;

    public PivotCommand(IBootPivotService service)
    {
        this.service = service;
    }

    public Command Build()
    {
        var command = new Command("pivot", "Queue staged image for next boot via Windows BCD.");

        var sessionOption = new Option<string>("--session")
        {
            Description = "Session id created by stage command.",
            Required = true
        };
        var workingRootOption = new Option<string?>("--work-dir")
        {
            Description = "Override working root where sessions are stored."
        };
        var applyOption = new Option<bool>("--apply")
        {
            Description = "Execute BCD commands. Without this flag command only previews."
        };
        var rebootOption = new Option<bool>("--reboot")
        {
            Description = "Reboot immediately after queuing boot sequence."
        };
        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output as JSON."
        };

        command.Add(sessionOption);
        command.Add(workingRootOption);
        command.Add(applyOption);
        command.Add(rebootOption);
        command.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var options = new BootPivotPivotOptions(
                parseResult.GetValue(sessionOption)!,
                parseResult.GetValue(workingRootOption),
                parseResult.GetValue(applyOption),
                parseResult.GetValue(rebootOption));

            var result = await service.PivotAsync(options, cancellationToken);
            var json = parseResult.GetValue(jsonOption);
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine(result.Message);
                if (!string.IsNullOrWhiteSpace(result.BootEntryId))
                {
                    Console.WriteLine($"Boot entry id: {result.BootEntryId}");
                }

                if (result.ExecutedCommands.Count > 0)
                {
                    Console.WriteLine("Commands:");
                    foreach (var commandLine in result.ExecutedCommands)
                    {
                        Console.WriteLine($"  - {commandLine}");
                    }
                }
            }

            var exitCode = ExitCodeMapper.FromStatus(result.Status);
            Environment.ExitCode = exitCode;
            return exitCode;
        });

        return command;
    }
}
