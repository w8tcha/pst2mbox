namespace Pst2Mbox;

internal sealed class ConversionStats
{
    public int FoldersVisited { get; set; }
    public int MessagesConverted { get; set; }
    public int MessagesSkippedByFilter { get; set; }
    public int AttachmentsIncluded { get; set; }
    public int MboxFilesWritten { get; set; }
    public List<string> Errors { get; } = [];
}
