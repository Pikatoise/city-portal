using System.Data.Common;
using System.Text.Json;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CityPortal.Notifications;

public sealed class MailConfigurationStore
{
    private readonly NotificationsDbContext _context;
    private readonly IDataProtector _protector;
    private readonly AccountMailOptions _fallback;

    internal MailConfigurationStore(NotificationsDbContext context, IDataProtectionProvider protection, IOptions<AccountMailOptions> options)
    {
        _context = context;
        _protector = protection.CreateProtector("CityPortal.SmtpConfiguration.v1");
        _fallback = options.Value;
    }

    public async Task<AccountMailOptions> GetAsync(CancellationToken cancellationToken)
    {
        MailConfiguration? configuration = await _context.MailConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return configuration is null ? _fallback : JsonSerializer.Deserialize<AccountMailOptions>(_protector.Unprotect(configuration.ProtectedOptions))
            ?? throw new InvalidOperationException("Invalid stored SMTP configuration.");
    }

    public async Task SaveAsync(AccountMailOptions options, DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        _context.Database.SetDbConnection(connection, contextOwnsConnection: false);
        await _context.Database.UseTransactionAsync(transaction, cancellationToken);
        _context.MailConfigurations.Add(new MailConfiguration { ProtectedOptions = _protector.Protect(JsonSerializer.Serialize(options)) });
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task TestAsync(AccountMailOptions options, string recipient, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.PortalName, options.SenderAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = options.PortalName + ": проверка отправки Email";
        message.Body = new TextPart("plain") { Text = "Проверка настроек почты выполнена. Это служебное письмо первоначальной настройки сайта." };
        using var smtp = new SmtpClient { Timeout = 10000 };
        await smtp.ConnectAsync(options.SmtpHost, options.SmtpPort, options.ImplicitTls ? SecureSocketOptions.SslOnConnect : options.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, timeout.Token);
        if (!string.IsNullOrEmpty(options.Username))
            await smtp.AuthenticateAsync(options.Username, options.Password ?? "", timeout.Token);
        await smtp.SendAsync(message, timeout.Token);
        await smtp.DisconnectAsync(true, timeout.Token);
    }
}
