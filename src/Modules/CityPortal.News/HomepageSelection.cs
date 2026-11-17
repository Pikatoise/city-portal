namespace CityPortal.News;

internal sealed class HomepageSelection
{
    public int Id { get; set; } = 1;
    public long Version { get; set; } = 1;
    public Guid? FirstCategoryId { get; set; }
    public Guid? SecondCategoryId { get; set; }
}
