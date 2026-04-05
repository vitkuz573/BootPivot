namespace BootPivot.Core.Models;

public sealed record BootPivotSessionManifest(
    string SessionId,
    string ImagePath,
    int ImageIndex,
    string Label,
    string LoaderScriptPath,
    string? LoaderCommand,
    string? BootEntryId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastPivotedUtc,
    string? SystemPartition = null,
    string? BootSdiPath = null,
    string? WinloadPath = null,
    IReadOnlyList<BootPivotWimImageInfo>? Images = null);
