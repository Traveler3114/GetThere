# GetThere — Command Reference (build, run, database, ops)

Every command you need to build, run, migrate, and test the solution on a dev machine, in the order
you normally reach for them — **the single place to look**. For the two-platform architecture see
[`../../AGENTS.md`](../../AGENTS.md).

## The pieces and their ports

| Service | Purpose | Address | Database |
|---------|---------|---------|----------|
| **OTP** (OpenTripPlanner) | Door-to-door transit routing engine | `http://localhost:8080/otp/gtfs/v1` | — (graph on disk) |
| **TransitInfoAPI** | Map, stations, feeds, routing (`/plan` → OTP), export | `http://localhost:5000` / `https://localhost:5001` | `TransitInfoDB` |
| **GetThereAPI** | Users, wallets, ticketing, journeys | `https://localhost:7230` | `GetThereDB` |
| **GetThere** | MAUI client (Android / Windows) | — | — |

**Start order:** OTP → TransitInfoAPI → GetThereAPI → MAUI app. Each depends on the ones before it
(TransitInfoAPI's `/plan` calls OTP; the app calls both APIs).

---

## 1. OTP — the routing engine

OTP files live under `C:\otp\`:

| File | What it is |
|------|-----------|
| `C:\otp\otp.jar` | The OpenTripPlanner 2.x runnable jar |
| `C:\otp\zagreb\` | The graph input **directory** OTP is pointed at |
| `C:\otp\zagreb\croatia-*.osm.pbf` | OSM street network (Geofabrik Croatia extract) |
| `C:\otp\zagreb\gtfs.zip` | Merged, reconciled GTFS bundle — **written by TransitInfoAPI** (see §2) |
| `C:\otp\zagreb\station_*.json`, `system_information.json` | GBFS mobility feeds — also written by TransitInfoAPI |
| `C:\otp\zagreb\router-config.json` | OTP router config |
| `C:\otp\zagreb\graph.obj` | The built graph (produced by `--build`) |

OTP reads **everything in the directory it is pointed at**, so keep `C:\otp\zagreb` as the single
input folder.

### Serve (graph already built — the usual case)
```bash
java -Xmx6G -jar C:\otp\otp.jar --load C:\otp\zagreb
```
Serves the GraphQL endpoint at `http://localhost:8080/otp/gtfs/v1` — the URL
`Routing:Otp:Endpoint` points TransitInfoAPI's `/plan` at.

### Rebuild the graph (after the GTFS bundle or OSM extract changed)
```bash
java -Xmx6G -jar C:\otp\otp.jar --build --save C:\otp\zagreb
```
Writes a fresh `graph.obj` into the directory and exits; then run `--load` to serve it.

### Build and serve in one shot
```bash
java -Xmx6G -jar C:\otp\otp.jar --build --serve C:\otp\zagreb
```

> **When to rebuild:** whenever `gtfs.zip` or the `.osm.pbf` changes. TransitInfoAPI rewrites
> `gtfs.zip` and the GBFS JSONs into `C:\otp\zagreb` on startup and on every feed activation (§2), so
> after refreshing feeds, rebuild the OTP graph to pick them up. Bump `-Xmx` if the build runs out of
> memory — the Croatia-wide OSM extract is ~200 MB.

---

## 2. TransitInfoAPI — map & routing platform

Run with the **https** profile — the MAUI client loads the map page from here and the Android
manifest disallows cleartext:
```bash
dotnet run --project TransitInfoAPI/TransitInfoAPI.csproj --launch-profile https
```

On startup in Development it:
- applies EF migrations (`MigrateAsync()`),
- builds the routing bundle and **writes `gtfs.zip` + the GBFS JSONs into `C:\otp\zagreb`**
  (`Routing:Export:OutputDirectory`) — this is how OTP's input files get on disk.

So the refresh loop is: **run TransitInfoAPI → it writes the bundle → rebuild the OTP graph (§1).**

The bundle is also served over HTTP (consumed by OTP server-side, `[Authorize]`, or anonymous in Dev
via `Routing:AllowAnonymousExport`):
- `GET /routing/export/gtfs.zip`
- `GET /routing/gbfs/gbfs.json` (and `system_information` / `station_information` / `station_status`)
- `GET /routing/gtfs-rt`

---

## 3. GetThereAPI — business platform

Must be running before the MAUI app starts:
```bash
dotnet run --project GetThereAPI/GetThereAPI.csproj --launch-profile https
```
Serves `https://localhost:7230`. Owns accounts, wallets, ticketing, and the journey
quote/book/cancel endpoints the buy flow calls.

---

## 4. GetThere — the MAUI app

Needs the MAUI workload and a platform SDK.

### Android (build + deploy + run)
```bash
dotnet build GetThere/GetThere.csproj -t:Run -f net10.0-android
```
The Android emulator reaches the host APIs via `https://10.0.2.2:7230/` (GetThereAPI) and the map
base URL — **not** `localhost`.

### Windows (build + run)
```bash
dotnet build GetThere/GetThere.csproj -t:Run -f net10.0-windows10.0.19041.0
```

### Compile-only (no device — verify the code builds)
```bash
dotnet build GetThere/GetThere.csproj -f net10.0-windows10.0.19041.0
```

---

## 5. Build & test

### Build a single project
```bash
dotnet build GetThereAPI/GetThereAPI.csproj
dotnet build GetThereShared/GetThereShared.csproj
```

### Run the whole test suite
```bash
dotnet test tests/GetThere.Tests/GetThere.Tests.csproj
```

### Run a subset by name
```bash
dotnet test tests/GetThere.Tests/GetThere.Tests.csproj --filter "FullyQualifiedName~Journey"
```

> The `tests/GetThere.Tests` project targets plain `net10.0`, so it can't reference the MAUI app's
> platform TFMs. It pulls in individual MAUI-app service files by `<Compile Include>` (e.g.
> `JourneyService.cs`) and tests them through a stub `HttpMessageHandler`. To verify the MAUI UI
> compiles, use the compile-only build in §4.

---

## 6. Database & EF Core migrations

Two databases, each owned by its API:

| Database | Owner | Connection string |
|----------|-------|-------------------|
| **GetThereDB** | GetThereAPI | `Server=localhost;Database=GetThereDB;Trusted_Connection=True;TrustServerCertificate=True` |
| **TransitInfoDB** | TransitInfoAPI | `Server=localhost;Database=TransitInfoDB;Trusted_Connection=True;TrustServerCertificate=True` |

Install the EF tool once:
```bash
dotnet tool install --global dotnet-ef
```

### Add & apply a migration
**Stop the API first** (a running API holds the DB and the build lock). Then, from the API's project
folder:
```bash
cd GetThereAPI          # or TransitInfoAPI
dotnet ef migrations add <DescriptiveName>
dotnet ef database update
```
Or without `cd`, using `--project`:
```bash
dotnet ef migrations add <DescriptiveName> --project GetThereAPI/GetThereAPI.csproj
dotnet ef database update --project GetThereAPI/GetThereAPI.csproj
```

> **`database update` is optional for TransitInfoAPI** — it runs `MigrateAsync()` on startup, so
> restarting it applies pending migrations. GetThereAPI does **not** auto-migrate; run
> `database update` explicitly. Running it explicitly is fine (and recommended) either way.

Rules: use a **unique, descriptive** name every time (`AddedWalletTable`, not `Update1`); always run
**both** commands (`add` writes the file, `update` applies it); never hand-edit `*ModelSnapshot.cs`
(EF regenerates it).

### Database operations (occasional)

Drop & recreate a dev database from scratch (destroys all data):
```bash
dotnet ef database drop --force --project GetThereAPI/GetThereAPI.csproj
dotnet ef database update --project GetThereAPI/GetThereAPI.csproj
```
Why/when → [`../database-drift.md`](../database-drift.md).

Regenerate the SQL baseline (e.g. after squashing migrations):
```bash
dotnet ef migrations script
```
Why/when → [`../transitinfodb-rebaseline.md`](../transitinfodb-rebaseline.md).

Set the JWT signing key outside source (never in `appsettings.json`):
```bash
dotnet user-secrets set "Jwt:Key" "<key>"
```
Why/when → [`../secrets-rotation.md`](../secrets-rotation.md).

---

## Quick start (cold machine, everything already built)

```bash
# 1. Routing engine
java -Xmx6G -jar C:\otp\otp.jar --load C:\otp\zagreb

# 2. Map platform (also rewrites the OTP bundle to C:\otp\zagreb on startup)
dotnet run --project TransitInfoAPI/TransitInfoAPI.csproj --launch-profile https

# 3. Business platform
dotnet run --project GetThereAPI/GetThereAPI.csproj --launch-profile https

# 4. App
dotnet build GetThere/GetThere.csproj -t:Run -f net10.0-android
```
