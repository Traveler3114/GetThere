// Hand-written GTFS-RT protobuf message classes.
// Replaces code that would normally be generated from gtfs-realtime.proto.
// Uses Google.Protobuf directly — no Grpc.Tools / protoc required.

using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;

namespace transit_realtime;

// ---------------------------------------------------------------------------
// FeedMessage
// ---------------------------------------------------------------------------
public sealed class FeedMessage : IMessage<FeedMessage>
{
    public static readonly MessageParser<FeedMessage> Parser = new(() => new FeedMessage());
    public MessageDescriptor Descriptor => null!;

    public FeedHeader Header { get; set; } = new();
    public RepeatedField<FeedEntity> Entity { get; } = new();

    public FeedMessage Clone() => new() { Header = Header.Clone(), Entity = { Entity } };

    public void MergeFrom(FeedMessage other)
    {
        Header.MergeFrom(other.Header);
        Entity.Add(other.Entity);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: Header = new FeedHeader(); input.ReadMessage(Header); break;
                case 18: var e = new FeedEntity(); input.ReadMessage(e); Entity.Add(e); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteMessage(Header);
        foreach (var entity in Entity)
        { output.WriteTag(2, WireFormat.WireType.LengthDelimited); output.WriteMessage(entity); }
    }

    public int CalculateSize() =>
        CodedOutputStream.ComputeMessageSize(Header) + 2 +
        Entity.Sum(e => CodedOutputStream.ComputeMessageSize(e) + 1);

    public bool Equals(FeedMessage? other) => other != null;

    public byte[] ToByteArray()
    {
        using var ms = new MemoryStream();
        using var output = new CodedOutputStream(ms);
        WriteTo(output);
        output.Flush();
        return ms.ToArray();
    }
}

// ---------------------------------------------------------------------------
// FeedHeader
// ---------------------------------------------------------------------------
public sealed class FeedHeader : IMessage<FeedHeader>
{
    public static readonly MessageParser<FeedHeader> Parser = new(() => new FeedHeader());
    public MessageDescriptor Descriptor => null!;

    public string GtfsRealtimeVersion { get; set; } = "2.0";
    public Types.Incrementality Incrementality { get; set; } = Types.Incrementality.FullDataset;
    public ulong Timestamp { get; set; }

    public static class Types
    {
        public enum Incrementality { FullDataset = 0, Differential = 1 }
    }

    public FeedHeader Clone() => new()
    {
        GtfsRealtimeVersion = GtfsRealtimeVersion,
        Incrementality = Incrementality,
        Timestamp = Timestamp
    };

    public void MergeFrom(FeedHeader other)
    {
        if (other.GtfsRealtimeVersion.Length > 0) GtfsRealtimeVersion = other.GtfsRealtimeVersion;
        Incrementality = other.Incrementality;
        Timestamp = other.Timestamp;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: GtfsRealtimeVersion = input.ReadString(); break;
                case 16: Incrementality = (Types.Incrementality)input.ReadEnum(); break;
                case 24: Timestamp = input.ReadUInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (GtfsRealtimeVersion.Length > 0) { output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteString(GtfsRealtimeVersion); }
        if (Incrementality != Types.Incrementality.FullDataset) { output.WriteTag(2, WireFormat.WireType.Varint); output.WriteEnum((int)Incrementality); }
        if (Timestamp != 0) { output.WriteTag(3, WireFormat.WireType.Varint); output.WriteUInt64(Timestamp); }
    }

    public int CalculateSize()
    {
        int size = 0;
        if (GtfsRealtimeVersion.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(GtfsRealtimeVersion);
        if (Incrementality != Types.Incrementality.FullDataset) size += 1 + CodedOutputStream.ComputeEnumSize((int)Incrementality);
        if (Timestamp != 0) size += 1 + CodedOutputStream.ComputeUInt64Size(Timestamp);
        return size;
    }

    public bool Equals(FeedHeader? other) => other != null;
}

// ---------------------------------------------------------------------------
// FeedEntity
// ---------------------------------------------------------------------------
public sealed class FeedEntity : IMessage<FeedEntity>
{
    public static readonly MessageParser<FeedEntity> Parser = new(() => new FeedEntity());
    public MessageDescriptor Descriptor => null!;

