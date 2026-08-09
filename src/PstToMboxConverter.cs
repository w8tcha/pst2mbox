using XstReader;
using XstReader.ElementProperties;

namespace Pst2Mbox;

internal static class PstToMboxConverter
{
    public static ConversionStats Convert(ConversionOptions options)
    {
        return options.SplitByFolder
            ? ConvertSplitByFolder(options)
            : ConvertSingleFile(options);
    }

    private static ConversionStats ConvertSingleFile(ConversionOptions options)
    {
        var stats = new ConversionStats();

        if (File.Exists(options.MboxPath) && !options.Overwrite)
            throw new InvalidOperationException(
                $"Output file '{options.MboxPath}' already exists. Pass --overwrite to replace it.");

        // Deliberately not disposed: XstFile.Dispose() spawns an unguarded background thread to
        // clear its cached folder tree, racing its own synchronous stream teardown; any exception
        // there escapes as an unhandled exception on that thread. The process exits after one
        // conversion anyway, so the OS reclaims the handle without needing Dispose().
        var xstFile = new XstFile(options.PstPath);
        using var output = new FileStream(options.MboxPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new MboxWriter(output);

        WalkFolder(xstFile.RootFolder, options, stats, writer);

        return stats;
    }

    private static ConversionStats ConvertSplitByFolder(ConversionOptions options)
    {
        var stats = new ConversionStats();

        if (Directory.Exists(options.MboxPath) && Directory.EnumerateFileSystemEntries(options.MboxPath).Any() && !options.Overwrite)
            throw new InvalidOperationException(
                $"Output directory '{options.MboxPath}' already exists and is not empty. Pass --overwrite to reuse it.");

        Directory.CreateDirectory(options.MboxPath);

        // See ConvertSingleFile for why xstFile is not disposed.
        var xstFile = new XstFile(options.PstPath);
        var usedFileNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        WalkFolderSplit(xstFile.RootFolder, options, stats, usedFileNames);

        return stats;
    }

    private static void WalkFolder(XstFolder folder, ConversionOptions options, ConversionStats stats, MboxWriter writer)
    {
        stats.FoldersVisited++;

        var folderMatches = options.FolderFilter is null ||
                            folder.Path.Contains(options.FolderFilter, StringComparison.OrdinalIgnoreCase);

        if (folderMatches)
        {
            foreach (var message in folder.Messages)
                ConvertMessage(message, options, stats, writer);
        }

        foreach (var subFolder in folder.Folders)
            WalkFolder(subFolder, options, stats, writer);
    }

    private static void WalkFolderSplit(XstFolder folder, ConversionOptions options, ConversionStats stats, Dictionary<string, int> usedFileNames)
    {
        stats.FoldersVisited++;

        var folderMatches = options.FolderFilter is null ||
                            folder.Path.Contains(options.FolderFilter, StringComparison.OrdinalIgnoreCase);

        if (folderMatches)
        {
            var qualifyingMessages = options.IncludeAllItemTypes
                ? folder.Messages
                : folder.Messages.Where(IsMailItem);

            using var enumerator = qualifyingMessages.GetEnumerator();
            if (enumerator.MoveNext())
            {
                var fileName = MakeUniqueFileName(folder, usedFileNames);
                var filePath = Path.Combine(options.MboxPath, fileName);

                using (var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new MboxWriter(output))
                {
                    do
                    {
                        ConvertMessage(enumerator.Current, options, stats, writer);
                    } while (enumerator.MoveNext());
                }

                stats.MboxFilesWritten++;

                if (options.Verbose)
                    Console.WriteLine($"Wrote '{folder.Path}' -> {filePath}");
            }
        }

        foreach (var subFolder in folder.Folders)
            WalkFolderSplit(subFolder, options, stats, usedFileNames);
    }

    private static string MakeUniqueFileName(XstFolder folder, Dictionary<string, int> usedFileNames)
    {
        var baseName = SanitizeFileName(string.IsNullOrWhiteSpace(folder.DisplayName) ? "Folder" : folder.DisplayName);

        if (!usedFileNames.TryGetValue(baseName, out var count))
        {
            usedFileNames[baseName] = 1;
            return baseName + ".mbox";
        }

        count++;
        usedFileNames[baseName] = count;
        return $"{baseName} ({count}).mbox";
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray()).Trim();
        return sanitized.Length == 0 ? "Folder" : sanitized;
    }

    private static void ConvertMessage(XstMessage message, ConversionOptions options, ConversionStats stats, MboxWriter writer)
    {
        if (!options.IncludeAllItemTypes && !IsMailItem(message))
        {
            stats.MessagesSkippedByFilter++;
            return;
        }

        try
        {
            var mime = MimeMessageFactory.Build(message, stats);
            var envelopeSender = mime.From.Mailboxes.FirstOrDefault()?.Address ?? "MAILER-DAEMON";
            writer.WriteMessage(mime, envelopeSender);
            stats.MessagesConverted++;

            if (options.Verbose)
                Console.WriteLine($"[{stats.MessagesConverted}] {message.ParentFolder?.Path}: {message.Subject}");
        }
        catch (Exception ex)
        {
            stats.Errors.Add($"'{message.Subject}' in '{message.ParentFolder?.Path}': {ex.Message}");
        }
    }

    private static bool IsMailItem(XstMessage message)
    {
        var messageClass = message.Properties[PropertyCanonicalName.PidTagMessageClass]?.DisplayValue;
        return messageClass is null || messageClass.StartsWith("IPM.Note", StringComparison.OrdinalIgnoreCase);
    }
}
