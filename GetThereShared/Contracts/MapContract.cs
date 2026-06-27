namespace GetThereShared.Contracts;

public class MapStationResponse
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? StationType { get; set; }
}

public class MapRouteResponse
{
    public int Id { get; set; }
    public string OnestopId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? RouteType { get; set; }
    public string OperatorName { get; set; } = string.Empty;
}

public class MapMobilityStationResponse
{
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int AvailableVehicles { get; set; }
    public int Capacity { get; set; }
    public string ProviderName { get; set; } = string.Empty;
}

public class MapVehicleResponse
{
    public string VehicleId { get; set; } = string.Empty;
    public string? RouteId { get; set; }
    public string? TripId { get; set; }
    public string? RouteShortName { get; set; }
    public bool IsRealtime { get; set; }
    public string? BlockId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Bearing { get; set; }
    public DateTime? LastUpdated { get; set; }
}

public class MapDepartureResponse
{
    public string TripId { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string Headsign { get; set; } = string.Empty;
    public DateTime? ScheduledDeparture { get; set; }
    public DateTime? EstimatedDeparture { get; set; }
    public int? DelaySeconds { get; set; }
}

public class MapOperatorResponse
{
    public string GlobalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OperatorType { get; set; } = string.Empty;
    public bool HasTicketing { get; set; }
}
