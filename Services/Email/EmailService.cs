using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FyreApp.Services.Email;

public sealed class EmailService : IEmailService
{
    private readonly EmailOptions _opts;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailOptions> opts, ILogger<EmailService> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toAddress, string toName, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.SmtpHost))
        {
            _logger.LogWarning("Email not sent — SMTP host is not configured. To: {To} Subject: {Subject}", toAddress, subject);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));
        message.Subject = subject;

        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();

        var socketOptions = _opts.UseSsl
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        // smtp4dev and other local dev servers don't validate certificates
        if (!_opts.UseSsl)
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;

        await client.ConnectAsync(_opts.SmtpHost, _opts.SmtpPort, socketOptions, ct);

        if (!string.IsNullOrWhiteSpace(_opts.Username))
            await client.AuthenticateAsync(_opts.Username, _opts.Password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
