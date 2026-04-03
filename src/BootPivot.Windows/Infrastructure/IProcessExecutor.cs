namespace BootPivot.Windows;

public interface IProcessExecutor
{
    Task<ProcessExecutionResult> ExecuteAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}
