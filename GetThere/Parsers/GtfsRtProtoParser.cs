using GetThereShared.Dtos;
using System.Diagnostics;

namespace GetThere.Services.Realtime;

/// <summary>
/// Parses GTFS-RT protobuf feeds.
/// Handles both VehiclePosition (field 4) and TripUpdate (field 3) entities.
/// ZET's feed contains both: ~180 VehiclePositions and ~320 TripUpdates with
/// per-stop arrival delays in seconds (int32, negative = early).
/// </summary>
public class GtfsRtProtoParser : IRealtimeParser
{
    public Task<List<VehiclePositionDto>> ParseAsync(
        byte[] data,
        TransitOperatorDto op,
        Dictionary<string, string>? tripRouteMap)
    {
        var vehicles = new List<VehiclePositionDto>();
        var tripUpdates = new Dictionary<string, List<StopTimeUpdateDto>>(StringComparer.Ordinal);
        int pos = 0;

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (fieldNum == 2 && wireType == 2) // FeedEntity
            {
                var (entityBytes, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                ParseEntity(entityBytes, tripRouteMap, vehicles, tripUpdates);
            }
            else pos = SkipField(data, pos, wireType);
        }

        // Attach trip updates to matching vehicles
        foreach (var v in vehicles)
            if (v.TripId != null && tripUpdates.TryGetValue(v.TripId, out var updates))
                v.StopTimeUpdates = updates;

        // Also expose trip updates for trips without a matching vehicle position
        // (scheduled trips that haven't started yet but have prediction data)
        foreach (var (tripId, updates) in tripUpdates)
        {
            if (vehicles.Any(v => v.TripId == tripId)) continue;
            vehicles.Add(new VehiclePositionDto
            {
                VehicleId = tripId,
                TripId = tripId,
                StopTimeUpdates = updates,
                // No position — lat/lon stay 0, filtered by map rendering
                IsScheduledOnly = true,
            });
        }

        Trace.WriteLine($"[GtfsRtProto:{op.Name}] {vehicles.Count(v => !v.IsScheduledOnly)} vehicles, " +
                        $"{tripUpdates.Count} trip updates");
        return Task.FromResult(vehicles);
    }

    // ── Entity ────────────────────────────────────────────────────────────

    private static void ParseEntity(
        byte[] data,
        Dictionary<string, string>? tripRouteMap,
        List<VehiclePositionDto> vehicles,
        Dictionary<string, List<StopTimeUpdateDto>> tripUpdates)
    {
        int pos = 0;
        string entityId = string.Empty;

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;

                if (fieldNum == 1)
                    entityId = System.Text.Encoding.UTF8.GetString(sub);
                else if (fieldNum == 3) // TripUpdate
                {
                    var (tripId, updates) = ParseTripUpdate(sub);
                    if (tripId != null && updates.Count > 0)
                        tripUpdates[tripId] = updates;
                }
                else if (fieldNum == 4) // VehiclePosition
                {
                    var dto = new VehiclePositionDto();
                    ParseVehiclePosition(sub, dto, tripRouteMap);
                    if (dto.Lat != 0 || dto.Lon != 0)
                    {
                        if (string.IsNullOrEmpty(dto.VehicleId)) dto.VehicleId = entityId;
                        vehicles.Add(dto);
                    }
                }
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    // ── TripUpdate ────────────────────────────────────────────────────────

    private static (string? TripId, List<StopTimeUpdateDto> Updates) ParseTripUpdate(byte[] data)
    {
        int pos = 0;
        string? tripId = null;
        var updates = new List<StopTimeUpdateDto>();

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                if (fieldNum == 1) // TripDescriptor
                    tripId = ParseTripId(sub);
                else if (fieldNum == 2) // StopTimeUpdate (ZET uses field 2)
                {
                    var stu = ParseStopTimeUpdate(sub);
                    if (stu != null) updates.Add(stu);
                }
            }
            else pos = SkipField(data, pos, wireType);
        }

        return (tripId, updates);
    }

    private static string? ParseTripId(byte[] data)
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
                if (fieldNum == 1) return System.Text.Encoding.UTF8.GetString(sub);
            }
            else pos = SkipField(data, pos, wireType);
        }
        return null;
    }

    private static StopTimeUpdateDto? ParseStopTimeUpdate(byte[] data)
    {
        int pos = 0;
        var stu = new StopTimeUpdateDto();

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                if (fieldNum == 4) // stop_id
                    stu.StopId = System.Text.Encoding.UTF8.GetString(sub);
                else if (fieldNum == 2 || fieldNum == 3) // arrival or departure StopTimeEvent
                {
                    // StopTimeEvent: field 1 = delay (int32 varint), field 2 = time (unix)
                    int ep = 0;
                    while (ep < sub.Length)
                    {
                        var (efn, ewt, ep2) = ReadTag(sub, ep);
                        ep = ep2;
                        if (efn == 0) break;
                        if (ewt == 0)
                        {
                            var (v, ep3) = ReadVarint(sub, ep);
                            ep = ep3;
                            if (efn == 1) // delay in seconds — int32 encoded as varint
                            {
                                // Mask to 32 bits and re-sign to handle negative delays
                                uint u32 = (uint)(v & 0xFFFFFFFF);
                                stu.DelaySeconds = (int)u32;
                            }
                            else if (efn == 2) // absolute unix timestamp
                                stu.ArrivalUnix = (long)v;
                        }
                        else ep = SkipField(sub, ep, ewt);
                    }
                }
            }
            else if (wireType == 0)
            {
                var (v, p2) = ReadVarint(data, pos);
                pos = p2;
                if (fieldNum == 1) stu.StopSequence = (int)v;
            }
            else pos = SkipField(data, pos, wireType);
        }

        return string.IsNullOrEmpty(stu.StopId) && stu.StopSequence == 0 ? null : stu;
    }

    // ── VehiclePosition ───────────────────────────────────────────────────

    private static void ParseVehiclePosition(byte[] data, VehiclePositionDto dto, Dictionary<string, string>? tripRouteMap)
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
                if (fieldNum == 1) ParseTrip(sub, dto, tripRouteMap);
                else if (fieldNum == 2) ParsePosition(sub, dto);
                else if (fieldNum == 3) ParseVehicleDescriptor(sub, dto);
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    private static void ParseTrip(byte[] data, VehiclePositionDto dto, Dictionary<string, string>? tripRouteMap)
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
                if (fieldNum == 1) tripId = str;
                else if (fieldNum == 5) dto.RouteId = str;
            }
            else pos = SkipField(data, pos, wireType);
        }

        if (tripId != null) dto.TripId = tripId;

        if (string.IsNullOrEmpty(dto.RouteId) && tripId != null)
            if (tripRouteMap != null && tripRouteMap.TryGetValue(tripId, out var mapped))
                dto.RouteId = mapped;

        if (string.IsNullOrEmpty(dto.VehicleId) && tripId != null)
            dto.VehicleId = tripId;
    }

    private static void ParsePosition(byte[] data, VehiclePositionDto dto)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;
            if (wireType == 5 && pos + 4 <= data.Length)
            {
                float val = BitConverter.ToSingle(data, pos); pos += 4;
                if (fieldNum == 1) dto.Lat = val;
                else if (fieldNum == 2) dto.Lon = val;
                else if (fieldNum == 3) dto.Bearing = val;
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

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
                if (fieldNum == 1) dto.VehicleId = str;
                else if (fieldNum == 2) dto.Label = str;
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