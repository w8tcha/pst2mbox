using System.Globalization;
using System.Text;

using MimeKit;

namespace Pst2Mbox;

/// <summary>
/// Writes MimeMessages to a stream in classic ("mboxrd") mbox format:
/// each message is preceded by a "From sender date" marker line, and any
/// line in the message that begins with "From " (optionally already
/// preceded by one or more '&gt;') is escaped with an extra leading '&gt;'
/// so that mbox readers do not mistake it for a new message boundary.
/// </summary>
internal sealed class MboxWriter : IDisposable
{
    private static readonly byte[] NewLine = [.. "\n"u8];
    private readonly Stream _stream;
    private readonly FormatOptions _formatOptions;
    private bool _wroteFirstMessage;

    public MboxWriter(Stream stream)
    {
        _stream = stream;
        _formatOptions = FormatOptions.Default.Clone();
        _formatOptions.NewLineFormat = NewLineFormat.Unix;
        _formatOptions.EnsureNewLine = true;
    }

    public void WriteMessage(MimeMessage message, string envelopeSender)
    {
        if (_wroteFirstMessage)
            _stream.Write(NewLine, 0, NewLine.Length);
        _wroteFirstMessage = true;

        var marker = $"From {SanitizeEnvelopeSender(envelopeSender)} {FormatMboxDate(message.Date)}\n";
        var markerBytes = Encoding.ASCII.GetBytes(marker);
        _stream.Write(markerBytes, 0, markerBytes.Length);

        using var buffer = new MemoryStream();
        message.WriteTo(_formatOptions, buffer);
        WriteArmored(_stream, buffer.GetBuffer(), (int)buffer.Length);
    }

    private static string SanitizeEnvelopeSender(string sender)
    {
        if (string.IsNullOrWhiteSpace(sender))
            return "MAILER-DAEMON";

        // The marker line is whitespace-delimited, so the address itself must not contain spaces.
        return sender.Replace(' ', '_');
    }

    private static string FormatMboxDate(DateTimeOffset date)
    {
        var utc = date.UtcDateTime;
        var dayOfWeek = utc.ToString("ddd", CultureInfo.InvariantCulture);
        var month = utc.ToString("MMM", CultureInfo.InvariantCulture);
        var day = utc.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2);
        var time = utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"{dayOfWeek} {month} {day} {time} {utc.Year:D4}";
    }

    /// <summary>
    /// Copies <paramref name="content"/> to <paramref name="output"/>, prefixing any line that
    /// matches ^&gt;*From  with one extra '&gt;' (mboxrd quoting).
    /// </summary>
    private static void WriteArmored(Stream output, byte[] content, int length)
    {
        const byte gt = (byte)'>';
        const byte lf = (byte)'\n';

        var from = "From "u8;
        var lineStart = 0;

        for (var i = 0; i <= length; i++)
        {
            if (i != length && content[i] != lf)
                continue;

            var lineEnd = i;
            var scan = lineStart;
            while (scan < lineEnd && content[scan] == gt)
                scan++;

            if (lineEnd - scan >= from.Length && content.AsSpan(scan, from.Length).SequenceEqual(from))
                output.WriteByte(gt);

            output.Write(content, lineStart, lineEnd - lineStart);
            if (i != length)
                output.WriteByte(lf);

            lineStart = i + 1;
        }
    }

    public void Dispose() => _stream.Dispose();
}
