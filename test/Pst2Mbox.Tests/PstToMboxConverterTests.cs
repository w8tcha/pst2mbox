using MimeKit;

using Xunit;

namespace Pst2Mbox.Tests;

public class PstToMboxConverterTests : IDisposable
{
    // sample.pst ("Top of Personal Folders") contains, besides empty folders:
    //   Freebusy Data (1 item, IPM.Microsoft.ScheduleData.FreeBusy)
    //   Calendar      (1 item, IPM.Appointment)
    //   Contacts      (1 item, IPM.Contact)
    //   Inbox         (6 items, IPM.Note, one of which embeds an .msg attachment)
    //   Sent Items    (5 items, IPM.Note)
    // for a total of 19 folders visited and 14 items overall (11 of them regular mail).
    private const int TotalFolders = 19;
    private const int TotalMailItems = 11;
    private const int TotalNonMailItems = 3;

    private readonly string _sampleFile = Path.Combine(AppContext.BaseDirectory, "TestData", "sample.pst");
    private readonly string _workDir = Directory.CreateTempSubdirectory("pst2mbox-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; leftover temp files don't affect other tests.
        }
    }

    private string OutputPath(string fileName) => Path.Combine(_workDir, fileName);

    [Fact]
    public void Convert_DefaultOptions_OnlyConvertsMailItems()
    {
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = OutputPath("out.mbox") };

        var stats = PstToMboxConverter.Convert(options);

        Assert.Equal(TotalFolders, stats.FoldersVisited);
        Assert.Equal(TotalMailItems, stats.MessagesConverted);
        Assert.Equal(TotalNonMailItems, stats.MessagesSkippedByFilter);
        Assert.Empty(stats.Errors);
        Assert.True(stats.AttachmentsIncluded > 0);
        Assert.True(File.Exists(options.MboxPath));
    }

    [Fact]
    public void Convert_IncludeAllItemTypes_ConvertsNonMailItemsToo()
    {
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = OutputPath("out.mbox"), IncludeAllItemTypes = true };

        var stats = PstToMboxConverter.Convert(options);

        Assert.Equal(TotalFolders, stats.FoldersVisited);
        Assert.Equal(TotalMailItems + TotalNonMailItems, stats.MessagesConverted);
        Assert.Equal(0, stats.MessagesSkippedByFilter);
        Assert.Empty(stats.Errors);
    }

    [Fact]
    public void Convert_FolderFilter_OnlyConvertsMatchingFolders()
    {
        // "Inbox" matches both the "Inbox" folder and "Inbox\SubInbox" (substring match), but SubInbox is empty.
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = OutputPath("out.mbox"), FolderFilter = "Inbox" };

        var stats = PstToMboxConverter.Convert(options);

        Assert.Equal(TotalFolders, stats.FoldersVisited); // every folder is still visited/counted
        Assert.Equal(6, stats.MessagesConverted); // only the Inbox's own mail items
    }

    [Fact]
    public void Convert_FolderFilterIsCaseInsensitive()
    {
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = OutputPath("out.mbox"), FolderFilter = "inbox" };

        var stats = PstToMboxConverter.Convert(options);

        Assert.Equal(6, stats.MessagesConverted);
    }

    [Fact]
    public void Convert_FolderFilterMatchingNothing_ConvertsZeroMessages()
    {
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = OutputPath("out.mbox"), FolderFilter = "NoSuchFolderXYZ" };

        var stats = PstToMboxConverter.Convert(options);

        Assert.Equal(0, stats.MessagesConverted);
        Assert.Equal(0, stats.MessagesSkippedByFilter);
    }

    [Fact]
    public void Convert_OutputFileAlreadyExistsWithoutOverwrite_Throws()
    {
        var path = OutputPath("out.mbox");
        File.WriteAllText(path, "existing content");
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = path };

        var ex = Assert.Throws<InvalidOperationException>(() => PstToMboxConverter.Convert(options));
        Assert.Contains("already exists", ex.Message, StringComparison.Ordinal);
        Assert.Equal("existing content", File.ReadAllText(path)); // untouched
    }

    [Fact]
    public void Convert_OutputFileAlreadyExistsWithOverwrite_ReplacesIt()
    {
        var path = OutputPath("out.mbox");
        File.WriteAllText(path, "existing content");
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = path, Overwrite = true };

        var stats = PstToMboxConverter.Convert(options);

        Assert.Equal(TotalMailItems, stats.MessagesConverted);
        Assert.DoesNotContain("existing content", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_MissingPstFile_Throws()
    {
        var options = new ConversionOptions { PstPath = OutputPath("does-not-exist.pst"), MboxPath = OutputPath("out.mbox") };

        Assert.ThrowsAny<Exception>(() => PstToMboxConverter.Convert(options));
    }

    [Fact]
    public void Convert_SplitByFolder_WritesOneMboxFilePerNonEmptyFolder()
    {
        var outDir = OutputPath("split");
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = outDir, SplitByFolder = true };

        var stats = PstToMboxConverter.Convert(options);

        Assert.Equal(2, stats.MboxFilesWritten); // only Inbox and Sent Items have qualifying mail
        Assert.Equal(TotalMailItems, stats.MessagesConverted);
        Assert.True(File.Exists(Path.Combine(outDir, "Inbox.mbox")));
        Assert.True(File.Exists(Path.Combine(outDir, "Sent Items.mbox")));
        // Folders with no qualifying messages (e.g. Calendar, Contacts, Deleted Items) must not get a file.
        Assert.Equal(2, Directory.GetFiles(outDir).Length);
    }

    [Fact]
    public void Convert_SplitByFolder_EachFileContainsOnlyThatFoldersMessages()
    {
        var outDir = OutputPath("split");
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = outDir, SplitByFolder = true };

        PstToMboxConverter.Convert(options);

        Assert.Equal(6, CountMessages(Path.Combine(outDir, "Inbox.mbox")));
        Assert.Equal(5, CountMessages(Path.Combine(outDir, "Sent Items.mbox")));
    }

    [Fact]
    public void Convert_SplitByFolder_NonEmptyOutputDirWithoutOverwrite_Throws()
    {
        var outDir = OutputPath("split");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "stray.txt"), "hi");
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = outDir, SplitByFolder = true };

        Assert.Throws<InvalidOperationException>(() => PstToMboxConverter.Convert(options));
    }

    [Fact]
    public void Convert_SplitByFolder_EmptyExistingOutputDirWithoutOverwrite_Succeeds()
    {
        var outDir = OutputPath("split");
        Directory.CreateDirectory(outDir);
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = outDir, SplitByFolder = true };

        var stats = PstToMboxConverter.Convert(options);

        Assert.Equal(2, stats.MboxFilesWritten);
    }

    [Fact]
    public void Convert_OutputMbox_ParsesBackAsValidMimeMessagesWithExpectedSubjects()
    {
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = OutputPath("out.mbox") };
        PstToMboxConverter.Convert(options);

        var subjects = ReadSubjects(options.MboxPath);

        Assert.Equal(TotalMailItems, subjects.Count);
        Assert.Contains("Multiple attachments", subjects);
        Assert.Contains("HTML body", subjects);
        Assert.Contains("this message contains embedded MSG", subjects);
    }

    [Fact]
    public void Convert_MessageWithFileAttachments_AttachmentsSurviveInMime()
    {
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = OutputPath("out.mbox") };
        PstToMboxConverter.Convert(options);

        using var stream = File.OpenRead(options.MboxPath);
        var parser = new MimeParser(stream, MimeFormat.Mbox);
        MimeMessage? multiAttachment = null;
        while (!parser.IsEndOfStream)
        {
            var message = parser.ParseMessage();
            if (message.Subject == "Multiple attachments")
                multiAttachment = message;
        }

        Assert.NotNull(multiAttachment);
        Assert.True(multiAttachment.Attachments.Any());
    }

    [Fact]
    public void Convert_MessageWithEmbeddedMsgAttachment_IncludesNestedRfc822MessagePart()
    {
        var options = new ConversionOptions { PstPath = _sampleFile, MboxPath = OutputPath("out.mbox") };
        PstToMboxConverter.Convert(options);

        using var stream = File.OpenRead(options.MboxPath);
        var parser = new MimeParser(stream, MimeFormat.Mbox);
        MimeMessage? withEmbedded = null;
        while (!parser.IsEndOfStream)
        {
            var message = parser.ParseMessage();
            if (message.Subject == "this message contains embedded MSG")
                withEmbedded = message;
        }

        Assert.NotNull(withEmbedded);
        Assert.True(withEmbedded.BodyParts.OfType<MessagePart>().Any());
    }

    private static int CountMessages(string mboxPath)
    {
        using var stream = File.OpenRead(mboxPath);
        var parser = new MimeParser(stream, MimeFormat.Mbox);
        var count = 0;
        while (!parser.IsEndOfStream)
        {
            parser.ParseMessage();
            count++;
        }

        return count;
    }

    private static List<string> ReadSubjects(string mboxPath)
    {
        using var stream = File.OpenRead(mboxPath);
        var parser = new MimeParser(stream, MimeFormat.Mbox);
        var subjects = new List<string>();
        while (!parser.IsEndOfStream)
        {
            subjects.Add(parser.ParseMessage().Subject ?? string.Empty);
        }

        return subjects;
    }
}
