namespace CityPortal.IntegrationTests;

// API factories share the process-wide upload slot, so their upload scenarios must not race one another.
[CollectionDefinition("Media upload slot")]
public sealed class MediaUploadCollection
{
}
