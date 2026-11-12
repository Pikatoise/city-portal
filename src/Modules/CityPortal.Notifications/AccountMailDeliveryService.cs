using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using MimeKit;
using Wolverine;
using Wolverine.EntityFrameworkCore;

namespace CityPortal.Notifications;

public sealed class AccountMailDeliveryService
{
    private static readonly int[] _retryMinutes = [1, 5, 15, 30, 60];
    private readonly NotificationsDbContext _context;
    private readonly IDbContextOutbox _outbox;
    private readonly IAccountMailLinkValidator _validator;
    private readonly IDataProtector _protector;
    private readonly AccountMailOptions _options;
    private readonly TimeProvider _time;

    internal AccountMailDeliveryService(NotificationsDbContext context, IDbContextOutbox outbox, IAccountMailLinkValidator validator,
        IDataProtectionProvider protection, IOptions<AccountMailOptions> options, TimeProvider time)
    {
        _context = context;
        _outbox = outbox;
        _validator = validator;
        _protector = protection.CreateProtector("CityPortal.AccountMail.v1");
        _options = options.Value;
        _time = time;
    }

    public async Task DeliverAsync(DeliverAccountMail message, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        // Keep a row lock through SMTP acknowledgement. A crash rolls back Sending to its previous recoverable state.
        EmailDelivery? delivery = await _context.Deliveries.FromSqlInterpolated(
            $"SELECT * FROM notifications.\"EmailDeliveries\" WHERE \"Id\" = {message.DeliveryId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        DateTimeOffset now = _time.GetUtcNow();

        if (delivery is null || delivery.State is "Sent" or "Failed" or "Skipped" || delivery.AttemptCount != message.Attempt)
            return;

        if (delivery.NextAttemptAtUtc > now)
            return;

        if (delivery.ExpiresAtUtc <= now || !await _validator.IsActiveAsync(delivery.AccountId, delivery.LinkId, cancellationToken))
        {
            delivery.State = "Skipped";
            delivery.CompletedAtUtc = now;
            delivery.ProtectedPayload = "";
            delivery.Version++;
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        delivery.State = "Sending";
        delivery.LastAttemptAtUtc = now;
        delivery.Version++;
        await _context.SaveChangesAsync(cancellationToken);
        string? errorCode = null;
        bool permanent = false;

        try
        {
            AccountMailPayload payload = JsonSerializer.Deserialize<AccountMailPayload>(_protector.Unprotect(delivery.ProtectedPayload))
                ?? throw new CryptographicException("Invalid protected mail payload.");
            await SendAsync(delivery, payload, cancellationToken);
        }
        catch (SmtpCommandException exception)
        {
            permanent = (int)exception.StatusCode >= 500;
            errorCode = permanent ? "smtp_permanent_rejection" : "smtp_temporary_rejection";
        }
        catch (Exception exception) when (exception is IOException or SmtpProtocolException or ServiceNotConnectedException
            or ServiceNotAuthenticatedException or System.Net.Sockets.SocketException or TimeoutException)
        {
            errorCode = "smtp_unavailable";
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            permanent = true;
            errorCode = "protected_payload_invalid";
        }
        catch (AuthenticationException)
        {
            permanent = true;
            errorCode = "smtp_authentication_failed";
        }
        catch (System.Security.Authentication.AuthenticationException)
        {
            errorCode = "smtp_tls_failed";
        }

        DateTimeOffset finished = _time.GetUtcNow();
        delivery.AttemptCount++;
        delivery.RetryCount++;
        delivery.ErrorCode = errorCode;
        _context.Attempts.Add(new EmailAttempt
        {
            Id = Guid.NewGuid(),
            DeliveryId = delivery.Id,
            Number = delivery.AttemptCount,
            StartedAtUtc = now,
            FinishedAtUtc = finished,
            Result = errorCode is null ? "Sent" : "Error",
            ErrorCode = errorCode
        });

        if (errorCode is null)
        {
            delivery.State = "Sent";
            delivery.CompletedAtUtc = finished;
            delivery.NextAttemptAtUtc = null;
            delivery.ProtectedPayload = "";
        }
        else if (permanent || delivery.RetryCount > _retryMinutes.Length)
        {
            delivery.State = "Failed";
            delivery.NextAttemptAtUtc = null;
        }
        else
        {
            delivery.State = "RetryScheduled";
            delivery.NextAttemptAtUtc = finished.AddMinutes(_retryMinutes[delivery.RetryCount - 1]);
            _outbox.Enroll(_context);
            await _outbox.ScheduleAsync(new DeliverAccountMail { DeliveryId = delivery.Id, Attempt = delivery.AttemptCount }, delivery.NextAttemptAtUtc.Value);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (delivery.State == "RetryScheduled")
            await _outbox.FlushOutgoingMessagesAsync();
    }

    private async Task SendAsync(EmailDelivery delivery, AccountMailPayload payload, CancellationToken cancellationToken)
    {
        bool recovery = delivery.Purpose == "recovery";
        string page = recovery ? "reset-password" : "confirm-email";
        string link = $"{_options.PublicOrigin.TrimEnd('/')}/account/{page}#linkId={delivery.LinkId:D}&token={Uri.EscapeDataString(payload.Token)}";
        string portal = _options.PortalName;
        string action = recovery ? "Восстановить пароль" : "Подтвердить Email";
        string instruction = recovery ? "Чтобы задать новый пароль, откройте ссылку:" : "Подтвердите Email, открыв ссылку и нажав кнопку подтверждения:";
        string footer = recovery
            ? "Ссылка действует 1 час с момента создания. Если вы не запрашивали восстановление пароля, проигнорируйте письмо."
            : "Ссылка действует 24 часа с момента создания. Если вы не регистрировались, проигнорируйте письмо.";
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(portal, _options.SenderAddress));
        message.To.Add(MailboxAddress.Parse(payload.Recipient));
        message.MessageId = $"{delivery.Id:N}@cityportal.invalid";
        message.Subject = $"{portal}: {(recovery ? "восстановление пароля" : "подтверждение Email")}";
        var body = new BodyBuilder
        {
            TextBody = $"{portal}\n\n{instruction}\n{link}\n\n{footer}",
            HtmlBody = $"<h1>{WebUtility.HtmlEncode(portal)}</h1><p>{instruction}</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">{action}</a></p><p>{footer}</p>"
        };
        message.Body = body.ToMessageBody();
        using var client = new SmtpClient { Timeout = 30000 };
        await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, _options.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);

        if (!string.IsNullOrEmpty(_options.Username))
            await client.AuthenticateAsync(_options.Username, _options.Password ?? "", cancellationToken);

        await client.SendAsync(message, cancellationToken);
        // SMTP acceptance is final even if QUIT subsequently fails.
        try
        {
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or SmtpProtocolException or ServiceNotConnectedException)
        {
        }
    }
}
