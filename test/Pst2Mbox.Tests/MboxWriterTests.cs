using System.Text;

using MimeKit;

using Xunit;

namespace Pst2Mbox.Tests;

public class MboxWriterTests
{
    private static MimeMessage CreateMessage(string subject, DateTimeOffset date, string from = "alice@example.com", string body = "Hello there.")
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alice", from));
        message.To.Add(new MailboxAddress("Bob", "bob@example.com"));
        message.Subject = subject;
        message.Date = date;
        message.Body = new TextPart("plain") { Text = body };
        return message;
    }

    // MboxWriter.Dispose() also disposes the stream it was given, so the bytes must be
    // captured before disposal rather than via a "using" block around the MemoryStream.
    private static byte[] CaptureBytes(Action<MboxWriter> write)
    {
        var stream = new MemoryStream();
        var writer = new MboxWriter(stream);
        write(writer);
        var bytes = stream.ToArray();
        writer.Dispose();
        return bytes;
    }

    private static string Capture(Action<MboxWriter> write) => Encoding.UTF8.GetString(CaptureBytes(write));

    [Fact]
    public void WriteMessage_FirstMessage_StartsWithFromMarkerAndNoLeadingBlankLine()
    {
        var date = new DateTimeOffset(2011, 6, 21, 23, 51, 7, TimeSpan.Zero);
        var text = Capture(w => w.WriteMessage(CreateMessage("Hello", date), "alice@example.com"));

        Assert.StartsWith("From alice@example.com Tue Jun 21 23:51:07 2011\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMessage_SingleDigitDay_PadsDayWithSpaceNotZero()
    {
        // Classic mbox/asctime formatting pads the day-of-month with a space, not a leading zero.
        var date = new DateTimeOffset(2019, 6, 5, 9, 0, 0, TimeSpan.Zero);
        var text = Capture(w => w.WriteMessage(CreateMessage("Hello", date), "alice@example.com"));

        Assert.StartsWith("From alice@example.com Wed Jun  5 09:00:00 2019\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMessage_ConvertsNonUtcDateToUtcForTheMarker()
    {
        var date = new DateTimeOffset(2011, 6, 22, 1, 51, 7, TimeSpan.FromHours(2)); // == 2011-06-21T23:51:07Z
        var text = Capture(w => w.WriteMessage(CreateMessage("Hello", date), "alice@example.com"));

        Assert.StartsWith("From alice@example.com Tue Jun 21 23:51:07 2011\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMessage_MultipleMessages_AreSeparatedByABlankLine()
    {
        var date = new DateTimeOffset(2011, 6, 21, 23, 51, 7, TimeSpan.Zero);
        var text = Capture(w =>
        {
            w.WriteMessage(CreateMessage("First", date), "alice@example.com");
            w.WriteMessage(CreateMessage("Second", date), "bob@example.com");
        });

        var firstMarkerIndex = text.IndexOf("From alice@example.com", StringComparison.Ordinal);
        var secondMarkerIndex = text.IndexOf("From bob@example.com", StringComparison.Ordinal);

        Assert.True(firstMarkerIndex >= 0 && secondMarkerIndex > firstMarkerIndex);
        // Exactly one blank line must precede the second message's "From " marker.
        Assert.Equal("\n\nFrom bob@example.com", text.Substring(secondMarkerIndex - 2, "\n\nFrom bob@example.com".Length));
    }

    [Fact]
    public void WriteMessage_EmptyEnvelopeSender_FallsBackToMailerDaemon()
    {
        var date = new DateTimeOffset(2011, 6, 21, 23, 51, 7, TimeSpan.Zero);
        var text = Capture(w => w.WriteMessage(CreateMessage("Hello", date), string.Empty));

        Assert.StartsWith("From MAILER-DAEMON ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMessage_WhitespaceEnvelopeSender_FallsBackToMailerDaemon()
    {
        var date = new DateTimeOffset(2011, 6, 21, 23, 51, 7, TimeSpan.Zero);
        var text = Capture(w => w.WriteMessage(CreateMessage("Hello", date), "   "));

        Assert.StartsWith("From MAILER-DAEMON ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMessage_EnvelopeSenderWithSpaces_SpacesAreReplacedWithUnderscores()
    {
        var date = new DateTimeOffset(2011, 6, 21, 23, 51, 7, TimeSpan.Zero);
        var text = Capture(w => w.WriteMessage(CreateMessage("Hello", date), "Some Display Name <a@b.com>"));

        Assert.StartsWith("From Some_Display_Name_<a@b.com> ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMessage_BodyLineStartingWithFrom_IsEscapedWithLeadingGreaterThan()
    {
        var date = new DateTimeOffset(2011, 6, 21, 23, 51, 7, TimeSpan.Zero);
        var text = Capture(w => w.WriteMessage(CreateMessage("Hello", date, body: "line one\nFrom the start of a line\nline three"), "alice@example.com"));

        Assert.Contains("\n>From the start of a line\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMessage_BodyLineAlreadyQuoted_GetsAnAdditionalGreaterThan()
    {
        var date = new DateTimeOffset(2011, 6, 21, 23, 51, 7, TimeSpan.Zero);
        var text = Capture(w => w.WriteMessage(CreateMessage("Hello", date, body: ">From already quoted"), "alice@example.com"));

        Assert.Contains("\n>>From already quoted", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMessage_BodyLineThatOnlyLooksLikeFrom_IsNotEscaped()
    {
        // "Fromage" does not match the literal "From " (with trailing space) prefix, so it must be left alone.
        var date = new DateTimeOffset(2011, 6, 21, 23, 51, 7, TimeSpan.Zero);
        var text = Capture(w => w.WriteMessage(CreateMessage("Hello", date, body: "Fromage is a cheese"), "alice@example.com"));

        Assert.Contains("\nFromage is a cheese", text, StringComparison.Ordinal);
        Assert.DoesNotContain(">Fromage", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMessage_RoundTripsThroughMimeParserInMboxFormat()
    {
        var date = new DateTimeOffset(2011, 6, 21, 23, 51, 7, TimeSpan.Zero);
        var bytes = CaptureBytes(w =>
        {
            w.WriteMessage(CreateMessage("First", date), "alice@example.com");
            w.WriteMessage(CreateMessage("Second", date), "bob@example.com");
        });

        using var readStream = new MemoryStream(bytes);
        var parser = new MimeParser(readStream, MimeFormat.Mbox);

        var subjects = new List<string>();
        while (!parser.IsEndOfStream)
        {
            var message = parser.ParseMessage(TestContext.Current.CancellationToken);
            subjects.Add(message.Subject ?? string.Empty);
        }

        Assert.Equal(["First", "Second"], subjects);
    }
}
