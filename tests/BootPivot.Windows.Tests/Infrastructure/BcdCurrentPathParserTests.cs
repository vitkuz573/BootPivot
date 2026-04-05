namespace BootPivot.Windows.Tests.Infrastructure;

public sealed class BcdCurrentPathParserTests
{
    [Fact]
    public void Parse_ReturnsPath_WhenPathLineExists()
    {
        IReadOnlyList<string> lines =
        [
            "Windows Boot Loader",
            "-------------------",
            "identifier              {current}",
            "device                  partition=C:",
            "path                    \\Windows\\System32\\winload.efi"
        ];

        var result = BcdCurrentPathParser.Parse(lines);

        Assert.Equal("\\Windows\\System32\\winload.efi", result);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        IReadOnlyList<string> lines =
        [
            "PATH                    \\Windows\\System32\\winload.exe"
        ];

        var result = BcdCurrentPathParser.Parse(lines);

        Assert.Equal("\\Windows\\System32\\winload.exe", result);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenPathLineMissing()
    {
        IReadOnlyList<string> lines =
        [
            "identifier              {current}",
            "device                  partition=C:"
        ];

        var result = BcdCurrentPathParser.Parse(lines);

        Assert.Null(result);
    }
}
