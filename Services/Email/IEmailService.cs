namespace FyreApp.Services.Email;

public interface IEmailService
{
    Task SendAsync(string toAddress, string toName, string subject, string htmlBody, CancellationToken ct = default);
}
