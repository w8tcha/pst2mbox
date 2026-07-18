namespace Pst2Mbox;

internal sealed class ConversionOptions
{
    public required string PstPath { get; init; }
    public required string MboxPath { get; init; }

    /// <summary>Only convert messages from folders whose path contains this text (case-insensitive). Null means all folders.</summary>
    public string? FolderFilter { get; init; }

    /// <summary>When false (default), only items whose MAPI message class starts with "IPM.Note" (regular mail) are converted.</summary>
    public bool IncludeAllItemTypes { get; init; }

    /// <summary>When true, MboxPath is treated as an output directory and each folder that contains
    /// qualifying messages is written to its own "&lt;folder name&gt;.mbox" file inside it.</summary>
    public bool SplitByFolder { get; init; }

    public bool Overwrite { get; init; }

    public bool Verbose { get; init; }
}
