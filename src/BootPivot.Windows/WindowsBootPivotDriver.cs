using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using BootPivot.Core.Abstractions;
using BootPivot.Core.Models;

namespace BootPivot.Windows;

public sealed class WindowsBootPivotDriver : IBootPivotDriver
{
    private const string ManifestFileName = "manifest.json";
    private const string LoaderScriptFileName = "loader.cmd";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly Regex BootEntryRegex = new(
        "\\{[0-9a-fA-F-]{36}\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly IProcessExecutor processExecutor;

    public WindowsBootPivotDriver(IProcessExecutor processExecutor)
    {
        this.processExecutor = processExecutor;
    }

    public Task<BootPivotInspectResult> InspectAsync(string workingRoot, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var platform = RuntimeInformation.OSDescription;
        var isWindows = OperatingSystem.IsWindows();
        var bcdEditAvailable = IsExecutableAvailable("bcdedit.exe");
        var reagentcAvailable = IsExecutableAvailable("reagentc.exe");
        var isElevated = isWindows && IsProcessElevated();

        var diagnostics = new List<string>
        {
            $"working-root: {workingRoot}",
            $"platform: {platform}",
            $"bcdedit: {(bcdEditAvailable ? "available" : "missing")}",
            $"reagentc: {(reagentcAvailable ? "available" : "missing")}",
            $"elevated: {(isElevated ? "yes" : "no")}"
        };

        var isSupported = isWindows && bcdEditAvailable;
        var status = isSupported ? BootPivotStatus.Success : BootPivotStatus.NotSupported;
        var message = isSupported
            ? "BootPivot environment is ready."
            : "BootPivot pivot operations require Windows with bcdedit available.";

        return Task.FromResult(new BootPivotInspectResult(
            status,
            message,
            platform,
            isSupported,
            isElevated,
            bcdEditAvailable,
            workingRoot,
            diagnostics));
    }

    public async Task<BootPivotStageResult> StageAsync(BootPivotStageDriverRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionDirectory = BuildSessionDirectory(request.WorkingRoot, request.SessionId);
        var loaderPath = Path.Combine(sessionDirectory, LoaderScriptFileName);

        var manifest = new BootPivotSessionManifest(
            request.SessionId,
            request.ImagePath,
            request.ImageIndex,
            request.Label,
            loaderPath,
            request.LoaderCommand,
            null,
            DateTimeOffset.UtcNow,
            null);

        if (!TryBuildRamdiskDevice(request.ImagePath, out var ramdiskDevice, out var ramdiskError))
        {
            return new BootPivotStageResult(
                BootPivotStatus.ValidationError,
                ramdiskError,
                manifest,
                Array.Empty<string>());
        }

        var plannedCommands = BuildPlannedPivotCommands(request.Label, ramdiskDevice, "<new_entry_guid>", reboot: false);

        if (request.DryRun)
        {
            return new BootPivotStageResult(
                BootPivotStatus.Success,
                "Dry-run: staging preview generated.",
                manifest,
                plannedCommands);
        }

        try
        {
            Directory.CreateDirectory(sessionDirectory);
            await File.WriteAllTextAsync(loaderPath, request.LoaderScriptContent, cancellationToken);

            var manifestPath = Path.Combine(sessionDirectory, ManifestFileName);
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);

            return new BootPivotStageResult(
                BootPivotStatus.Success,
                $"Session '{request.SessionId}' staged at '{sessionDirectory}'.",
                manifest,
                plannedCommands);
        }
        catch (Exception ex)
        {
            return new BootPivotStageResult(
                BootPivotStatus.Failed,
                $"Failed to stage session '{request.SessionId}'. {ex.Message}",
                manifest,
                plannedCommands);
        }
    }

    public async Task<BootPivotPivotResult> PivotAsync(BootPivotPivotDriverRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionDirectory = BuildSessionDirectory(request.WorkingRoot, request.SessionId);
        var manifestPath = Path.Combine(sessionDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return new BootPivotPivotResult(
                BootPivotStatus.NotFound,
                $"Session manifest not found: {manifestPath}",
                null,
                Array.Empty<string>());
        }

        BootPivotSessionManifest? manifest;
        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            manifest = JsonSerializer.Deserialize<BootPivotSessionManifest>(manifestJson, JsonOptions);
        }
        catch (Exception ex)
        {
            return new BootPivotPivotResult(
                BootPivotStatus.Failed,
                $"Failed to read session manifest. {ex.Message}",
                null,
                Array.Empty<string>());
        }

        if (manifest is null)
        {
            return new BootPivotPivotResult(
                BootPivotStatus.Failed,
                "Session manifest is empty or invalid.",
                null,
                Array.Empty<string>());
        }

        if (!TryBuildRamdiskDevice(manifest.ImagePath, out var ramdiskDevice, out var ramdiskError))
        {
            return new BootPivotPivotResult(
                BootPivotStatus.ValidationError,
                ramdiskError,
                null,
                Array.Empty<string>());
        }

