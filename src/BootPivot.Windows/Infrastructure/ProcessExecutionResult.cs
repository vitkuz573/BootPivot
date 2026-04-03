namespace BootPivot.Windows;

public sealed record ProcessExecutionResult(
    int ExitCode,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError);
