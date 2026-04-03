namespace BootPivot.Cli;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        return CliApp.RunAsync(args);
    }
}
