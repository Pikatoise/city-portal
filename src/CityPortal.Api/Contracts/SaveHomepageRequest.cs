namespace CityPortal.Api.Contracts;

public sealed record SaveHomepageRequest(long Version, Guid FirstCategoryId, Guid SecondCategoryId);
