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

public class GeoJsonPointGeometry
{
    public string Type { get; set; } = "Point";
    public double[] Coordinates { get; set; } = [];
}

public class GeoJsonLineStringGeometry
{
    public string Type { get; set; } = "LineString";
    public IEnumerable<double[]> Coordinates { get; set; } = [];
}

public class GeoJsonPolygonGeometry
{
    public string Type { get; set; } = "Polygon";
    public IEnumerable<IEnumerable<double[]>> Coordinates { get; set; } = [];
}

public class GeoJsonMultiLineStringGeometry
{
    public string Type { get; set; } = "MultiLineString";
    public IEnumerable<IEnumerable<double[]>> Coordinates { get; set; } = [];
}

public class GeoJsonMultiPointGeometry
{
    public string Type { get; set; } = "MultiPoint";
    public IEnumerable<double[]> Coordinates { get; set; } = [];
}

public class GeoJsonMultiPolygonGeometry
{
    public string Type { get; set; } = "MultiPolygon";
    public IEnumerable<IEnumerable<IEnumerable<double[]>>> Coordinates { get; set; } = [];
}
