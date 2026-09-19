using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace PrepaidEngine.Api.Auth.Email;

/// <summary>Bound from the "Email" configuration section. The password is a secret (user-secrets or Email__Password), never a committed value.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP server. Leave empty to send nothing (Development then prints the message to the console instead).</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@prepaidengine.local";
    public string FromName { get; set; } = "Prepaid Engine";
}

public interface IEmailSender
{
    /// <summary>False when no way of sending is configured, so callers can say so instead of pretending a message went out.</summary>
    bool CanSend { get; }

    Task SendAsync(string toAddress, string subject, string textBody, CancellationToken cancellationToken = default);
}

/// <summary>Sends through the configured SMTP server (for example smtp.gmail.com:587 with an app password).</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

    public bool CanSend => !string.IsNullOrWhiteSpace(_options.Host);

    public async Task SendAsync(string toAddress, string subject, string textBody, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.UseSsl, Timeout = 15_000 };
        if (!string.IsNullOrEmpty(_options.User))
            client.Credentials = new NetworkCredential(_options.User, _options.Password);

        using var message = new MailMessage { From = new MailAddress(_options.FromAddress, _options.FromName), Subject = subject, Body = textBody, IsBodyHtml = false };
        message.To.Add(toAddress);
        await client.SendMailAsync(message, cancellationToken);
    }
}

/// <summary>
/// Development only, when no SMTP server is configured: prints the message to the API console so the flow can be tried
/// without a mail server. It is never used outside Development (see Program.cs), because it would put one-time codes in logs.
/// </summary>
public sealed class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) => _logger = logger;

    public bool CanSend => true;

    public Task SendAsync(string toAddress, string subject, string textBody, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("DEVELOPMENT ONLY: no SMTP server is configured, so this e-mail was not sent.\nTo: {To}\nSubject: {Subject}\n{Body}", toAddress, subject, textBody);
        return Task.CompletedTask;
    }
}

/// <summary>Used when nothing can send mail (a non-Development environment with no SMTP host).</summary>
public sealed class NoEmailSender : IEmailSender
{
    public bool CanSend => false;

    public Task SendAsync(string toAddress, string subject, string textBody, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("E-mail is not configured (Email:Host is empty).");
}
