using System.Net;
using FyreApp.Models;
using FyreApp.Services.Email;
using Microsoft.AspNetCore.Identity;

namespace FyreApp.Services.UserManagement;

public enum PasswordLinkStatus { Sent, NotFound, Inactive, NoEmail, EmailNotSetUp, Failed }
public sealed record PasswordLinkResult(PasswordLinkStatus Status, string? Error = null);

public interface ILoginEmailService
{
    // Emails a login a link to choose its password (/Auth/SetPassword): a new login's welcome email, or a new link
    // sent from the Users page. setPasswordUrl is that page's absolute address; the link adds the login and a token
    // that works once, for DataProtectionTokenProviderOptions.TokenLifespan (3 days, Program.cs).
    Task<PasswordLinkResult> SendPasswordLinkAsync(string userId, string setPasswordUrl, bool welcome, CancellationToken ct = default);
}

public sealed class LoginEmailService : ILoginEmailService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IEmailService _email;
    private readonly ILogger<LoginEmailService> _logger;

    public LoginEmailService(UserManager<ApplicationUser> users, IEmailService email, ILogger<LoginEmailService> logger)
    {
        _users = users;
        _email = email;
        _logger = logger;
    }

    public async Task<PasswordLinkResult> SendPasswordLinkAsync(string userId, string setPasswordUrl, bool welcome, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null) return new(PasswordLinkStatus.NotFound);
        if (!user.IsActive) return new(PasswordLinkStatus.Inactive);
        if (string.IsNullOrWhiteSpace(user.Email)) return new(PasswordLinkStatus.NoEmail);
        if (!_email.IsConfigured) return new(PasswordLinkStatus.EmailNotSetUp);

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var link = $"{setPasswordUrl}?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
        var name = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));

        try
        {
            await _email.SendAsync(user.Email, name, welcome ? "Your FyreApp login" : "Set your FyreApp password",
                Body(user, link, welcome), ct);
            return new(PasswordLinkStatus.Sent);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Password link email to {Email} failed", user.Email);
            return new(PasswordLinkStatus.Failed, ex.Message);
        }
    }

    public static string Body(ApplicationUser user, string link, bool welcome)
    {
        var hi = string.IsNullOrWhiteSpace(user.FirstName) ? "Hi," : $"Hi {WebUtility.HtmlEncode(user.FirstName)},";
        var intro = welcome
            ? "You've been given a login to FyreApp. Choose your password to get started:"
            : "Use this link to choose a new password for FyreApp:";
        var href = WebUtility.HtmlEncode(link);
        return $"""
            <p>{hi}</p>
            <p>{intro}</p>
            <p><a href="{href}" style="display:inline-block;padding:10px 18px;background:#337ab7;color:#ffffff;text-decoration:none;border-radius:4px">Set your password</a></p>
            <p>You'll sign in with this email address: <strong>{WebUtility.HtmlEncode(user.Email)}</strong></p>
            <p style="color:#666;font-size:13px">The link works once, for 3 days. If it has expired, ask an admin to send you a new one.
            If you weren't expecting this email, you can ignore it.</p>
            """;
    }
}
