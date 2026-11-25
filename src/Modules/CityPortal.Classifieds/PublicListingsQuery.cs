using System.ComponentModel.DataAnnotations;

namespace CityPortal.Classifieds;

public sealed class PublicListingsQuery
{
    [MaxLength(200)]
    public string? Query { get; init; }

    public Guid? CategoryId { get; init; }

    public Guid? DistrictId { get; init; }

    [Range(typeof(long), "0", "999999999999")]
    public long? MinPrice { get; init; }

    [Range(typeof(long), "0", "999999999999")]
    public long? MaxPrice { get; init; }

    [Range(1, 100000)]
    public int Page { get; init; } = 1;
}
