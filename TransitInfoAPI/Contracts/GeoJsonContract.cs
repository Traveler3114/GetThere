namespace TransitInfoAPI.Contracts;

public class GeoJsonFeature
{
    public string Type { get; set; } = "Feature";
    public object? Geometry { get; set; }
    public Dictionary<string, object?>? Properties { get; set; }
}

public class GeoJsonFeatureCollection
{
    public string Type { get; set; } = "FeatureCollection";
    public List<GeoJsonFeature> Features { get; set; } = [];
}
