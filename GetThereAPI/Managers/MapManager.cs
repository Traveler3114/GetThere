using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using GetThereAPI.Data;
using GetThereAPI.Entities;
using GetThereAPI.Services;
using GetThereShared.Contracts;
using GetThereAPI.Common;

namespace GetThereAPI.Managers;

public class MapManager
{
    private readonly TransitInfoApiClient _transitClient;
    private readonly AppDbContext _db;

    public MapManager(TransitInfoApiClient transitClient, AppDbContext db)
    {
        _transitClient = transitClient;
        _db = db;
    }

    public async Task<List<MapStationResponse>> GetStationsAsync(
        double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var stations = await _transitClient.GetStationsAsync(lat, lon, radiusKm, ct);
        return stations.Select(s => new MapStationResponse
        {
            Id = s.Id,
            OnestopId = s.OnestopId,
            Name = s.Name,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            StationType = s.StationType
        }).ToList();
    }

    public async Task<List<MapRouteResponse>> GetRoutesAsync(
        int? operatorId, string? routeType, CancellationToken ct = default)
    {
        var routes = await _transitClient.GetRoutesAsync(operatorId, routeType, ct);
        return routes.Select(r => new MapRouteResponse
        {
            Id = r.Id,
            OnestopId = r.OnestopId,
            Name = r.Name,
            RouteType = r.RouteType,
            OperatorName = r.OperatorName ?? string.Empty
        }).ToList();
    }

    public async Task<List<MapMobilityStationResponse>> GetMobilityStationsAsync(
        double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var stations = await _transitClient.GetMobilityStationsAsync(lat, lon, radiusKm, ct);
        return stations.Select(s => new MapMobilityStationResponse
        {
            StationId = s.StationId,
            Name = s.Name,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            AvailableVehicles = s.AvailableVehicles,
            Capacity = s.Capacity ?? 0,
            ProviderName = s.ProviderName
        }).ToList();
    }

    public async Task<List<MapVehicleResponse>> GetVehiclesAsync(
        string? feedId, double? lat, double? lon, double? radiusKm, CancellationToken ct = default)
    {
        var vehicles = await _transitClient.GetVehiclesAsync(feedId, lat, lon, radiusKm, ct);
        return vehicles.Select(v => new MapVehicleResponse
        {
            VehicleId = v.VehicleId,
            RouteId = v.RouteId,
            TripId = v.TripId,
            RouteShortName = v.RouteShortName,
            IsRealtime = v.IsRealtime,
            BlockId = v.BlockId,
            Latitude = v.Latitude,
            Longitude = v.Longitude,
            Bearing = v.Bearing,
            LastUpdated = v.LastUpdated
        }).ToList();
    }

    public async Task<List<MapDepartureResponse>> GetDeparturesAsync(string onestopId, CancellationToken ct = default)
    {
        // Departures endpoint not yet implemented in TransitInfoApiClient
        return [];
    }

    public async Task<List<MapOperatorResponse>> GetStationOperatorsAsync(string onestopId, CancellationToken ct = default)
    {
        // Station operators endpoint not yet implemented in TransitInfoApiClient
        return [];
    }

    public async Task<JsonElement> GetTransportTypesAsync(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse("[]");
        return doc.RootElement;
    }
}