        var previewCommands = BuildPlannedPivotCommands(manifest.Label, ramdiskDevice, "<new_entry_guid>", request.Reboot);
        if (!request.ApplyChanges)
        {
            return new BootPivotPivotResult(
                BootPivotStatus.Success,
                "Pivot preview generated. Re-run with --apply to execute BCD changes.",
                manifest.BootEntryId,
                previewCommands);
        }

        if (!OperatingSystem.IsWindows())
        {
            return new BootPivotPivotResult(
                BootPivotStatus.NotSupported,
                "Pivot execution is only supported on Windows.",
                null,
                previewCommands);
        }

        if (!IsExecutableAvailable("bcdedit.exe"))
        {
            return new BootPivotPivotResult(
                BootPivotStatus.NotSupported,
                "bcdedit.exe is not available in PATH.",
                null,
                previewCommands);
        }

        if (!IsProcessElevated())
        {
            return new BootPivotPivotResult(
                BootPivotStatus.PermissionDenied,
                "Pivot execution requires an elevated shell (Administrator).",
                null,
                previewCommands);
        }

        var executedCommands = new List<string>();

        var createCommand = new CommandSpec("bcdedit", ["/create", "/d", manifest.Label, "/application", "osloader"]);
        var createResult = await processExecutor.ExecuteAsync(createCommand.FileName, createCommand.Arguments, cancellationToken);
        executedCommands.Add(FormatCommand(createCommand));
        if (createResult.ExitCode != 0)
        {
            return BuildCommandFailure("Failed to create BCD entry.", createResult, executedCommands);
        }

        var bootEntryId = TryExtractBootEntryId(createResult.StandardOutput, createResult.StandardError);
        if (bootEntryId is null)
        {
            return new BootPivotPivotResult(
                BootPivotStatus.Failed,
                "Failed to parse newly created BCD entry identifier from bcdedit output.",
                null,
                executedCommands);
        }

        var remainingCommands = BuildPivotCommandSpecs(bootEntryId, ramdiskDevice, request.Reboot);
        foreach (var command in remainingCommands)
        {
            var result = await processExecutor.ExecuteAsync(command.FileName, command.Arguments, cancellationToken);
            executedCommands.Add(FormatCommand(command));
            if (result.ExitCode != 0)
            {
                return BuildCommandFailure(
                    $"Command failed: {FormatCommand(command)}",
                    result,
                    executedCommands);
            }
        }

        try
        {
            var updatedManifest = manifest with
            {
                BootEntryId = bootEntryId,
                LastPivotedUtc = DateTimeOffset.UtcNow
            };

            var updatedManifestJson = JsonSerializer.Serialize(updatedManifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, updatedManifestJson, cancellationToken);
        }
        catch (Exception ex)
        {
            return new BootPivotPivotResult(
                BootPivotStatus.Failed,
                $"Pivot executed, but failed to update session manifest. {ex.Message}",
                bootEntryId,
                executedCommands);
        }

