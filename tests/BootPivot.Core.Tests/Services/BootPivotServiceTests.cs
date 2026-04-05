using BootPivot.Core.Abstractions;
using BootPivot.Core.Models;
using BootPivot.Core.Services;
using Moq;

namespace BootPivot.Core.Tests.Services;

public sealed class BootPivotServiceTests
{
    [Fact]
    public async Task GetImageInfoAsync_ReturnsValidationError_WhenImagePathIsMissing()
    {
        var driver = new Mock<IBootPivotDriver>(MockBehavior.Strict);
        var sut = new BootPivotService(driver.Object);

        var result = await sut.GetImageInfoAsync(string.Empty, CancellationToken.None);

        Assert.Equal(BootPivotStatus.ValidationError, result.Status);
        Assert.Contains("Image path is required", result.Message);
        driver.VerifyNoOtherCalls();
    }

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
    public async Task StageAsync_ReturnsDriverStatus_WhenImageInfoValidationFails()
    {
        var driver = new Mock<IBootPivotDriver>(MockBehavior.Strict);
        driver.Setup(x => x.GetImageInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootPivotImageInfoResult(
                BootPivotStatus.NotSupported,
                "dism.exe is not available",
                "C:\\images\\boot.wim",
                false,
                Array.Empty<BootPivotWimImageInfo>(),
                Array.Empty<string>()));

        var sut = new BootPivotService(driver.Object);

        var result = await sut.StageAsync(
            new BootPivotStageOptions(ImagePath: @"C:\images\boot.wim", ImageIndex: 1),
            CancellationToken.None);

        Assert.Equal(BootPivotStatus.NotSupported, result.Status);
        Assert.Contains("Image validation failed", result.Message);
        driver.Verify(x => x.StageAsync(It.IsAny<BootPivotStageDriverRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        driver.VerifyAll();
    }

    [Fact]
    public async Task StageAsync_ReturnsValidationError_WhenImageIndexIsMissingInWim()
    {
        var driver = new Mock<IBootPivotDriver>(MockBehavior.Strict);
        driver.Setup(x => x.GetImageInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootPivotImageInfoResult(
                BootPivotStatus.Success,
                "ok",
                @"C:\images\boot.wim",
                true,
                [
                    new BootPivotWimImageInfo(1, "First", null),
                    new BootPivotWimImageInfo(2, "Second", null)
                ],
                Array.Empty<string>()));

        var sut = new BootPivotService(driver.Object);

        var result = await sut.StageAsync(
            new BootPivotStageOptions(ImagePath: @"C:\images\boot.wim", ImageIndex: 3),
            CancellationToken.None);

        Assert.Equal(BootPivotStatus.ValidationError, result.Status);
        Assert.Contains("Image index 3 was not found", result.Message);
        Assert.Contains("1, 2", result.Message);
        driver.Verify(x => x.StageAsync(It.IsAny<BootPivotStageDriverRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        driver.VerifyAll();
    }

    [Fact]
    public async Task StageAsync_GeneratesSessionAndDelegatesToDriver()
    {
        var driver = new Mock<IBootPivotDriver>(MockBehavior.Strict);
        BootPivotStageDriverRequest? capturedRequest = null;

        driver.Setup(x => x.GetImageInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BootPivotImageInfoResult(
                BootPivotStatus.Success,
                "ok",
                @"C:\images\boot.wim",
                true,
                [
                    new BootPivotWimImageInfo(1, "First", null),
                    new BootPivotWimImageInfo(3, "Third", null)
                ],
                Array.Empty<string>()));

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
                SystemPartition: "C:",
                BootSdiPath: "\\boot\\boot.sdi",
                WinloadPath: "\\Windows\\System32\\winload.efi",
                DryRun: true),
            CancellationToken.None);

        Assert.Equal(BootPivotStatus.Success, result.Status);
        Assert.NotNull(capturedRequest);
        Assert.Matches("^[0-9]{14}-[a-f0-9]{8}$", capturedRequest!.SessionId);
        Assert.Equal(3, capturedRequest.ImageIndex);
        Assert.Equal("BootPivot Demo", capturedRequest.Label);
        Assert.True(Path.IsPathRooted(capturedRequest.ImagePath));
        Assert.Equal("C:", capturedRequest.SystemPartition);
        Assert.Equal("\\boot\\boot.sdi", capturedRequest.BootSdiPath);
        Assert.Equal("\\Windows\\System32\\winload.efi", capturedRequest.WinloadPath);
        Assert.Equal(2, capturedRequest.Images.Count);
        Assert.DoesNotContain("<some_var>", capturedRequest.LoaderScriptContent);

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
