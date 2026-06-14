// Hand-written GTFS-RT protobuf message classes.
// Replaces code that would normally be generated from gtfs-realtime.proto.
// Uses Google.Protobuf directly � no Grpc.Tools / protoc required.

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

    public bool Equals(FeedMessage? other) => other is not null;

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

    public bool Equals(FeedHeader? other) => other is not null;
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
    public VehiclePosition? VehiclePosition { get; set; }

    public FeedEntity Clone() => new() { Id = Id, IsDeleted = IsDeleted, TripUpdate = TripUpdate?.Clone(), VehiclePosition = VehiclePosition?.Clone() };

    public void MergeFrom(FeedEntity other)
    {
        if (other.Id.Length > 0) Id = other.Id;
        IsDeleted = other.IsDeleted;
        if (other.TripUpdate is not null) { TripUpdate ??= new TripUpdate(); TripUpdate.MergeFrom(other.TripUpdate); }
        if (other.VehiclePosition is not null) { VehiclePosition ??= new VehiclePosition(); VehiclePosition.MergeFrom(other.VehiclePosition); }
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
                case 34: VehiclePosition = new VehiclePosition(); input.ReadMessage(VehiclePosition); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (Id.Length > 0) { output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteString(Id); }
        if (IsDeleted) { output.WriteTag(2, WireFormat.WireType.Varint); output.WriteBool(IsDeleted); }
        if (TripUpdate is not null) { output.WriteTag(3, WireFormat.WireType.LengthDelimited); output.WriteMessage(TripUpdate); }
        if (VehiclePosition is not null) { output.WriteTag(4, WireFormat.WireType.LengthDelimited); output.WriteMessage(VehiclePosition); }
    }

    public int CalculateSize()
    {
        int size = 0;
        if (Id.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(Id);
        if (IsDeleted) size += 2;
        if (TripUpdate is not null) size += 1 + CodedOutputStream.ComputeMessageSize(TripUpdate);
        if (VehiclePosition is not null) size += 1 + CodedOutputStream.ComputeMessageSize(VehiclePosition);
        return size;
    }

    public bool Equals(FeedEntity? other) => other is not null;
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
                if (other.Arrival is not null) { Arrival ??= new(); Arrival.MergeFrom(other.Arrival); }
                if (other.Departure is not null) { Departure ??= new(); Departure.MergeFrom(other.Departure); }
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
                if (Arrival is not null) { output.WriteTag(2, WireFormat.WireType.LengthDelimited); output.WriteMessage(Arrival); }
                if (Departure is not null) { output.WriteTag(3, WireFormat.WireType.LengthDelimited); output.WriteMessage(Departure); }
                if (StopId.Length > 0) { output.WriteTag(4, WireFormat.WireType.LengthDelimited); output.WriteString(StopId); }
            }

            public int CalculateSize()
            {
                int size = 0;
                if (StopSequence != 0) size += 1 + CodedOutputStream.ComputeUInt32Size(StopSequence);
                if (Arrival is not null) size += 1 + CodedOutputStream.ComputeMessageSize(Arrival);
                if (Departure is not null) size += 1 + CodedOutputStream.ComputeMessageSize(Departure);
                if (StopId.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(StopId);
                return size;
            }

            public bool Equals(StopTimeUpdate? other) => other is not null;
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
            public bool Equals(StopTimeEvent? other) => other is not null;
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

    public bool Equals(TripUpdate? other) => other is not null;
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

    public bool Equals(TripDescriptor? other) => other is not null;
}

// ---------------------------------------------------------------------------
// VehiclePosition enums
// ---------------------------------------------------------------------------
public enum VehicleStopStatus { IncomingAt = 0, StoppedAt = 1, InTransitTo = 2 }
public enum CongestionLevel { Unknown = 0, RunningSmoothly = 1, StopAndGo = 3, Congestion = 4, SevereCongestion = 5 }
public enum OccupancyStatus { NoData = 0, Empty = 1, ManySeatsAvailable = 2, FewSeatsAvailable = 3, StandingRoomOnly = 4, CrushedStandingRoomOnly = 5, Full = 6, NotAcceptingPassengers = 7 }

// ---------------------------------------------------------------------------
// VehiclePosition
// ---------------------------------------------------------------------------
public sealed class VehiclePosition : IMessage<VehiclePosition>
{
    public static readonly MessageParser<VehiclePosition> Parser = new(() => new VehiclePosition());
    public MessageDescriptor Descriptor => null!;

