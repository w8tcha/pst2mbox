using Pst2Mbox;

var arguments = new List<string>();
string? folderFilter = null;
var includeAll = false;
var overwrite = false;
var verbose = false;
var splitByFolder = false;

var i = 0;
while (i < args.Length)
{
    switch (args[i])
    {
        case "-h":
        case "--help":
            PrintUsage();
            return 0;

        case "-f":
        case "--folder":
            if (++i >= args.Length)
                return Fail("Missing value for --folder.");
            folderFilter = args[i];
            break;

        case "-a":
        case "--include-all":
            includeAll = true;
            break;

        case "-s":
        case "--split-folders":
            splitByFolder = true;
            break;

        case "--overwrite":
            overwrite = true;
            break;

        case "-v":
        case "--verbose":
            verbose = true;
            break;

        default:
            arguments.Add(args[i]);
            break;
    }

    i++;
}

if (arguments.Count != 2)
{
    PrintUsage();
    return arguments.Count == 0 ? 0 : 1;
}

var options = new ConversionOptions
{
    PstPath = arguments[0],
    MboxPath = arguments[1],
    FolderFilter = folderFilter,
    IncludeAllItemTypes = includeAll,
    SplitByFolder = splitByFolder,
    Overwrite = overwrite,
    Verbose = verbose
};

if (!File.Exists(options.PstPath))
    return Fail($"Input file not found: {options.PstPath}");

var stopwatch = System.Diagnostics.Stopwatch.StartNew();

try
{
    var stats = PstToMboxConverter.Convert(options);
    stopwatch.Stop();

    Console.WriteLine();
    Console.WriteLine("Conversion complete.");
    Console.WriteLine($"  Folders visited:        {stats.FoldersVisited}");
    if (options.SplitByFolder)
        Console.WriteLine($"  Mbox files written:      {stats.MboxFilesWritten}");
    Console.WriteLine($"  Messages converted:      {stats.MessagesConverted}");
    Console.WriteLine($"  Messages skipped (filter): {stats.MessagesSkippedByFilter}");
    Console.WriteLine($"  Attachments included:    {stats.AttachmentsIncluded}");
    Console.WriteLine($"  Elapsed:                 {stopwatch.Elapsed}");

    if (stats.Errors.Count > 0)
    {
        Console.WriteLine($"  Errors:                  {stats.Errors.Count}");
        foreach (var error in stats.Errors)
        {
            await Console.Error.WriteLineAsync($"    - {error}");
        }
    }

    return 0;
}
catch (Exception ex)
{
    return Fail($"Conversion failed: {ex.Message}");
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine();
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        pst2mbox - convert an Outlook .pst file into an mbox mailbox file

        Usage:
          pst2mbox <input.pst> <output.mbox> [options]
          pst2mbox <input.pst> <output-dir> --split-folders [options]

        Options:
          -f, --folder <text>   Only convert messages from folders whose path contains
                                 <text> (case-insensitive). Default: all folders.
          -a, --include-all     Include all item types (calendar, contacts, tasks, notes),
                                 not just regular mail (IPM.Note). Default: mail only.
          -s, --split-folders   Write one .mbox file per PST folder instead of a single
                                 combined file. <output.mbox> is treated as an output
                                 directory, created if needed; each folder that contains
                                 qualifying messages is written to "<folder name>.mbox"
                                 inside it (folders without messages are skipped, and
                                 name collisions get a " (2)", " (3)", ... suffix).
              --overwrite       Overwrite the output file (or reuse a non-empty output
                                 directory) if it already exists.
          -v, --verbose         Print each converted message as it is written.
          -h, --help            Show this help.

        Examples:
          pst2mbox "C:\Mail\Outlook.pst" "C:\Mail\outlook.mbox" --overwrite
          pst2mbox "C:\Mail\Outlook.pst" "C:\Mail\ByFolder" --split-folders --overwrite
        """);
}
