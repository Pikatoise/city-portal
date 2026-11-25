namespace CityPortal.Classifieds;

public sealed record PublicListingSummary(Guid Id, string Title, string Category, string District, long? Price);
