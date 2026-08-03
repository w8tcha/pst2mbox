using MimeKit;
using MimeKit.Utils;

using XstReader;

namespace Pst2Mbox;

/// <summary>
/// Converts an XstReader message (a MAPI item read out of a .pst/.ost file) into a MimeKit
/// MimeMessage, so it can be serialized as a standard RFC 5322 message inside a mbox file.
/// </summary>
internal static class MimeMessageFactory
{
    private const int MaxEmbeddedMessageDepth = 10;

    public static MimeMessage Build(XstMessage message, ConversionStats stats, int depth = 0)
    {
        var mime = new MimeMessage();

        var sender = message.Recipients.Sender;
        var fromAddress = sender?.Address;
        var fromName = sender?.DisplayName ?? message.From ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(fromAddress))
            mime.From.Add(new MailboxAddress(fromName, fromAddress.Trim()));
        else if (!string.IsNullOrWhiteSpace(fromName))
            mime.From.Add(new MailboxAddress(fromName, "unknown@unknown.invalid"));

        AddAddresses(mime.To, message.Recipients.To);
        AddAddresses(mime.Cc, message.Recipients.Cc);
        AddAddresses(mime.Bcc, message.Recipients.Bcc);

        mime.Subject = message.Subject ?? string.Empty;

        mime.Date = message.Date is { } date
            ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))
            : DateTimeOffset.UtcNow;

        var messageId = message.InternetMessageId;
        mime.MessageId = string.IsNullOrWhiteSpace(messageId)
            ? MimeUtils.GenerateMessageId()
            : messageId.Trim().Trim('<', '>');

        var bodyBuilder = new BodyBuilder();
        var body = message.Body; // accessing this also marks inline attachments as "rendered inline"
        switch (body?.Format)
        {
            case XstMessageBodyFormat.Html:
                bodyBuilder.HtmlBody = body.Text;
                break;
            case XstMessageBodyFormat.PlainText:
                bodyBuilder.TextBody = body.Text;
                break;
            case XstMessageBodyFormat.Rtf:
                // No safe RTF-to-text conversion is available here; keep the original RTF
                // rather than emit garbled plain text.
                break;
            default:
                bodyBuilder.TextBody = string.Empty;
                break;
        }

        foreach (var attachment in message.Attachments.VisibleFiles())
        {
            try
            {
                using var ms = new MemoryStream();
                attachment.SaveToStream(ms);
                var name = attachment.FileNameForSaving ?? "attachment.bin";
                bodyBuilder.Attachments.Add(name, ms.ToArray());
                stats.AttachmentsIncluded++;
            }
            catch (Exception ex)
            {
                stats.Errors.Add($"Attachment '{attachment.FileNameForSaving}' in message '{message.Subject}': {ex.Message}");
            }
        }

        var bodyEntity = body?.Format == XstMessageBodyFormat.Rtf
            ? CombineWithRtf(bodyBuilder, body.Text)
            : bodyBuilder.ToMessageBody();

        if (depth < MaxEmbeddedMessageDepth)
        {
            var embedded = message.Attachments
                .Where(a => a.IsEmail)
                .Select(a => (a.FileNameForSaving, Nested: TryBuildEmbedded(a, stats, depth)))
                .Where(x => x.Nested is not null)
                .Select(x => (x.FileNameForSaving, Nested: x.Nested!))
                .ToList();

            if (embedded.Count > 0)
            {
                var mixed = new Multipart("mixed") { bodyEntity };
                foreach (var (name, nested) in embedded)
                {
                    var part = new MessagePart
                    {
                        Message = nested,
                        ContentDisposition = new ContentDisposition(ContentDisposition.Attachment)
                        {
                            FileName = !string.IsNullOrEmpty(name) ? name : $"{nested.Subject ?? "message"}.eml"
                        }
                    };
                    mixed.Add(part);
                }

                bodyEntity = mixed;
            }
        }

        mime.Body = bodyEntity;
        return mime;
    }

    private static MimeMessage? TryBuildEmbedded(XstAttachment attachment, ConversionStats stats, int depth)
    {
        try
        {
            var nested = attachment.AttachedEmailMessage;
            return nested is null ? null : Build(nested, stats, depth + 1);
        }
        catch (Exception ex)
        {
            stats.Errors.Add($"Embedded message attachment '{attachment.FileNameForSaving}': {ex.Message}");
            return null;
        }
    }

    private static MimeEntity CombineWithRtf(BodyBuilder bodyBuilder, string? rtfText)
    {
        var rtfPart = new TextPart("rtf") { Text = rtfText ?? string.Empty };
        if (bodyBuilder.Attachments.Count == 0)
            return rtfPart;

        var mixed = new Multipart("mixed") { rtfPart };
        foreach (var attachment in bodyBuilder.Attachments)
            mixed.Add(attachment);
        return mixed;
    }

    private static void AddAddresses(InternetAddressList list, IEnumerable<XstRecipient> recipients)
    {
        foreach (var recipient in recipients)
        {
            var address = recipient.Address;
            if (string.IsNullOrWhiteSpace(address))
                continue;

            list.Add(new MailboxAddress(recipient.DisplayName ?? string.Empty, address.Trim()));
        }
    }
}
