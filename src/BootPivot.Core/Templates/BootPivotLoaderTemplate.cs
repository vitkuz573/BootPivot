namespace BootPivot.Core.Templates;

public static class BootPivotLoaderTemplate
{
    public const string Default = """
@echo off
setlocal EnableExtensions

echo [BootPivot] loader initialized
echo [BootPivot] image path: <image_path>
echo [BootPivot] image index: <image_index>
echo [BootPivot] label: <boot_label>

<some_var>
""";
}
