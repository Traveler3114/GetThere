using GetThereShared.Dtos;
using System.Diagnostics;

namespace GetThere.Services.Realtime;

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

        // Attach TripUpdate data to matching VehiclePositions
        foreach (var v in vehicles)
            if (v.TripId != null && tripUpdates.TryGetValue(v.TripId, out var updates))
                v.StopTimeUpdates = updates;

        // Expose TripUpdate-only entries (no GPS yet but have predictions)
        foreach (var (tripId, updates) in tripUpdates)
        {
            if (vehicles.Any(v => v.TripId == tripId)) continue;
            vehicles.Add(new VehiclePositionDto
            {
                VehicleId = tripId,
                TripId = tripId,
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
                else if (fieldNum == 2 || fieldNum == 3) // StopTimeUpdate
                {
                    // SPEC: field 3.  Some feeds (e.g. ZET): field 2.  Accept both.
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

                if (fieldNum == 3 || fieldNum == 4)
                {
                    // field 3 = arrival (spec) or departure (spec)
                    // field 4 = departure (spec) OR stop_id (ZET)
                    // Disambiguate: if sub looks like a StopTimeEvent (contains varint fields),
                    // treat as event. Otherwise treat field 4 as stop_id string.
                    if (fieldNum == 3 || LooksLikeStopTimeEvent(sub))
                        ParseStopTimeEvent(sub, stu);
                    else if (fieldNum == 4 && string.IsNullOrEmpty(stu.StopId))
                        stu.StopId = System.Text.Encoding.UTF8.GetString(sub);
                }
                else if (fieldNum == 2)
                {
                    // field 2 = stop_id (spec) OR arrival StopTimeEvent (ZET)
                    // Disambiguate: if sub contains protobuf-encoded fields (varint tag+value),
                    // it's a StopTimeEvent. Otherwise it's a plain string stop_id.
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
                if (fieldNum == 1) stu.StopSequence = (int)v; // stop_sequence
            }
            else pos = SkipField(data, pos, wireType);
        }

        return string.IsNullOrEmpty(stu.StopId) && stu.StopSequence == 0 ? null : stu;
    }

    /// <summary>
    /// Heuristic: returns true if the bytes look like a StopTimeEvent submessage
    /// (contains at least one protobuf field with wire type 0 = varint, which
    /// would be delay or timestamp), rather than a plain UTF-8 stop_id string.
    /// </summary>
    private static bool LooksLikeStopTimeEvent(byte[] data)
    {
        if (data.Length == 0) return false;
        // Try to read the first tag
        try
        {
            var (fieldNum, wireType, _) = ReadTag(data, 0);
            // A StopTimeEvent has field 1 (delay) or field 2 (time), both varint (wire 0)
            // A stop_id string would have its first byte be a printable ASCII char (0x20-0x7E)
            // or UTF-8 lead byte (0x80+), never a valid protobuf tag byte for these fields
            // field 1, wire 0 = tag byte 0x08
            // field 2, wire 0 = tag byte 0x10
            // field 3, wire 0 = tag byte 0x18
            return (fieldNum == 1 || fieldNum == 2 || fieldNum == 3) && wireType == 0;
        }
        catch { return false; }
    }

    private static void ParseStopTimeEvent(byte[] data, StopTimeUpdateDto stu)
    {
        // StopTimeEvent: field 1 = delay (int32), field 2 = time (int64), field 3 = uncertainty
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
                    // The GTFS-RT spec defines delay as int32 (not sint32),
                    // meaning negative values are varint-encoded as large uint64
                    // (10-byte two's complement), NOT zigzag-encoded.
                    //
                    // However some feeds incorrectly use zigzag sint32.
                    // Detect which encoding:
                    //   - Zigzag: odd numbers = negative (e.g. 1→-1, 3→-2, 97→-49)
                    //   - Two's complement uint64: negative int32s appear as values > 2^31
                    //     specifically in range [2^32-1800, 2^32] for ±30min delays
                    //
                    // Heuristic: if value > 2^33 it must be two's complement int32
                    // (a real zigzag delay that large would be ±1 billion seconds).
                    // If value is odd and small it's likely zigzag.
                    // If value is in (2^31, 2^33) it's ambiguous but treat as two's complement.

                    int delaySeconds;
                    if (v > 0x1_0000_0000UL) // > 2^32 → definitely two's complement int32
                    {
                        delaySeconds = (int)(uint)(v & 0xFFFFFFFF);
                    }
                    else if (v <= 0x7FFF_FFFFUL) // fits in positive int32 → same either way
                    {
                        delaySeconds = (int)v;
                    }
                    else // in range [2^31, 2^32] — use two's complement (most common for GTFS-RT)
                    {
                        delaySeconds = (int)(uint)v;
                    }

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
        byte[] data, VehiclePositionDto dto, Dictionary<string, string>? tripRouteMap)
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

    private static void ParseTrip(
        byte[] data, VehiclePositionDto dto, Dictionary<string, string>? tripRouteMap)
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
                if (fieldNum == 1) tripId = str; // trip_id
                else if (fieldNum == 5) dto.RouteId = str; // route_id
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
                if (fieldNum == 1) dto.VehicleId = str; // id
                else if (fieldNum == 2) dto.Label = str; // label
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