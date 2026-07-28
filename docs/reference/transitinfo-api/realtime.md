# TransitInfoAPI — Realtime and Mobility

Three background workers keep data fresh, each with a different cadence because the data ages at
different rates.

| Worker | Default interval | Initial delay | Feeds | Storage |
|---|---|---|---|---|
| `FeedPollingWorker` | 60 **minutes** | — | GTFS static | Database |
| `RealtimePollingWorker` | 30 **seconds** | 10 s | GTFS-RT | **In-memory only** |
| `MobilityPollingWorker` | 120 **seconds** | 15 s | GBFS | Database |

The startup delays on the two live workers are deliberate: they let migrations and seeding finish
before the first poll, so a cold start does not race the schema.

---

## Why realtime data lives in memory

`RealtimeManager` is a **singleton** holding `ConcurrentDictionary` caches for vehicle positions and
trip updates. Nothing is persisted.

```csharp
// In-memory only — does not survive restart. Acceptable: high churn, low value after restart.
// Revisit for Phase 2 multi-instance deployment.
```

The reasoning is sound for what this data is. A vehicle position is worthless 30 seconds later, and
persisting positions for hundreds of vehicles every 30 seconds is a continuous write load producing
rows nobody will ever read. Losing the cache on restart costs one poll cycle.

Two real consequences follow, and both are flagged in the code:

1. **A restart blanks realtime data** for up to one polling interval.
2. **This does not work multi-instance.** Each instance would hold its own cache and poll upstream
   independently, so clients would see different vehicles depending on which instance answered.
   The same single-instance assumption appears in `FeedManager`'s static semaphore dictionary.

Alerts are the exception — they *are* persisted to the `Alerts` table, because a service disruption
notice stays relevant for hours or days, unlike a position.

---

## GTFS-RT polling

GTFS-Realtime is a **Protocol Buffers** feed, not JSON. The `.proto` is compiled by `Grpc.Tools` at
build time (`<Protobuf Include="gtfs-realtime.proto" GrpcServices="None" />` — message types only, no
gRPC service).

`PollAllFeedsAsync` fetches every active `GTFSRealtime` feed with `MaxDegreeOfParallelism = 3`, the
same cap as static feed polling.

### Per-feed caching, and the bug it fixes

```csharp
private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, TripUpdateBundle>> _tripUpdatesByFeed;
```

Trip updates are held **per feed** and only then flattened into the lookup used for queries. The
comment records why:

> the cache used to be rebuilt from this cycle's successes alone, so a single transient failure
> blanked that operator's realtime view until the next successful poll.

Keeping last-known-good data per feed means one operator's flaky endpoint degrades only that
operator's data, and only after it goes stale — not the whole realtime view for one cycle.

### Vehicle staleness

Vehicles are keyed `{feedId}:{vehicleId}` — feed-scoped, because vehicle IDs are only unique within a
feed and two operators can both run a vehicle `1`.

Each cycle evicts entries older than `VehicleStaleCutoffMinutes` (default 5). Without eviction, a
vehicle that stops reporting — end of shift, lost telemetry — would sit on the map forever. Five
minutes at a 30-second cadence tolerates ten missed polls before a vehicle disappears.

### Failure handling

Same shape as static feeds: consecutive failures counted per feed, auto-deactivation after
`MaxConsecutiveFailuresBeforeDeactivate` (default 10), counter reset on any success. At 30-second
polling that is only five minutes of failure, so a realtime feed is deactivated far more readily than
a static one — appropriate, since a realtime endpoint that is down for five minutes is genuinely
broken rather than briefly slow.

Cancellation is excluded from the failure count (`when (!innerCt.IsCancellationRequested)`), so a
shutdown does not deactivate every feed on the way out.

---

## Delay propagation

The interesting logic is turning sparse trip updates into a delay for a *specific* departure.

A GTFS-RT `TripUpdate` does not carry an entry for every stop. It typically gives a few, and consumers
are expected to propagate. `RealtimeManager` resolves a delay in order of confidence:

