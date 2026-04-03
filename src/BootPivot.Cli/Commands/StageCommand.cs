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
            Description = "Command that replaces <some_var> in loader template."
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
        command.Add(dryRunOption);
        command.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var options = new BootPivotStageOptions(
                parseResult.GetValue(imageOption)!,
                parseResult.GetValue(indexOption),
                parseResult.GetValue(labelOption)!,
                parseResult.GetValue(sessionOption),
                parseResult.GetValue(workingRootOption),
                parseResult.GetValue(loaderCommandOption),
                parseResult.GetValue(dryRunOption));

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
