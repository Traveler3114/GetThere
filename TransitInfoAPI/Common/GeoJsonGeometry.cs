using NetTopologySuite.Geometries;
using TransitInfoAPI.Contracts;

namespace TransitInfoAPI.Common;

public static class GeoJsonGeometry
{
    public static object FromNtsGeometry(Geometry? geom)
    {
        if (geom is Point p)
            return new GeoJsonPointGeometry { Coordinates = [p.X, p.Y] };

        if (geom is LineString ls)
            return new GeoJsonLineStringGeometry { Coordinates = ls.Coordinates.Select(c => new[] { c.X, c.Y }) };

        if (geom is Polygon poly)
        {
            var rings = new List<IEnumerable<double[]>>();
            rings.Add(poly.Shell.Coordinates.Select(c => new[] { c.X, c.Y }));
            foreach (var hole in poly.Holes)
                rings.Add(hole.Coordinates.Select(c => new[] { c.X, c.Y }));
            return new GeoJsonPolygonGeometry { Coordinates = rings };
        }

        if (geom is MultiLineString mls)
            return new GeoJsonMultiLineStringGeometry
            {
                Coordinates = mls.Geometries.Select(g =>
                    ((LineString)g).Coordinates.Select(c => new[] { c.X, c.Y }))
            };

        if (geom is MultiPoint mp)
            return new GeoJsonMultiPointGeometry
            {
                Coordinates = mp.Geometries.Select(g =>
                    new[] { ((Point)g).X, ((Point)g).Y })
            };

        if (geom is MultiPolygon mpoly)
            return new GeoJsonMultiPolygonGeometry
            {
                Coordinates = mpoly.Geometries.Select(g =>
                {
                    var polyG = (Polygon)g;
                    var rings = new List<IEnumerable<double[]>>();
                    rings.Add(polyG.Shell.Coordinates.Select(c => new[] { c.X, c.Y }));
                    foreach (var hole in polyG.Holes)
                        rings.Add(hole.Coordinates.Select(c => new[] { c.X, c.Y }));
                    return rings;
                })
            };

        throw new InvalidOperationException($"Unsupported geometry type: {geom?.GetType().Name ?? "null"}");
    }

    public static GeoJsonFeatureCollection ToPointCollection<T>(
        IEnumerable<T> items,
        Func<T, double> getLat,
        Func<T, double> getLon,
        Func<T, Dictionary<string, object?>> getProps)
    {
        var features = items.Select(item => new GeoJsonFeature
        {
            Geometry = new GeoJsonPointGeometry { Coordinates = [getLon(item), getLat(item)] },
            Properties = getProps(item)
        }).ToList();

        return new GeoJsonFeatureCollection { Features = features };
    }

    public static GeoJsonFeatureCollection ToLineStringCollection<T>(
        IEnumerable<T> items,
        Func<T, Geometry?> getGeometry,
        Func<T, Dictionary<string, object?>> getProps)
    {
        var features = items.Select(item =>
        {
            var geom = getGeometry(item);
            return new GeoJsonFeature
            {
                Geometry = geom != null ? FromNtsGeometry(geom) : null,
                Properties = getProps(item)
            };
        }).Where(f => f.Geometry != null).ToList();

        return new GeoJsonFeatureCollection { Features = features };
    }
}
