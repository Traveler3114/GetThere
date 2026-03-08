using GetThereShared.Dtos;
using System.Diagnostics;

namespace GetThere.Services.Realtime;

/// <summary>
/// Parses GTFS-RT Vehicle Positions protobuf feeds.
/// Implements the full GTFS-RT spec (FeedMessage → FeedEntity → VehiclePosition).
/// Uses a pre-built trip→route map so it works regardless of trip_id format.
/// </summary>
public class GtfsRtProtoParser : IRealtimeParser
{
    public Task<List<VehiclePositionDto>> ParseAsync(
        byte[] data,
        TransitOperatorDto op,
        Dictionary<string, string>? tripRouteMap)
    {
        var result = new List<VehiclePositionDto>();
        int pos = 0, entityCount = 0;

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (fieldNum == 2 && wireType == 2) // FeedEntity
            {
                entityCount++;
                var (entityBytes, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                var dto = ParseEntity(entityBytes, tripRouteMap);
                if (dto != null) result.Add(dto);
            }
            else pos = SkipField(data, pos, wireType);
        }

        Trace.WriteLine($"[GtfsRtProto:{op.Name}] {result.Count} vehicles from {entityCount} entities");
        return Task.FromResult(result);
    }

    // ── Entity ────────────────────────────────────────────────────────────

    private static VehiclePositionDto? ParseEntity(
        byte[] data,
        Dictionary<string, string>? tripRouteMap)
    {
        int pos = 0;
        string entityId = string.Empty;
        VehiclePositionDto? dto = null;

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;

                if (fieldNum == 1) // entity id
                    entityId = System.Text.Encoding.UTF8.GetString(sub);
                else if (fieldNum == 4) // VehiclePosition (standard GTFS-RT field 4)
                {
                    dto = new VehiclePositionDto();
                    ParseVehiclePosition(sub, dto, tripRouteMap);
                }
                // field 3 = TripUpdate, field 5 = Alert — skip both
            }
            else pos = SkipField(data, pos, wireType);
        }

        if (dto == null || (dto.Lat == 0 && dto.Lon == 0)) return null;
        if (string.IsNullOrEmpty(dto.VehicleId)) dto.VehicleId = entityId;
        return dto;
    }

    // ── VehiclePosition ───────────────────────────────────────────────────

    private static void ParseVehiclePosition(
        byte[] data,
        VehiclePositionDto dto,
        Dictionary<string, string>? tripRouteMap)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                if      (fieldNum == 1) ParseTrip(sub, dto, tripRouteMap);
                else if (fieldNum == 2) ParsePosition(sub, dto);
                else if (fieldNum == 3) ParseVehicleDescriptor(sub, dto);
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    // ── TripDescriptor ────────────────────────────────────────────────────

    private static void ParseTrip(
        byte[] data,
        VehiclePositionDto dto,
        Dictionary<string, string>? tripRouteMap)
    {
        int pos = 0;
        string? tripId = null;

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                var str = System.Text.Encoding.UTF8.GetString(sub);

                if (fieldNum == 1) tripId = str;              // trip_id
                else if (fieldNum == 5) dto.RouteId = str;    // route_id (field 5 in spec)
            }
            else pos = SkipField(data, pos, wireType);
        }

        // Prefer route_id from feed; fall back to trip map; last resort leave null
        if (string.IsNullOrEmpty(dto.RouteId) && tripId != null)
        {
            if (tripRouteMap != null && tripRouteMap.TryGetValue(tripId, out var mapped))
                dto.RouteId = mapped;
            // else: no map available yet — route will show as unknown color
        }

        // Use trip_id as vehicle id fallback if nothing else set it
        if (string.IsNullOrEmpty(dto.VehicleId) && tripId != null)
            dto.VehicleId = tripId;
    }

    // ── Position (lat/lon as fixed32 floats) ──────────────────────────────

    private static void ParsePosition(byte[] data, VehiclePositionDto dto)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 5 && pos + 4 <= data.Length) // fixed32 float
            {
                float val = BitConverter.ToSingle(data, pos);
                pos += 4;
                if      (fieldNum == 1) dto.Lat     = val;
                else if (fieldNum == 2) dto.Lon     = val;
                else if (fieldNum == 3) dto.Bearing = val;
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    // ── VehicleDescriptor ─────────────────────────────────────────────────

    private static void ParseVehicleDescriptor(byte[] data, VehiclePositionDto dto)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                var str = System.Text.Encoding.UTF8.GetString(sub);
                if      (fieldNum == 1) dto.VehicleId = str; // id
                else if (fieldNum == 2) dto.Label     = str; // label
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    // ── Protobuf primitives ───────────────────────────────────────────────

    private static (int field, int wire, int pos) ReadTag(byte[] data, int pos)
    {
        if (pos >= data.Length) return (0, 0, pos);
        var (varint, newPos) = ReadVarint(data, pos);
        return ((int)(varint >> 3), (int)(varint & 7), newPos);
    }

    private static (ulong value, int pos) ReadVarint(byte[] data, int pos)
    {
        ulong result = 0; int shift = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return (result, pos);
    }

    internal static (byte[] bytes, int pos) ReadLengthDelimited(byte[] data, int pos)
    {
        var (length, newPos) = ReadVarint(data, pos);
        int len = (int)length;
        var bytes = new byte[len];
        Array.Copy(data, newPos, bytes, 0, Math.Min(len, data.Length - newPos));
        return (bytes, newPos + len);
    }

    internal static int SkipField(byte[] data, int pos, int wireType)
    {
        switch (wireType)
        {
            case 0: while (pos < data.Length && (data[pos++] & 0x80) != 0) { } return pos;
            case 1: return pos + 8;
            case 2: var (len, p) = ReadVarint(data, pos); return p + (int)len;
            case 5: return pos + 4;
            default: return data.Length;
        }
    }
}