    public string Id { get; set; } = "";
    public bool IsDeleted { get; set; }
    public TripUpdate? TripUpdate { get; set; }

    public FeedEntity Clone() => new() { Id = Id, IsDeleted = IsDeleted, TripUpdate = TripUpdate?.Clone() };

    public void MergeFrom(FeedEntity other)
    {
        if (other.Id.Length > 0) Id = other.Id;
        IsDeleted = other.IsDeleted;
        if (other.TripUpdate != null) { TripUpdate ??= new TripUpdate(); TripUpdate.MergeFrom(other.TripUpdate); }
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: Id = input.ReadString(); break;
                case 16: IsDeleted = input.ReadBool(); break;
                case 26: TripUpdate = new TripUpdate(); input.ReadMessage(TripUpdate); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (Id.Length > 0) { output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteString(Id); }
        if (IsDeleted) { output.WriteTag(2, WireFormat.WireType.Varint); output.WriteBool(IsDeleted); }
        if (TripUpdate != null) { output.WriteTag(3, WireFormat.WireType.LengthDelimited); output.WriteMessage(TripUpdate); }
    }

    public int CalculateSize()
    {
        int size = 0;
        if (Id.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(Id);
        if (IsDeleted) size += 2;
        if (TripUpdate != null) size += 1 + CodedOutputStream.ComputeMessageSize(TripUpdate);
        return size;
    }

    public bool Equals(FeedEntity? other) => other != null;
}

// ---------------------------------------------------------------------------
// TripUpdate
// ---------------------------------------------------------------------------
public sealed class TripUpdate : IMessage<TripUpdate>
{
    public static readonly MessageParser<TripUpdate> Parser = new(() => new TripUpdate());
    public MessageDescriptor Descriptor => null!;

    public TripDescriptor Trip { get; set; } = new();
    public RepeatedField<Types.StopTimeUpdate> StopTimeUpdate { get; } = new();

    public static class Types
    {
        public sealed class StopTimeUpdate : IMessage<StopTimeUpdate>
        {
            public static readonly MessageParser<StopTimeUpdate> Parser = new(() => new StopTimeUpdate());
            public MessageDescriptor Descriptor => null!;

            public uint StopSequence { get; set; }
            public string StopId { get; set; } = "";
            public StopTimeEvent? Arrival { get; set; }
            public StopTimeEvent? Departure { get; set; }

            public StopTimeUpdate Clone() => new()
            {
                StopSequence = StopSequence,
                StopId = StopId,
                Arrival = Arrival?.Clone(),
                Departure = Departure?.Clone()
            };

            public void MergeFrom(StopTimeUpdate other)
            {
                StopSequence = other.StopSequence;
                if (other.StopId.Length > 0) StopId = other.StopId;
                if (other.Arrival != null) { Arrival ??= new(); Arrival.MergeFrom(other.Arrival); }
                if (other.Departure != null) { Departure ??= new(); Departure.MergeFrom(other.Departure); }
            }

            public void MergeFrom(CodedInputStream input)
            {
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    switch (tag)
                    {
                        case 8: StopSequence = input.ReadUInt32(); break;
                        case 18: Arrival = new StopTimeEvent(); input.ReadMessage(Arrival); break;
                        case 26: Departure = new StopTimeEvent(); input.ReadMessage(Departure); break;
                        case 34: StopId = input.ReadString(); break;
                        default: input.SkipLastField(); break;
                    }
                }
            }

            public void WriteTo(CodedOutputStream output)
            {
                if (StopSequence != 0) { output.WriteTag(1, WireFormat.WireType.Varint); output.WriteUInt32(StopSequence); }
                if (Arrival != null) { output.WriteTag(2, WireFormat.WireType.LengthDelimited); output.WriteMessage(Arrival); }
                if (Departure != null) { output.WriteTag(3, WireFormat.WireType.LengthDelimited); output.WriteMessage(Departure); }
                if (StopId.Length > 0) { output.WriteTag(4, WireFormat.WireType.LengthDelimited); output.WriteString(StopId); }
            }

