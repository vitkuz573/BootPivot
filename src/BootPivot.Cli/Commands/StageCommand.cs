using System.CommandLine;
using System.Text.Json;
using BootPivot.Cli.Infrastructure;
using BootPivot.Core.Abstractions;
using BootPivot.Core.Models;

namespace BootPivot.Cli.Commands;

public sealed class StageCommand
{
    private readonly IBootPivotService service;

    public StageCommand(IBootPivotService service)
    {
        this.service = service;
    }

    public Command Build()
    {
        var command = new Command("stage", "Create BootPivot session artifacts and boot command plan.");

        var imageOption = new Option<string>("--image")
        {
            Description = "Path to the target image file (for example C:\\images\\boot.wim).",
            Required = true
        };
        var indexOption = new Option<int>("--index")
        {
            Description = "Image index inside the image file.",
            DefaultValueFactory = _ => 1
        };
        var labelOption = new Option<string>("--label")
        {
            Description = "Boot menu label for the temporary entry.",
            DefaultValueFactory = _ => "BootPivot Session"
        };
        var sessionOption = new Option<string?>("--session")
        {
            Description = "Optional explicit session id."
        };
        var workingRootOption = new Option<string?>("--work-dir")
        {
            Description = "Override working root where sessions are stored."
        };
        var loaderCommandOption = new Option<string?>("--loader-command")
        {
            Description = "Optional command injected into the loader script."
        };
        var systemPartitionOption = new Option<string?>("--system-partition")
        {
            Description = "System partition containing boot.sdi (for example C:)."
        };
        var bootSdiPathOption = new Option<string?>("--boot-sdi")
        {
            Description = "Path to boot.sdi on the system partition (for example \\boot\\boot.sdi)."
        };
        var winloadPathOption = new Option<string?>("--winload")
        {
            Description = "Winload path inside the image (for example \\Windows\\System32\\winload.efi)."
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Preview only. Do not write files."
        };
        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output as JSON."
        };

        command.Add(imageOption);
        command.Add(indexOption);
        command.Add(labelOption);
        command.Add(sessionOption);
        command.Add(workingRootOption);
        command.Add(loaderCommandOption);
        command.Add(systemPartitionOption);
        command.Add(bootSdiPathOption);
        command.Add(winloadPathOption);
        command.Add(dryRunOption);
        command.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var options = new BootPivotStageOptions(
                ImagePath: parseResult.GetValue(imageOption)!,
                ImageIndex: parseResult.GetValue(indexOption),
                Label: parseResult.GetValue(labelOption)!,
                SessionId: parseResult.GetValue(sessionOption),
                WorkingRoot: parseResult.GetValue(workingRootOption),
                LoaderCommand: parseResult.GetValue(loaderCommandOption),
                SystemPartition: parseResult.GetValue(systemPartitionOption),
                BootSdiPath: parseResult.GetValue(bootSdiPathOption),
                WinloadPath: parseResult.GetValue(winloadPathOption),
                DryRun: parseResult.GetValue(dryRunOption));

            var result = await service.StageAsync(options, cancellationToken);
            var json = parseResult.GetValue(jsonOption);
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine(result.Message);

                if (result.Manifest is not null)
                {
                    Console.WriteLine($"Session: {result.Manifest.SessionId}");
                    Console.WriteLine($"Image: {result.Manifest.ImagePath}");
                    Console.WriteLine($"Index: {result.Manifest.ImageIndex}");
                    Console.WriteLine($"Label: {result.Manifest.Label}");
                    Console.WriteLine($"Loader script: {result.Manifest.LoaderScriptPath}");
                    Console.WriteLine($"System partition: {result.Manifest.SystemPartition ?? "n/a"}");
                    Console.WriteLine($"Boot.sdi path: {result.Manifest.BootSdiPath ?? "n/a"}");
                    Console.WriteLine($"Winload path: {result.Manifest.WinloadPath ?? "n/a"}");
                }

                if (result.PlannedCommands.Count > 0)
                {
                    Console.WriteLine("Planned commands:");
                    foreach (var commandLine in result.PlannedCommands)
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
