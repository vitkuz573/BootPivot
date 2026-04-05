using System.CommandLine;
using System.Text.Json;
using BootPivot.Cli.Infrastructure;
using BootPivot.Core.Abstractions;

namespace BootPivot.Cli.Commands;

public sealed class ImageInfoCommand
{
    private readonly IBootPivotService service;

    public ImageInfoCommand(IBootPivotService service)
    {
        this.service = service;
    }

    public Command Build()
    {
        var command = new Command("image-info", "Read image indexes and metadata from a WIM image.");

        var imageOption = new Option<string>("--image")
        {
            Description = "Path to the target WIM file.",
            Required = true
        };
        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output as JSON."
        };

        command.Add(imageOption);
        command.Add(jsonOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var imagePath = parseResult.GetValue(imageOption)!;
            var result = await service.GetImageInfoAsync(imagePath, cancellationToken);
            var json = parseResult.GetValue(jsonOption);

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine(result.Message);
                Console.WriteLine($"Image: {result.ImagePath}");
                Console.WriteLine($"Index validation available: {(result.IndexValidationAvailable ? "yes" : "no")}");

                if (result.Images.Count > 0)
                {
                    Console.WriteLine("Indexes:");
                    foreach (var image in result.Images.OrderBy(static x => x.Index))
                    {
                        Console.WriteLine($"  - {image.Index}: {image.Name ?? "(no name)"}");
                        if (!string.IsNullOrWhiteSpace(image.Description))
                        {
                            Console.WriteLine($"    {image.Description}");
                        }
                    }
                }

                if (result.Diagnostics.Count > 0)
                {
                    Console.WriteLine("Diagnostics:");
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        Console.WriteLine($"  - {diagnostic}");
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
