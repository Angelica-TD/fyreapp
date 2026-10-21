namespace FyreApp.Services.Email;

public interface IEmailService
{
    // False while Email:SmtpHost is unset: SendAsync then sends nothing and only logs it
    bool IsConfigured { get; }

    // Where mail goes out and who it's from, for the admin page ("email-smtp…:587, from Fyrepower <office@…>")
    string Description { get; }

    Task SendAsync(string toAddress, string toName, string subject, string htmlBody, CancellationToken ct = default);
}
