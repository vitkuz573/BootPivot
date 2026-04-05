using BootPivot.Core.Templates;

namespace BootPivot.Core.Tests.Templates;

public sealed class BootPivotLoaderTemplateRendererTests
{
    [Fact]
    public void Render_ReplacesKnownPlaceholders()
    {
        const string template = "image=<image_path>;index=<image_index>;label=<boot_label>;var=<some_var>";

        var rendered = BootPivotLoaderTemplateRenderer.Render(
            template,
            @"C:\images\boot.wim",
            2,
            "Pivot Label",
            "echo hello");

        Assert.Equal("image=C:\\images\\boot.wim;index=2;label=Pivot Label;var=echo hello", rendered);
    }

    [Fact]
    public void Render_ReplacesSomeVarWithEmpty_WhenLoaderCommandMissing()
    {
        const string template = "cmd=<some_var>";

        var rendered = BootPivotLoaderTemplateRenderer.Render(
            template,
            @"C:\images\boot.wim",
            1,
            "Pivot Label",
            null);

        Assert.Equal("cmd=", rendered);
    }
}
