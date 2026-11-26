using System.ComponentModel.DataAnnotations;

namespace CityPortal.Api.Contracts;

/// <summary>Editor-reviewed text and the public revision and channel shown in the preview.</summary>
public sealed record QueueTelegramAnnouncementRequest(Guid RevisionId,
    [Required, MaxLength(33)] string Channel, [Required, MaxLength(3000)] string Text);