        return new BootPivotPivotResult(
            BootPivotStatus.Success,
            "BootPivot entry created and queued for next boot.",
            bootEntryId,
            executedCommands);
    }

    public Task<BootPivotCleanupResult> CleanupAsync(BootPivotCleanupDriverRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        ArgumentNullException.ThrowIfNull(request);

        var deletedPaths = new List<string>();
        var targets = ResolveCleanupTargets(request).ToList();

        if (targets.Count == 0)
        {
            var status = string.IsNullOrWhiteSpace(request.SessionId)
                ? BootPivotStatus.Success
                : BootPivotStatus.NotFound;
            var message = string.IsNullOrWhiteSpace(request.SessionId)
                ? "No sessions matched cleanup criteria."
                : $"Session '{request.SessionId}' was not found.";

            return Task.FromResult(new BootPivotCleanupResult(status, message, 0, Array.Empty<string>()));
        }

        if (request.DryRun)
        {
            return Task.FromResult(new BootPivotCleanupResult(
                BootPivotStatus.Success,
                $"Dry-run: {targets.Count} session(s) selected for cleanup.",
                targets.Count,
                targets));
        }

        foreach (var target in targets)
        {
            try
            {
                Directory.Delete(target, recursive: true);
                deletedPaths.Add(target);
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BootPivotCleanupResult(
                    BootPivotStatus.Failed,
                    $"Failed to delete '{target}'. {ex.Message}",
                    deletedPaths.Count,
                    deletedPaths));
            }
        }

        return Task.FromResult(new BootPivotCleanupResult(
            BootPivotStatus.Success,
            $"Deleted {deletedPaths.Count} session(s).",
            deletedPaths.Count,
            deletedPaths));
    }

    private static IEnumerable<string> ResolveCleanupTargets(BootPivotCleanupDriverRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var target = BuildSessionDirectory(request.WorkingRoot, request.SessionId.Trim());
            if (Directory.Exists(target))
            {
                yield return target;
            }

            yield break;
        }

        if (!Directory.Exists(request.WorkingRoot))
        {
            yield break;
        }

        var thresholdUtc = request.OlderThan.HasValue
            ? DateTimeOffset.UtcNow - request.OlderThan.Value
            : (DateTimeOffset?)null;

        foreach (var directory in Directory.GetDirectories(request.WorkingRoot))
        {
            if (thresholdUtc.HasValue)
            {
                var lastWriteUtc = new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory), TimeSpan.Zero);
                if (lastWriteUtc > thresholdUtc.Value)
                {
                    continue;
                }
            }

            yield return directory;
        }
    }

    private static BootPivotPivotResult BuildCommandFailure(
        string message,
        ProcessExecutionResult result,
        IReadOnlyList<string> executedCommands)
    {
        var diagnostics = new List<string> { message };

        if (result.StandardOutput.Count > 0)
        {
            diagnostics.AddRange(result.StandardOutput.Select(line => $"stdout: {line}"));
        }

        if (result.StandardError.Count > 0)
        {
            diagnostics.AddRange(result.StandardError.Select(line => $"stderr: {line}"));
        }

        diagnostics.Add($"exit-code: {result.ExitCode}");

        return new BootPivotPivotResult(
            BootPivotStatus.Failed,
            string.Join(Environment.NewLine, diagnostics),
            null,
            executedCommands);
    }

    private static string? TryExtractBootEntryId(
        IReadOnlyList<string> standardOutput,
        IReadOnlyList<string> standardError)
    {
        foreach (var line in standardOutput.Concat(standardError))
        {
            var match = BootEntryRegex.Match(line);
            if (match.Success)
            {
                return match.Value;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> BuildPlannedPivotCommands(
        string label,
        string ramdiskDevice,
        string bootEntryId,
        bool reboot)
    {
        var commands = new List<CommandSpec>
        {
            new("bcdedit", ["/create", "/d", label, "/application", "osloader"])
        };

        commands.AddRange(BuildPivotCommandSpecs(bootEntryId, ramdiskDevice, reboot));
        return commands.Select(FormatCommand).ToArray();
    }

    private static IReadOnlyList<CommandSpec> BuildPivotCommandSpecs(
        string bootEntryId,
        string ramdiskDevice,
        bool reboot)
    {
        var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        if (string.IsNullOrWhiteSpace(systemDrive))
        {
            systemDrive = "C:";
        }

        var commands = new List<CommandSpec>
        {
            new("bcdedit", ["/set", "{ramdiskoptions}", "ramdisksdidevice", $"partition={systemDrive}"]),
            new("bcdedit", ["/set", "{ramdiskoptions}", "ramdisksdipath", "\\boot\\boot.sdi"]),
            new("bcdedit", ["/set", bootEntryId, "device", ramdiskDevice]),
            new("bcdedit", ["/set", bootEntryId, "osdevice", ramdiskDevice]),
            new("bcdedit", ["/set", bootEntryId, "systemroot", "\\Windows"]),
            new("bcdedit", ["/set", bootEntryId, "winpe", "yes"]),
            new("bcdedit", ["/set", bootEntryId, "detecthal", "yes"]),
            new("bcdedit", ["/bootsequence", bootEntryId])
        };

        if (reboot)
        {
            commands.Add(new CommandSpec("shutdown", ["/r", "/t", "0"]));
        }

        return commands;
    }

    private static bool TryBuildRamdiskDevice(string imagePath, out string ramdiskDevice, out string error)
    {
        ramdiskDevice = string.Empty;
        error = string.Empty;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(imagePath);
        }
        catch (Exception ex)
        {
            error = $"Invalid image path '{imagePath}'. {ex.Message}";
            return false;
        }

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) || root.Length < 2 || root[1] != ':')
        {
            error = "Image path must be on a local drive (for example C:\\images\\boot.wim).";
            return false;
        }

        var drive = root[..2];
        var relativePath = fullPath[root.Length..].Replace('/', '\\');
        if (!relativePath.StartsWith('\\'))
        {
            relativePath = "\\" + relativePath;
        }

        ramdiskDevice = $"ramdisk=[{drive}]{relativePath},{{ramdiskoptions}}";
        return true;
    }

    private static string FormatCommand(CommandSpec command)
    {
        var args = command.Arguments.Select(QuoteIfNeeded);
        return $"{command.FileName} {string.Join(' ', args)}";
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal)
            ? $"\"{value}\""
            : value;
    }

    private static string BuildSessionDirectory(string workingRoot, string sessionId)
    {
        return Path.Combine(workingRoot, sessionId);
    }

    private static bool IsExecutableAvailable(string executable)
    {
        var explicitLocations = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), executable),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", executable)
        };

        foreach (var location in explicitLocations)
        {
            if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
            {
                return true;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), executable);
            if (File.Exists(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private sealed record CommandSpec(string FileName, IReadOnlyList<string> Arguments);
}
