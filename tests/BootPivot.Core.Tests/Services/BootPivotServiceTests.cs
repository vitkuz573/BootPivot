using BootPivot.Core.Abstractions;
using BootPivot.Core.Models;
using BootPivot.Core.Services;
using Moq;

namespace BootPivot.Core.Tests.Services;

public sealed class BootPivotServiceTests
{
    [Fact]
    public async Task StageAsync_ReturnsValidationError_WhenImagePathIsMissing()
    {
        var driver = new Mock<IBootPivotDriver>(MockBehavior.Strict);
        var sut = new BootPivotService(driver.Object);

        var result = await sut.StageAsync(
            new BootPivotStageOptions(string.Empty),
            CancellationToken.None);

        Assert.Equal(BootPivotStatus.ValidationError, result.Status);
        Assert.Contains("Image path is required", result.Message);
        driver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StageAsync_GeneratesSessionAndDelegatesToDriver()
    {
        var driver = new Mock<IBootPivotDriver>(MockBehavior.Strict);
        BootPivotStageDriverRequest? capturedRequest = null;

        driver.Setup(x => x.StageAsync(It.IsAny<BootPivotStageDriverRequest>(), It.IsAny<CancellationToken>()))
            .Callback<BootPivotStageDriverRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new BootPivotStageResult(
                BootPivotStatus.Success,
                "ok",
                null,
                Array.Empty<string>()));

        var sut = new BootPivotService(driver.Object);

        var result = await sut.StageAsync(
            new BootPivotStageOptions(
                ImagePath: Path.Combine("images", "boot.wim"),
                ImageIndex: 3,
                Label: "BootPivot Demo",
                SessionId: null,
                WorkingRoot: Path.Combine(Path.GetTempPath(), "bootpivot-tests"),
                LoaderCommand: null,
                DryRun: true),
            CancellationToken.None);

        Assert.Equal(BootPivotStatus.Success, result.Status);
        Assert.NotNull(capturedRequest);
        Assert.Matches("^[0-9]{14}-[a-f0-9]{8}$", capturedRequest!.SessionId);
        Assert.Equal(3, capturedRequest.ImageIndex);
        Assert.Equal("BootPivot Demo", capturedRequest.Label);
        Assert.True(Path.IsPathRooted(capturedRequest.ImagePath));
        Assert.Contains("<some_var>", capturedRequest.LoaderScriptContent);

        driver.VerifyAll();
    }

    [Fact]
    public async Task CleanupAsync_ReturnsValidationError_WhenOlderThanDaysIsNotPositive()
    {
        var driver = new Mock<IBootPivotDriver>(MockBehavior.Strict);
        var sut = new BootPivotService(driver.Object);

        var result = await sut.CleanupAsync(
            new BootPivotCleanupOptions(OlderThanDays: 0),
            CancellationToken.None);

        Assert.Equal(BootPivotStatus.ValidationError, result.Status);
        Assert.Contains("greater than 0", result.Message);
        driver.VerifyNoOtherCalls();
    }
}
