namespace CityPortal.Media;

public sealed class MediaStorageOptions
{
    public string Endpoint { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "cityportal-media";
    public bool AllowHttp { get; set; }
}
