using System.ComponentModel.DataAnnotations;

namespace CityPortal.Api.Contracts;

/// <summary>Records the editor's decision after inspecting the channel.</summary>
public sealed record ResolveTelegramAnnouncementRequest([Required, MaxLength(33)] string Channel,
    [Range(1, long.MaxValue)] long Version, bool Retry, [MaxLength(250)] string? PostUrl, Guid? RevisionId, [MaxLength(3000)] string? Text, bool Cancel = false);
