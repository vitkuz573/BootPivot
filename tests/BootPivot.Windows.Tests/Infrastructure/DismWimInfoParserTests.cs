using BootPivot.Core.Models;

namespace BootPivot.Windows.Tests.Infrastructure;

public sealed class DismWimInfoParserTests
{
    [Fact]
    public void Parse_ReturnsImageEntries_FromStandardDismOutput()
    {
        IReadOnlyList<string> lines =
        [
            "Deployment Image Servicing and Management tool",
            "",
            "Details for image : C:\\images\\boot.wim",
            "",
            "Index : 1",
            "Name : Windows PE",
            "Description : Recovery environment",
            "",
            "Index : 2",
            "Name : Setup",
            "Description : Setup image"
        ];

        var result = DismWimInfoParser.Parse(lines);

        Assert.Equal(2, result.Count);
        Assert.Equal(new BootPivotWimImageInfo(1, "Windows PE", "Recovery environment"), result[0]);
        Assert.Equal(new BootPivotWimImageInfo(2, "Setup", "Setup image"), result[1]);
    }

    [Fact]
    public void Parse_ReturnsEmpty_WhenNoIndexesArePresent()
    {
        IReadOnlyList<string> lines =
        [
            "Deployment Image Servicing and Management tool",
            "No valid images were found"
        ];

        var result = DismWimInfoParser.Parse(lines);

        Assert.Empty(result);
    }

    [Fact]
    public void Parse_AllowsMissingNameOrDescription()
    {
        IReadOnlyList<string> lines =
        [
            "Index : 1",
            "Name : ",
            "Description :   ",
            "Index : 2",
            "Description : Has description only"
        ];

        var result = DismWimInfoParser.Parse(lines);

        Assert.Equal(2, result.Count);
        Assert.Equal(new BootPivotWimImageInfo(1, null, null), result[0]);
        Assert.Equal(new BootPivotWimImageInfo(2, null, "Has description only"), result[1]);
    }
}