    public TripDescriptor? Trip { get; set; }
    public Position? Position { get; set; }
    public VehicleDescriptor? Vehicle { get; set; }
    public string CurrentStop { get; set; } = "";
    public ulong CurrentStopSequence { get; set; }
    public VehicleStopStatus CurrentStatus { get; set; } = VehicleStopStatus.InTransitTo;
    public ulong Timestamp { get; set; }
    public CongestionLevel CongestionLevel { get; set; } = CongestionLevel.Unknown;
    public OccupancyStatus OccupancyStatus { get; set; } = OccupancyStatus.NoData;

    public VehiclePosition Clone() => new()
    {
        Trip = Trip?.Clone(),
        Position = Position?.Clone(),
        Vehicle = Vehicle?.Clone(),
        CurrentStop = CurrentStop,
        CurrentStopSequence = CurrentStopSequence,
        CurrentStatus = CurrentStatus,
        Timestamp = Timestamp,
        CongestionLevel = CongestionLevel,
        OccupancyStatus = OccupancyStatus
    };

    public void MergeFrom(VehiclePosition other)
    {
        if (other.Trip is not null) { Trip ??= new TripDescriptor(); Trip.MergeFrom(other.Trip); }
        if (other.Position is not null) { Position ??= new Position(); Position.MergeFrom(other.Position); }
        if (other.Vehicle is not null) { Vehicle ??= new VehicleDescriptor(); Vehicle.MergeFrom(other.Vehicle); }
        if (other.CurrentStop.Length > 0) CurrentStop = other.CurrentStop;
        CurrentStopSequence = other.CurrentStopSequence;
        CurrentStatus = other.CurrentStatus;
        Timestamp = other.Timestamp;
        CongestionLevel = other.CongestionLevel;
        OccupancyStatus = other.OccupancyStatus;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: Trip = new TripDescriptor(); input.ReadMessage(Trip); break;
                case 18: Position = new Position(); input.ReadMessage(Position); break;
                case 26: Vehicle = new VehicleDescriptor(); input.ReadMessage(Vehicle); break;
                case 34: CurrentStop = input.ReadString(); break;
                case 40: CurrentStopSequence = input.ReadUInt64(); break;
                case 48: CurrentStatus = (VehicleStopStatus)input.ReadEnum(); break;
                case 56: Timestamp = input.ReadUInt64(); break;
                case 64: CongestionLevel = (CongestionLevel)input.ReadEnum(); break;
                case 72: OccupancyStatus = (OccupancyStatus)input.ReadEnum(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (Trip is not null) { output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteMessage(Trip); }
        if (Position is not null) { output.WriteTag(2, WireFormat.WireType.LengthDelimited); output.WriteMessage(Position); }
        if (Vehicle is not null) { output.WriteTag(3, WireFormat.WireType.LengthDelimited); output.WriteMessage(Vehicle); }
        if (CurrentStop.Length > 0) { output.WriteTag(4, WireFormat.WireType.LengthDelimited); output.WriteString(CurrentStop); }
        if (CurrentStopSequence != 0) { output.WriteTag(5, WireFormat.WireType.Varint); output.WriteUInt64(CurrentStopSequence); }
        if (CurrentStatus != VehicleStopStatus.InTransitTo) { output.WriteTag(6, WireFormat.WireType.Varint); output.WriteEnum((int)CurrentStatus); }
        if (Timestamp != 0) { output.WriteTag(7, WireFormat.WireType.Varint); output.WriteUInt64(Timestamp); }
        if (CongestionLevel != CongestionLevel.Unknown) { output.WriteTag(8, WireFormat.WireType.Varint); output.WriteEnum((int)CongestionLevel); }
        if (OccupancyStatus != OccupancyStatus.NoData) { output.WriteTag(9, WireFormat.WireType.Varint); output.WriteEnum((int)OccupancyStatus); }
    }

    public int CalculateSize()
    {
        int size = 0;
        if (Trip is not null) size += 1 + CodedOutputStream.ComputeMessageSize(Trip);
        if (Position is not null) size += 1 + CodedOutputStream.ComputeMessageSize(Position);
        if (Vehicle is not null) size += 1 + CodedOutputStream.ComputeMessageSize(Vehicle);
        if (CurrentStop.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(CurrentStop);
        if (CurrentStopSequence != 0) size += 1 + CodedOutputStream.ComputeUInt64Size(CurrentStopSequence);
        if (CurrentStatus != VehicleStopStatus.InTransitTo) size += 2;
        if (Timestamp != 0) size += 1 + CodedOutputStream.ComputeUInt64Size(Timestamp);
        if (CongestionLevel != CongestionLevel.Unknown) size += 2;
        if (OccupancyStatus != OccupancyStatus.NoData) size += 2;
        return size;
    }

    public bool Equals(VehiclePosition? other) => other is not null;
}

// ---------------------------------------------------------------------------
// Position
// ---------------------------------------------------------------------------
public sealed class Position : IMessage<Position>
{
    public static readonly MessageParser<Position> Parser = new(() => new Position());
    public MessageDescriptor Descriptor => null!;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Bearing { get; set; }
    public double Speed { get; set; }
    public double Odometer { get; set; }

    public Position Clone() => new() { Latitude = Latitude, Longitude = Longitude, Bearing = Bearing, Speed = Speed, Odometer = Odometer };

    public void MergeFrom(Position other)
    {
        Latitude = other.Latitude;
        Longitude = other.Longitude;
        Bearing = other.Bearing;
        Speed = other.Speed;
        Odometer = other.Odometer;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 9: Latitude = input.ReadDouble(); break;
                case 13: Latitude = input.ReadFloat(); break;
                case 17: Longitude = input.ReadDouble(); break;
                case 21: Longitude = input.ReadFloat(); break;
                case 29: Bearing = input.ReadFloat(); break;
                case 33: Odometer = input.ReadDouble(); break;
                case 45: Speed = input.ReadFloat(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (Latitude != 0) { output.WriteTag(1, WireFormat.WireType.Fixed64); output.WriteDouble(Latitude); }
        if (Longitude != 0) { output.WriteTag(2, WireFormat.WireType.Fixed64); output.WriteDouble(Longitude); }
        if (Bearing != 0) { output.WriteTag(3, WireFormat.WireType.Fixed32); output.WriteFloat((float)Bearing); }
        if (Odometer != 0) { output.WriteTag(4, WireFormat.WireType.Fixed64); output.WriteDouble(Odometer); }
        if (Speed != 0) { output.WriteTag(5, WireFormat.WireType.Fixed32); output.WriteFloat((float)Speed); }
    }

    public int CalculateSize()
    {
        int size = 0;
        if (Latitude != 0) size += 9;
        if (Longitude != 0) size += 9;
        if (Bearing != 0) size += 5;
        if (Odometer != 0) size += 9;
        if (Speed != 0) size += 5;
        return size;
    }

    public bool Equals(Position? other) => other is not null;
}

// ---------------------------------------------------------------------------
// VehicleDescriptor
// ---------------------------------------------------------------------------
public sealed class VehicleDescriptor : IMessage<VehicleDescriptor>
{
    public static readonly MessageParser<VehicleDescriptor> Parser = new(() => new VehicleDescriptor());
    public MessageDescriptor Descriptor => null!;

    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string LicensePlate { get; set; } = "";

    public VehicleDescriptor Clone() => new() { Id = Id, Label = Label, LicensePlate = LicensePlate };

    public void MergeFrom(VehicleDescriptor other)
    {
        if (other.Id.Length > 0) Id = other.Id;
        if (other.Label.Length > 0) Label = other.Label;
        if (other.LicensePlate.Length > 0) LicensePlate = other.LicensePlate;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: Id = input.ReadString(); break;
                case 18: Label = input.ReadString(); break;
                case 26: LicensePlate = input.ReadString(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (Id.Length > 0) { output.WriteTag(1, WireFormat.WireType.LengthDelimited); output.WriteString(Id); }
        if (Label.Length > 0) { output.WriteTag(2, WireFormat.WireType.LengthDelimited); output.WriteString(Label); }
        if (LicensePlate.Length > 0) { output.WriteTag(3, WireFormat.WireType.LengthDelimited); output.WriteString(LicensePlate); }
    }

    public int CalculateSize()
    {
        int size = 0;
        if (Id.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(Id);
        if (Label.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(Label);
        if (LicensePlate.Length > 0) size += 1 + CodedOutputStream.ComputeStringSize(LicensePlate);
        return size;
    }

    public bool Equals(VehicleDescriptor? other) => other is not null;
}
