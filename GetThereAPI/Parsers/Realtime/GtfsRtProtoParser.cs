using GetThereAPI.Entities;
using System.Diagnostics;

namespace GetThereAPI.Parsers.Realtime;

/// <summary>
/// Parses GTFS-RT protobuf feeds — fully generic, handles both spec-compliant
/// feeds and non-standard implementations (e.g. ZET).
///
/// GTFS-RT spec deviations handled:
///   • TripUpdate.stop_time_update: spec=field 3, some feeds use field 2 → accept both
///   • StopTimeUpdate.stop_id:      spec=field 2, some feeds use field 4 → accept both
///   • StopTimeUpdate.arrival:      spec=field 3, some feeds use field 2 → accept both
///   • Delay encoding: spec=zigzag sint32, some feeds use raw two's-complement int32
///     encoded as uint64 varint (e.g. ZET) → detect and handle both
/// </summary>
public class GtfsRtProtoParser : IRealtimeParser
{
    public Task<List<ParsedVehicle>> ParseAsync(
        byte[] data,
        TransitOperator op,
        Dictionary<string, string>? tripRouteMap)
    {
        var vehicles    = new List<ParsedVehicle>();
        var tripUpdates = new Dictionary<string, List<ParsedStopTimeUpdate>>(StringComparer.Ordinal);
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

        // Attach TripUpdate data to matching vehicles
        foreach (var v in vehicles)
            if (v.TripId != null && tripUpdates.TryGetValue(v.TripId, out var updates))
                v.StopTimeUpdates = updates;

        // Add TripUpdate-only entries (no GPS yet but have delay predictions)
        foreach (var (tripId, updates) in tripUpdates)
        {
            if (vehicles.Any(v => v.TripId == tripId)) continue;
            vehicles.Add(new ParsedVehicle
            {
                VehicleId       = tripId,
                TripId          = tripId,
                StopTimeUpdates = updates,
                IsScheduledOnly = true,
            });
        }

        Trace.WriteLine($"[GtfsRtProto:{op.Name}] " +
                        $"{vehicles.Count(v => !v.IsScheduledOnly)} vehicles, " +
                        $"{tripUpdates.Count} trip updates");

        return Task.FromResult(vehicles);
    }

    // ── Entity ────────────────────────────────────────────────────────────