1. **Exact match** — an update for this stop sequence, or for this stop id.
2. **Propagated** — the nearest preceding stop's delay applies to this one.
3. **No data** — the departure is returned scheduled-only.

Two lookups exist per trip (`BySequence` and `ByStopId`) because feeds are inconsistent about which
they populate; matching on either is what makes this work across operators.

The estimated time is always `scheduledDeparture + delay` rather than any absolute timestamp the feed
supplies, which keeps schedule and realtime on one clock.

`DepartureResponse.IsRealtime` tells the client which of these happened, so the UI can distinguish a
live estimate from a timetable.

---

## Departures: `ScheduleManager`

`GetDeparturesAsync` is the most timezone-sensitive code in the system, because **GTFS times are local
to the agency and can exceed 24 hours** — `25:30:00` means 1:30 am the following service day, which is
how a service that runs past midnight stays attached to the right day.

The flow:

1. Convert the requested UTC instant into `Schedule:Timezone` (default `Europe/Zagreb`).
2. Derive the local service date and seconds-since-midnight.
3. Compute valid `service_id`s for **today and tomorrow** from `Calendar` (weekday flags plus date
   range) and `CalendarDate` (explicit additions and exceptions).
4. Query `StopTimes` for the canonical station, restricted to the **active** feed version, with
   `DepartureTime >= fromTime`.
5. Overlay realtime delays.

Tomorrow is included because a query at 23:50 must return departures after midnight — those belong to
tomorrow's service and would otherwise be missed entirely.

`StopTime.DepartureTime` is stored as an **integer of seconds since local midnight**, not a time type.
That is what allows values past 86400, and it is why the covering index
`(CanonicalStationId, DepartureTime) INCLUDE (TripId)` makes this query fast.

### The timezone limitation

One configured timezone is applied to all feeds. GTFS specifies the timezone per *agency*, so a feed
outside `Europe/Zagreb` — ÖBB's Austrian services, for instance — has its departure times converted
with the wrong zone. Correct for the current deployment, wrong the moment feeds span zones. The fix is
to read `Agency.Timezone`, which is already imported and stored but not used here.

---

## GBFS and mobility

GBFS (General Bikeshare Feed Specification) is a JSON standard for docked bike and scooter share.
Unlike GTFS-RT it **is** persisted, to `MobilityStations`, because a dock's location is stable and
only its availability count moves.

`MobilityPollingWorker` polls every 120 seconds — slower than vehicles because a dock's count changes
on the timescale of someone walking up to it.

`MobilityStation` carries `AvailableVehicles`, `Capacity` and `LastUpdated`, and is linked to both an
`Operator` and a `Country` so mobility can be filtered geographically alongside transit.

Mobility uses the shared `RouteType` values `Bicycle = 100` and `Scooter = 101` — outside GTFS's own
numbering, which is precisely why the enums are stored as strings.

---

## Alerts

Service alerts from GTFS-RT are written to the `Alerts` table with `HeaderText`, `DescriptionText`,
`Cause`, `Effect`, an active period, and affected entities.

The affected-entity fields are **delimited strings, not relations**:

```
AffectedStopIds, AffectedRouteIds, AffectedTripIds, AffectedAgencyIds
```

A join table would be the normalised choice. Strings were chosen because alerts are short-lived,
rewritten wholesale on each poll, and only ever filtered by a single id at a time — so the write cost
of maintaining join rows would exceed any read benefit. The trade is that filtering is a `LIKE`, which
does not use an index and cannot be trusted for exact-token matching without care.

---

## The three workers side by side

What is worth internalising is that each worker's design follows from **how fast its data goes
stale**:

| | Static | Realtime | Mobility |
|---|---|---|---|
| Ages in | weeks | seconds | minutes |
| Persisted | yes, versioned | **no** | yes, overwritten |
| Import cost | minutes | milliseconds | seconds |
| Parallelism | 3 | 3 | — |
| Deactivate after | 10 failures ≈ 10 h | 10 failures ≈ 5 min | 10 failures ≈ 20 min |
| Restart impact | none | data gone for one cycle | none |

Every one of those differences is a consequence of the first row.