            public int CalculateSize()
            {
                int size = 0;
                if (StopSequence != 0) size += 1 + CodedOutputStream.ComputeUInt32Size(StopSequence);
                if (Arrival != null) size += 1 + CodedOutputStream.ComputeMessageSize(Arrival);
                if (Departure != null) size += 1 + CodedOutputStream.ComputeMessageSize(Departure);
                if (StopId.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(StopId);
                return size;
            }

            public bool Equals(StopTimeUpdate? other) => other != null;
        }

        public sealed class StopTimeEvent : IMessage<StopTimeEvent>
        {
            public static readonly MessageParser<StopTimeEvent> Parser = new(() => new StopTimeEvent());
            public MessageDescriptor Descriptor => null!;

            public int Delay { get; set; }

            public StopTimeEvent Clone() => new() { Delay = Delay };
            public void MergeFrom(StopTimeEvent other) { Delay = other.Delay; }

            public void MergeFrom(CodedInputStream input)
            {
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    switch (tag)
                    {
                        case 8: Delay = input.ReadInt32(); break;
                        default: input.SkipLastField(); break;
                    }
                }
            }

            public void WriteTo(CodedOutputStream output)
            {
                if (Delay != 0) { output.WriteTag(1, WireFormat.WireType.Varint); output.WriteInt32(Delay); }
            }

            public int CalculateSize() => Delay != 0 ? 1 + CodedOutputStream.ComputeInt32Size(Delay) : 0;
            public bool Equals(StopTimeEvent? other) => other != null;
        }
    }

    public TripUpdate Clone() => new() { Trip = Trip.Clone(), StopTimeUpdate = { StopTimeUpdate } };

    public void MergeFrom(TripUpdate other)
    {
        Trip.MergeFrom(other.Trip);
        StopTimeUpdate.Add(other.StopTimeUpdate);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: Trip = new TripDescriptor(); input.ReadMessage(Trip); break;
                case 18: var s = new Types.StopTimeUpdate(); input.ReadMessage(s); StopTimeUpdate.Add(s); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteMessage(Trip);
        foreach (var s in StopTimeUpdate)
        { output.WriteTag(2, WireFormat.WireType.LengthDelimited); output.WriteMessage(s); }
    }

    public int CalculateSize() =>
        1 + CodedOutputStream.ComputeMessageSize(Trip) +
        StopTimeUpdate.Sum(s => 1 + CodedOutputStream.ComputeMessageSize(s));

    public bool Equals(TripUpdate? other) => other != null;
}

// ---------------------------------------------------------------------------
// TripDescriptor
// ---------------------------------------------------------------------------
public sealed class TripDescriptor : IMessage<TripDescriptor>
{
    public static readonly MessageParser<TripDescriptor> Parser = new(() => new TripDescriptor());
    public MessageDescriptor Descriptor => null!;

    public string TripId { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string StartDate { get; set; } = "";

    public TripDescriptor Clone() => new() { TripId = TripId, RouteId = RouteId, StartDate = StartDate };

    public void MergeFrom(TripDescriptor other)
    {
        if (other.TripId.Length > 0) TripId = other.TripId;
        if (other.RouteId.Length > 0) RouteId = other.RouteId;
        if (other.StartDate.Length > 0) StartDate = other.StartDate;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: TripId = input.ReadString(); break;
                case 18: StartDate = input.ReadString(); break;
                case 42: RouteId = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (TripId.Length > 0) { output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteString(TripId); }
        if (StartDate.Length > 0) { output.WriteTag(2, WireFormat.WireType.LengthDelimited); output.WriteString(StartDate); }
        if (RouteId.Length > 0) { output.WriteTag(5, WireFormat.WireType.LengthDelimited); output.WriteString(RouteId); }
    }

    public int CalculateSize()
    {
        int size = 0;
        if (TripId.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(TripId);
        if (StartDate.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(StartDate);
        if (RouteId.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(RouteId);
        return size;
    }

    public bool Equals(TripDescriptor? other) => other != null;
}