    private static void ParseEntity(
        byte[] data,
        Dictionary<string, string>? tripRouteMap,
        List<ParsedVehicle> vehicles,
        Dictionary<string, List<ParsedStopTimeUpdate>> tripUpdates)
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
                    var vehicle = new ParsedVehicle();
                    ParseVehiclePosition(sub, vehicle, tripRouteMap);
                    if (vehicle.Lat != 0 || vehicle.Lon != 0)
                    {
                        if (string.IsNullOrEmpty(vehicle.VehicleId))
                            vehicle.VehicleId = entityId;
                        vehicles.Add(vehicle);
                    }
                }
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    // ── TripUpdate ────────────────────────────────────────────────────────

    private static (string? TripId, List<ParsedStopTimeUpdate> Updates) ParseTripUpdate(
        byte[] data)
    {
        int pos    = 0;
        string? tripId = null;
        var updates    = new List<ParsedStopTimeUpdate>();

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;
                if (fieldNum == 1)                          // TripDescriptor
                    tripId = ParseTripId(sub);
                else if (fieldNum == 2 || fieldNum == 3)    // StopTimeUpdate
                {
                    // SPEC: field 3. Some feeds (e.g. ZET): field 2. Accept both.
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

    private static ParsedStopTimeUpdate? ParseStopTimeUpdate(byte[] data)
    {
        int pos  = 0;
        var stu  = new ParsedStopTimeUpdate();

        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 2)
            {
                var (sub, p2) = ReadLengthDelimited(data, pos);
                pos = p2;

                if (fieldNum == 3 || fieldNum == 4)
                {
                    // field 3 = arrival (spec) or departure (spec)
                    // field 4 = departure (spec) OR stop_id (ZET)
                    // Disambiguate: if sub looks like a StopTimeEvent treat as event,
                    // otherwise treat field 4 as stop_id string.
                    if (fieldNum == 3 || LooksLikeStopTimeEvent(sub))
                        ParseStopTimeEvent(sub, stu);
                    else if (fieldNum == 4 && string.IsNullOrEmpty(stu.StopId))
                        stu.StopId = System.Text.Encoding.UTF8.GetString(sub);
                }
                else if (fieldNum == 2)
                {
                    // field 2 = stop_id (spec) OR arrival StopTimeEvent (ZET)
                    if (LooksLikeStopTimeEvent(sub))
                        ParseStopTimeEvent(sub, stu);
                    else if (string.IsNullOrEmpty(stu.StopId))
                        stu.StopId = System.Text.Encoding.UTF8.GetString(sub);
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

    /// <summary>
    /// Heuristic: returns true if bytes look like a StopTimeEvent submessage
    /// rather than a plain UTF-8 stop_id string.
    /// </summary>
    private static bool LooksLikeStopTimeEvent(byte[] data)
    {
        if (data.Length == 0) return false;
        try
        {
            var (fieldNum, wireType, _) = ReadTag(data, 0);
            return (fieldNum == 1 || fieldNum == 2 || fieldNum == 3) && wireType == 0;
        }
        catch { return false; }
    }

    private static void ParseStopTimeEvent(byte[] data, ParsedStopTimeUpdate stu)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            var (fieldNum, wireType, p1) = ReadTag(data, pos);
            pos = p1;
            if (fieldNum == 0) break;

            if (wireType == 0)
            {
                var (v, p2) = ReadVarint(data, pos);
                pos = p2;

                if (fieldNum == 1) // delay in seconds
                {
                    // GTFS-RT spec: delay is int32 (not sint32).
                    // Negative values encoded as large uint64 (two's complement), NOT zigzag.
                    // Some feeds incorrectly use zigzag — detect and handle both.
                    int delaySeconds;
                    if (v > 0x1_0000_0000UL)        // > 2^32 → definitely two's complement
                        delaySeconds = (int)(uint)(v & 0xFFFFFFFF);
                    else if (v <= 0x7FFF_FFFFUL)     // fits in positive int32 → same either way
                        delaySeconds = (int)v;
                    else                              // [2^31, 2^32] → treat as two's complement
                        delaySeconds = (int)(uint)v;

                    stu.DelaySeconds = delaySeconds;
                }
                else if (fieldNum == 2) // absolute unix arrival time
                {
                    stu.ArrivalUnix = (long)v;
                }
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    // ── VehiclePosition ───────────────────────────────────────────────────

    private static void ParseVehiclePosition(
        byte[] data, ParsedVehicle vehicle, Dictionary<string, string>? tripRouteMap)
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
                if (fieldNum == 1)      ParseTrip(sub, vehicle, tripRouteMap);
                else if (fieldNum == 2) ParsePosition(sub, vehicle);
                else if (fieldNum == 3) ParseVehicleDescriptor(sub, vehicle);
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    private static void ParseTrip(
            byte[] data, ParsedVehicle vehicle, Dictionary<string, string>? tripRouteMap)
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
                if (fieldNum == 1) tripId = str;   // trip_id
                else if (fieldNum == 5) vehicle.RouteId = str;   // route_id
            }
            else pos = SkipField(data, pos, wireType);
        }

        if (tripId != null) vehicle.TripId = tripId;

        // Use local variable — can't use property as out parameter
        if (string.IsNullOrEmpty(vehicle.RouteId) && tripId != null && tripRouteMap != null)
        {
            if (tripRouteMap.TryGetValue(tripId, out var mappedRouteId))
                vehicle.RouteId = mappedRouteId;
        }

        if (string.IsNullOrEmpty(vehicle.VehicleId) && tripId != null)
            vehicle.VehicleId = tripId;
    }

    private static void ParsePosition(byte[] data, ParsedVehicle vehicle)
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
                if (fieldNum == 1)      vehicle.Lat     = val;
                else if (fieldNum == 2) vehicle.Lon     = val;
                else if (fieldNum == 3) vehicle.Bearing = val;
            }
            else pos = SkipField(data, pos, wireType);
        }
    }

    private static void ParseVehicleDescriptor(byte[] data, ParsedVehicle vehicle)
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
                if (fieldNum == 1)      vehicle.VehicleId = str;   // id
                else if (fieldNum == 2) vehicle.Label     = str;   // label
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
        int len   = (int)length;
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
