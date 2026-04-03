using System.CommandLine;
using System.Text.Json;
using BootPivot.Cli.Infrastructure;
using BootPivot.Core.Abstractions;
using BootPivot.Core.Models;

namespace BootPivot.Cli.Commands;

public sealed class CleanupCommand
{
    private readonly IBootPivotService service;

    public CleanupCommand(IBootPivotService service)
    {
        this.service = service;
    }

    public Command Build()
    {
        var command = new Command("cleanup", "Delete staged BootPivot sessions.");

        var sessionOption = new Option<string?>("--session")
        {
            Description = "Delete only the specified session id."
        };
        var workingRootOption = new Option<string?>("--work-dir")
        {
            Description = "Override working root where sessions are stored."
        };
        var olderThanDaysOption = new Option<int?>("--older-than-days")
        {
            Description = "Delete sessions older than N days."
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Preview selected sessions without deleting."
        };
        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output as JSON."
        };

        command.Add(sessionOption);
        command.Add(workingRootOption);
        command.Add(olderThanDaysOption);
        command.Add(dryRunOption);
        command.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var options = new BootPivotCleanupOptions(
                parseResult.GetValue(sessionOption),
                parseResult.GetValue(workingRootOption),
                parseResult.GetValue(olderThanDaysOption),
                parseResult.GetValue(dryRunOption));

            var result = await service.CleanupAsync(options, cancellationToken);
            var json = parseResult.GetValue(jsonOption);
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine(result.Message);
                Console.WriteLine($"Deleted count: {result.DeletedCount}");

                if (result.DeletedPaths.Count > 0)
                {
                    Console.WriteLine("Targets:");
                    foreach (var path in result.DeletedPaths)
                    {
                        Console.WriteLine($"  - {path}");
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
