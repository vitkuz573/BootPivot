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
    private const string DefaultBootSdiPath = "\\boot\\boot.sdi";
    private const string DefaultWinloadEfiPath = "\\Windows\\System32\\winload.efi";
    private const string DefaultWinloadExePath = "\\Windows\\System32\\winload.exe";

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

    public async Task<BootPivotInspectResult> InspectAsync(string workingRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingRoot);

        var resolvedWorkingRoot = Path.GetFullPath(workingRoot);
        var platform = RuntimeInformation.OSDescription;
        var isWindows = OperatingSystem.IsWindows();
        var bcdEditAvailable = IsExecutableAvailable("bcdedit.exe");
        var reagentcAvailable = IsExecutableAvailable("reagentc.exe");
        var dismAvailable = IsExecutableAvailable("dism.exe");
        var isElevated = isWindows && IsProcessElevated();

        var systemPartition = ResolveSystemPartition(null);
        var bootSdiPath = DefaultBootSdiPath;
        var bootSdiFullPath = BuildAbsolutePath(systemPartition, bootSdiPath);
        var bootSdiAvailable = isWindows && File.Exists(bootSdiFullPath);

        var recommendedWinloadPath = await ResolveRecommendedWinloadPathAsync(cancellationToken);

        var diagnostics = new List<string>
        {
            $"working-root: {resolvedWorkingRoot}",
            $"platform: {platform}",
            $"bcdedit: {(bcdEditAvailable ? "available" : "missing")}",
            $"dism: {(dismAvailable ? "available" : "missing")}",
            $"reagentc: {(reagentcAvailable ? "available" : "missing")}",
            $"elevated: {(isElevated ? "yes" : "no")}",
            $"system-partition: {systemPartition}",
            $"boot.sdi: {(bootSdiAvailable ? "available" : "missing")}",
            $"boot.sdi-path: {bootSdiFullPath}",
            $"recommended-winload-path: {recommendedWinloadPath}"
        };

        var isSupported = isWindows && bcdEditAvailable && dismAvailable && bootSdiAvailable;
        var status = isSupported ? BootPivotStatus.Success : BootPivotStatus.NotSupported;
        var message = isSupported
            ? "BootPivot environment is ready."
            : "BootPivot requires Windows, bcdedit, dism, and an accessible boot.sdi.";

        return new BootPivotInspectResult(
            status,
            message,
            platform,
            isSupported,
            isElevated,
            bcdEditAvailable,
            dismAvailable,
            bootSdiAvailable,
            bootSdiFullPath,
            recommendedWinloadPath,
            resolvedWorkingRoot,
            diagnostics);
    }

    public async Task<BootPivotImageInfoResult> GetImageInfoAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return new BootPivotImageInfoResult(
                BootPivotStatus.ValidationError,
                "Image path is required.",
                string.Empty,
                false,
                Array.Empty<BootPivotWimImageInfo>(),
                Array.Empty<string>());
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(imagePath.Trim());
        }
        catch (Exception ex)
        {
            return new BootPivotImageInfoResult(
                BootPivotStatus.ValidationError,
                $"Image path is invalid. {ex.Message}",
                imagePath,
                false,
                Array.Empty<BootPivotWimImageInfo>(),
                Array.Empty<string>());
        }

        var diagnostics = new List<string>
        {
            $"image-path: {fullPath}"
        };

        if (!File.Exists(fullPath))
        {
            return new BootPivotImageInfoResult(
                BootPivotStatus.NotFound,
                $"Image file was not found: {fullPath}",
                fullPath,
                false,
                Array.Empty<BootPivotWimImageInfo>(),
                diagnostics);
        }

        if (!OperatingSystem.IsWindows())
        {
            return new BootPivotImageInfoResult(
                BootPivotStatus.NotSupported,
                "WIM metadata inspection is supported only on Windows.",
                fullPath,
                false,
                Array.Empty<BootPivotWimImageInfo>(),
                diagnostics);
        }

        if (!IsExecutableAvailable("dism.exe"))
        {
            return new BootPivotImageInfoResult(
                BootPivotStatus.NotSupported,
                "dism.exe is not available in PATH.",
                fullPath,
                false,
                Array.Empty<BootPivotWimImageInfo>(),
                diagnostics);
        }

        var command = new CommandSpec("dism", ["/English", "/Get-WimInfo", $"/WimFile:{fullPath}"]);
        var execution = await processExecutor.ExecuteAsync(command.FileName, command.Arguments, cancellationToken);

        diagnostics.Add($"command: {FormatCommand(command)}");
        diagnostics.Add($"exit-code: {execution.ExitCode}");

        if (execution.ExitCode != 0)
        {
            diagnostics.AddRange(execution.StandardError.Take(3).Select(static line => $"stderr: {line}"));
            diagnostics.AddRange(execution.StandardOutput.Take(3).Select(static line => $"stdout: {line}"));

            return new BootPivotImageInfoResult(
                BootPivotStatus.Failed,
                "DISM failed to inspect the image. See diagnostics for details.",
                fullPath,
                false,
                Array.Empty<BootPivotWimImageInfo>(),
                diagnostics);
        }

        var parserInput = execution.StandardOutput.Concat(execution.StandardError).ToArray();
        var parsedImages = DismWimInfoParser.Parse(parserInput);
        var indexValidationAvailable = parsedImages.Count > 0;
        var message = indexValidationAvailable
            ? $"Detected {parsedImages.Count} image index(es)."
            : "Image metadata query succeeded, but image indexes were not detected.";

        return new BootPivotImageInfoResult(
            BootPivotStatus.Success,
            message,
            fullPath,
            indexValidationAvailable,
            parsedImages,
            diagnostics);
    }

    public async Task<BootPivotStageResult> StageAsync(BootPivotStageDriverRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryResolveSystemPartition(request.SystemPartition, out var systemPartition, out var systemPartitionError))
        {
            return BuildStageValidationFailure(systemPartitionError);
        }

        if (!TryResolveBootSdiPath(request.BootSdiPath, systemPartition, out var bootSdiPath, out var bootSdiFullPath, out var bootSdiError))
        {
            return BuildStageValidationFailure(bootSdiError);
        }

        if (OperatingSystem.IsWindows() && !File.Exists(bootSdiFullPath))
        {
            return BuildStageValidationFailure($"boot.sdi was not found at '{bootSdiFullPath}'.");
        }

        if (!TryResolveRequestedWinloadPath(request.WinloadPath, out var requestedWinloadPath, out var winloadPathError))
        {
            return BuildStageValidationFailure(winloadPathError);
        }

        var winloadPath = await ResolveWinloadPathAsync(requestedWinloadPath, cancellationToken);

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
            null,
            systemPartition,
            bootSdiPath,
            winloadPath,
            request.Images);

        if (!TryBuildRamdiskDevice(request.ImagePath, out var ramdiskDevice, out var ramdiskError))
        {
            return new BootPivotStageResult(
                BootPivotStatus.ValidationError,
                ramdiskError,
                manifest,
                Array.Empty<string>());
        }

        var plannedCommands = BuildPlannedPivotCommands(
            request.Label,
            ramdiskDevice,
            "<new_entry_guid>",
            systemPartition,
            bootSdiPath,
            winloadPath,
            reboot: false);

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

        if (!TryResolveSystemPartition(manifest.SystemPartition, out var systemPartition, out var systemPartitionError))
        {
            return new BootPivotPivotResult(
                BootPivotStatus.ValidationError,
                systemPartitionError,
                null,
                Array.Empty<string>());
        }

        if (!TryResolveBootSdiPath(manifest.BootSdiPath, systemPartition, out var bootSdiPath, out var bootSdiFullPath, out var bootSdiError))
        {
            return new BootPivotPivotResult(
                BootPivotStatus.ValidationError,
                bootSdiError,
                null,
                Array.Empty<string>());
        }

        if (request.ApplyChanges && OperatingSystem.IsWindows() && !File.Exists(bootSdiFullPath))
        {
            return new BootPivotPivotResult(
                BootPivotStatus.ValidationError,
                $"boot.sdi was not found at '{bootSdiFullPath}'.",
                null,
                Array.Empty<string>());
        }

        if (!TryResolveRequestedWinloadPath(manifest.WinloadPath, out var requestedWinloadPath, out var winloadPathError))
        {
            return new BootPivotPivotResult(
                BootPivotStatus.ValidationError,
                winloadPathError,
                null,
                Array.Empty<string>());
        }

        var winloadPath = await ResolveWinloadPathAsync(requestedWinloadPath, cancellationToken);

        if (!TryBuildRamdiskDevice(manifest.ImagePath, out var ramdiskDevice, out var ramdiskError))
        {
            return new BootPivotPivotResult(
                BootPivotStatus.ValidationError,
                ramdiskError,
                null,
                Array.Empty<string>());
        }

        var previewCommands = BuildPlannedPivotCommands(
            manifest.Label,
            ramdiskDevice,
            "<new_entry_guid>",
            systemPartition,
            bootSdiPath,
            winloadPath,
            request.Reboot);

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

        var remainingCommands = BuildPivotCommandSpecs(
            bootEntryId,
            ramdiskDevice,
            systemPartition,
            bootSdiPath,
            winloadPath,
            request.Reboot);

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
                LastPivotedUtc = DateTimeOffset.UtcNow,
                SystemPartition = systemPartition,
                BootSdiPath = bootSdiPath,
                WinloadPath = winloadPath
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

    private static BootPivotStageResult BuildStageValidationFailure(string message)
    {
        return new BootPivotStageResult(
            BootPivotStatus.ValidationError,
            message,
            null,
            Array.Empty<string>());
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
        string systemPartition,
        string bootSdiPath,
        string winloadPath,
        bool reboot)
    {
        var commands = new List<CommandSpec>
        {
            new("bcdedit", ["/create", "/d", label, "/application", "osloader"])
        };

        commands.AddRange(BuildPivotCommandSpecs(
            bootEntryId,
            ramdiskDevice,
            systemPartition,
            bootSdiPath,
            winloadPath,
            reboot));

        return commands.Select(FormatCommand).ToArray();
    }

    private static IReadOnlyList<CommandSpec> BuildPivotCommandSpecs(
        string bootEntryId,
        string ramdiskDevice,
        string systemPartition,
        string bootSdiPath,
        string winloadPath,
        bool reboot)
    {
        var commands = new List<CommandSpec>
        {
            new("bcdedit", ["/set", "{ramdiskoptions}", "ramdisksdidevice", $"partition={systemPartition}"]),
            new("bcdedit", ["/set", "{ramdiskoptions}", "ramdisksdipath", bootSdiPath]),
            new("bcdedit", ["/set", bootEntryId, "device", ramdiskDevice]),
            new("bcdedit", ["/set", bootEntryId, "osdevice", ramdiskDevice]),
            new("bcdedit", ["/set", bootEntryId, "path", winloadPath]),
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

    private async Task<string> ResolveRecommendedWinloadPathAsync(CancellationToken cancellationToken)
    {
        var detectedPath = await TryReadCurrentWinloadPathAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(detectedPath))
        {
            return detectedPath;
        }

        return ResolveFallbackWinloadPath();
    }

    private async Task<string> ResolveWinloadPathAsync(string? requestedPath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return requestedPath;
        }

        return await ResolveRecommendedWinloadPathAsync(cancellationToken);
    }

    private async Task<string?> TryReadCurrentWinloadPathAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !IsExecutableAvailable("bcdedit.exe"))
        {
            return null;
        }

        var command = new CommandSpec("bcdedit", ["/enum", "{current}"]);
        var result = await processExecutor.ExecuteAsync(command.FileName, command.Arguments, cancellationToken);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var path = BcdCurrentPathParser.Parse(result.StandardOutput);
        return NormalizeWinloadPath(path);
    }

    private static string ResolveFallbackWinloadPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return DefaultWinloadEfiPath;
        }

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory))
        {
            return DefaultWinloadEfiPath;
        }

        var efiCandidate = Path.Combine(windowsDirectory, "System32", "winload.efi");
        if (File.Exists(efiCandidate))
        {
            return DefaultWinloadEfiPath;
        }

        var exeCandidate = Path.Combine(windowsDirectory, "System32", "winload.exe");
        if (File.Exists(exeCandidate))
        {
            return DefaultWinloadExePath;
        }

        return DefaultWinloadEfiPath;
    }

    private static bool TryResolveSystemPartition(string? requested, out string systemPartition, out string error)
    {
        var candidate = requested;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = Environment.GetEnvironmentVariable("SystemDrive");
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = "C:";
        }

        var normalized = candidate.Trim().Replace('/', '\\').TrimEnd('\\');
        if (normalized.Length == 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
        {
            systemPartition = string.Create(2, normalized, static (buffer, value) =>
            {
                buffer[0] = char.ToUpperInvariant(value[0]);
                buffer[1] = ':';
            });

            error = string.Empty;
            return true;
        }

        systemPartition = string.Empty;
        error = $"System partition '{candidate}' is invalid. Expected a drive designator like C:.";
        return false;
    }

    private static string ResolveSystemPartition(string? requested)
    {
        return TryResolveSystemPartition(requested, out var systemPartition, out _)
            ? systemPartition
            : "C:";
    }

    private static bool TryResolveBootSdiPath(
        string? requestedPath,
        string systemPartition,
        out string bootSdiPath,
        out string bootSdiFullPath,
        out string error)
    {
        var candidate = string.IsNullOrWhiteSpace(requestedPath)
            ? DefaultBootSdiPath
            : requestedPath.Trim();

        candidate = candidate.Replace('/', '\\');

        if (TrySplitDriveAbsolutePath(candidate, out var drive, out var relativePathFromDrive))
        {
            if (!string.Equals(drive, systemPartition, StringComparison.OrdinalIgnoreCase))
            {
                bootSdiPath = string.Empty;
                bootSdiFullPath = string.Empty;
                error = $"boot.sdi path drive '{drive}' does not match system partition '{systemPartition}'.";
                return false;
            }

            bootSdiPath = NormalizeRootedPath(relativePathFromDrive);
        }
        else
        {
            bootSdiPath = NormalizeRootedPath(candidate);
        }

        if (string.IsNullOrWhiteSpace(bootSdiPath) || bootSdiPath == "\\")
        {
            bootSdiPath = string.Empty;
            bootSdiFullPath = string.Empty;
            error = "boot.sdi path is invalid.";
            return false;
        }

        if (bootSdiPath.Contains(':', StringComparison.Ordinal))
        {
            bootSdiPath = string.Empty;
            bootSdiFullPath = string.Empty;
            error = "boot.sdi path must be root-relative (for example \\boot\\boot.sdi).";
            return false;
        }

        bootSdiFullPath = BuildAbsolutePath(systemPartition, bootSdiPath);
        error = string.Empty;
        return true;
    }

    private static string NormalizeRootedPath(string value)
    {
        var normalized = value.Replace('/', '\\').Trim();
        normalized = normalized.TrimStart('\\');
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : $"\\{normalized}";
    }

    private static string? NormalizeWinloadPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim().Replace('/', '\\');
        if (TrySplitDriveAbsolutePath(candidate, out _, out var relativePathFromDrive))
        {
            candidate = relativePathFromDrive;
        }

        return NormalizeRootedPath(candidate);
    }

    private static bool TryResolveRequestedWinloadPath(
        string? requestedPath,
        out string? normalizedPath,
        out string error)
    {
        normalizedPath = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return true;
        }

        var normalized = NormalizeWinloadPath(requestedPath);
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "\\" || normalized.Contains(':', StringComparison.Ordinal))
        {
            error = "Winload path is invalid. Expected a rooted path like \\Windows\\System32\\winload.efi.";
            return false;
        }

        normalizedPath = normalized;
        return true;
    }

    private static bool TrySplitDriveAbsolutePath(string path, out string drive, out string relativePath)
    {
        if (path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/'))
        {
            drive = string.Create(2, path, static (buffer, value) =>
            {
                buffer[0] = char.ToUpperInvariant(value[0]);
                buffer[1] = ':';
            });
            relativePath = path[2..];
            return true;
        }

        drive = string.Empty;
        relativePath = string.Empty;
        return false;
    }

    private static string BuildAbsolutePath(string systemPartition, string rootedPath)
    {
        return $"{systemPartition}\\{rootedPath.TrimStart('\\')}";
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
