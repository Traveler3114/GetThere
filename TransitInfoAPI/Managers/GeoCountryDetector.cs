namespace TransitInfoAPI.Managers;

public static class GeoCountryDetector
{
    private record Box(double MinLat, double MaxLat, double MinLon, double MaxLon, string IsoCode);

    private static readonly Box[] _boxes;

    static GeoCountryDetector()
    {
        _boxes =
        [
            // Micro-states
            new(47.05, 47.20, 9.50, 9.65, "LI"),
            new(43.73, 43.78, 7.40, 7.47, "MC"),
            new(41.90, 41.91, 12.44, 12.46, "VA"),
            new(43.90, 44.00, 12.40, 12.50, "SM"),

            // Italy — northern specific sub-boxes (no overlap with medium countries)
            new(45.40, 45.60, 12.20, 12.50, "IT"),  // Venezia
            new(45.55, 45.80, 13.70, 13.90, "IT"),  // Trieste
            new(45.90, 46.20, 13.10, 13.40, "IT"),  // Udine
            new(46.40, 46.60, 11.30, 11.50, "IT"),  // Bolzano
            new(46.40, 46.60, 13.50, 13.70, "IT"),  // Tarvisio
            new(44.30, 44.70, 11.10, 11.50, "IT"),  // Bologna
            new(41.70, 42.10, 12.30, 12.70, "IT"),  // Roma

            // AT Carinthia sub-box — catches Austrian stations overlapping SI territory, checked before SI
            new(46.45, 47.00, 13.00, 14.50, "AT"),  // Austria (Carinthia south)

            // Specific SI sub-boxes (checked before HR to prevent Slovenian stations from falling to HR)
            new(45.45, 45.60, 13.50, 13.80, "SI"),  // Slovenia (Koper/Izola/Piran coast)
            new(45.60, 46.60, 13.50, 14.50, "SI"),  // Slovenia (Ljubljana/Jesenice/Kranj)
            new(45.75, 46.60, 14.50, 15.80, "SI"),  // Slovenia (Celje/Maribor/Novo Mesto)

            // Croatia — checked after SI sub-boxes; covers everything SI doesn't claim
            new(42.40, 46.60, 13.50, 19.40, "HR"),  // Croatia

            new(44.10, 46.20, 15.70, 23.30, "BA"),  // Bosnia and Herzegovina
            new(42.20, 43.00, 18.40, 19.50, "ME"),  // Montenegro
            new(41.80, 42.70, 19.20, 21.10, "AL"),  // Albania
            new(46.30, 49.00, 9.50, 17.20, "AT"),   // Austria
            new(45.70, 47.80, 12.10, 22.90, "HU"),  // Hungary
            new(45.80, 47.80, 12.80, 22.50, "SK"),  // Slovakia
            new(41.90, 46.50, 18.40, 23.30, "RO"),  // Romania
            new(41.20, 44.20, 22.50, 28.90, "BG"),  // Bulgaria
            new(38.50, 42.00, 19.50, 29.70, "GR"),  // Greece

            // Broad Italy boxes — cover larger areas, lower priority
            new(46.20, 46.80, 10.50, 12.50, "IT"),  // Italy north (South Tyrol, Trentino)
            new(35.50, 46.20, 6.60, 12.80, "IT"),   // Italy west (Tyrrhenian side)
            new(37.50, 42.80, 12.80, 18.60, "IT"),  // Italy east (Adriatic side)
            new(49.50, 53.60, 3.30, 7.20, "BE"),    // Belgium
            new(50.70, 53.50, 3.40, 7.20, "NL"),    // Netherlands
            new(49.50, 51.10, 2.00, 6.40, "LU"),    // Luxembourg
            new(46.80, 48.50, 7.40, 10.50, "CH"),   // Switzerland
            new(49.00, 55.10, 5.00, 15.10, "DE"),   // Germany
            new(49.00, 54.50, 14.10, 24.10, "PL"),  // Poland
            new(48.50, 51.10, 12.00, 18.90, "CZ"),  // Czech Republic

            // Large countries
            new(42.40, 48.00, -4.90, 8.20, "FR"),   // France
            new(36.00, 43.80, -9.40, 3.30, "ES"),   // Spain
            new(36.90, 42.20, -8.20, -6.40, "PT"),  // Portugal
            new(51.00, 55.00, -8.00, -5.40, "IE"),  // Ireland
            new(49.90, 60.90, -8.00, 1.80, "GB"),   // United Kingdom
            new(47.00, 50.00, 22.00, 26.00, "MD"),  // Moldova
            new(41.30, 48.50, 22.00, 40.00, "UA"),  // Ukraine
            new(51.10, 54.10, 19.10, 24.10, "LT"),  // Lithuania
            new(55.00, 58.10, 20.00, 28.30, "LV"),  // Latvia
            new(57.50, 59.70, 21.40, 28.30, "EE"),  // Estonia
            new(54.20, 57.90, 4.00, 12.70, "DK"),   // Denmark
            new(55.30, 56.50, 10.70, 15.20, "SE"),  // Sweden (south)
            new(56.50, 69.50, 11.00, 32.00, "NO"),  // Norway
            new(59.00, 70.10, 20.00, 32.00, "FI"),  // Finland
            new(41.00, 43.60, 40.00, 46.70, "GE"),  // Georgia
            new(38.80, 41.30, 43.50, 46.70, "AM"),  // Armenia
            new(38.80, 41.70, 44.70, 50.80, "AZ"),  // Azerbaijan
            new(41.50, 43.30, 27.50, 30.00, "TR"),  // Turkey (European)
            new(43.00, 46.80, 27.00, 30.00, "TR"),  // Turkey (east)
        ];
    }

    public static string? DetectCountryIso(double lat, double lon)
    {
        foreach (var box in _boxes)
        {
            if (lat >= box.MinLat && lat <= box.MaxLat && lon >= box.MinLon && lon <= box.MaxLon)
                return box.IsoCode;
        }
        return null;
    }
}
