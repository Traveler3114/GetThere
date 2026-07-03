# Shape Editor & Feed Import Fixes

## Bug 1: Shape editor map empty

### 1a — `initDraw` never called (map.once('idle') never fires)
**File:** `TransitInfoAPI/wwwroot/admin/shape-editor.html`  
**Lines:** 304-313  

Replace this block:
```javascript
      if (shapeData && shapeData.geometry && shapeData.geometry.type === 'LineString') {
        originalGeometry = JSON.parse(JSON.stringify(shapeData.geometry));

        map.once('idle', () => {
          initDraw(shapeData.geometry);
          fitMapToShape(shapeData.geometry);
        });

        document.getElementById('save-btn').disabled = false;
        shapeLoaded = true;
      }
```

With:
```javascript
      if (shapeData && shapeData.geometry && shapeData.geometry.type === 'LineString') {
        originalGeometry = JSON.parse(JSON.stringify(shapeData.geometry));
        initDraw(shapeData.geometry);
        fitMapToShape(shapeData.geometry);
        document.getElementById('save-btn').disabled = false;
        shapeLoaded = true;
      } else {
        showToast('Shape data has no valid LineString geometry', 'error');
      }
```

### 1b — Silent 404 when geometry is null
**File:** `TransitInfoAPI/wwwroot/admin/shape-editor.html`  
**After** the closing `}` of the `if (shapeResp.ok)` block at line 315, add:
```javascript
    } else {
      const errMsg = shapeResp.status === 404
        ? 'No shape generated yet for this route'
        : 'Failed to load shape (HTTP ' + shapeResp.status + ')';
      showToast(errMsg, 'error');
    }
```

The full `if (shapeResp.ok)` block becomes:
```javascript
    if (shapeResp.ok) {
      const shapeData = await shapeResp.json();
      if (shapeData && shapeData.geometry && shapeData.geometry.type === 'LineString') {
        originalGeometry = JSON.parse(JSON.stringify(shapeData.geometry));
        initDraw(shapeData.geometry);
        fitMapToShape(shapeData.geometry);
        document.getElementById('save-btn').disabled = false;
        shapeLoaded = true;
      } else {
        showToast('Shape data has no valid LineString geometry', 'error');
      }
    } else {
      const errMsg = shapeResp.status === 404
        ? 'No shape generated yet for this route'
        : 'Failed to load shape (HTTP ' + shapeResp.status + ')';
      showToast(errMsg, 'error');
    }
```

---

## Bug 2: Feed import flaky + slow

### 2a — Move reconciliation & place matching out of SQL transaction
**File:** `TransitInfoAPI/Managers/FeedManager.cs`

**Step 1:** Remove lines 388-391 (currently inside the SQL transaction's inner `try`):
```csharp
                    // DELETE these 2 lines:
                    await ReconcileAndBackfillAsync(feedVersionId, version, ct);
                    await MatchPlacesAsync(ct);
```

**Step 2:** After the outer `catch (Exception ex)` block closes at line 404 `}`, insert the post-transaction calls (at 16-space indent, before the lock's `try` closes at line 405 `}`):
```csharp
            // Reconciliation and place matching run outside the import transaction.
            // They don't need transactional atomicity with GTFS data — worst case a
            // station stays Pending and gets picked up next import. This keeps the
            // transaction window short and avoids command timeouts on large feeds.
            try
            {
                await ReconcileAndBackfillAsync(feedVersionId, version, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation failed after successful import for FeedVersion {VersionId}", feedVersionId);
                _logStore.AddEntry(feedVersionId, $"Reconciliation failed (non-fatal): {ex.Message}");
            }
            try
            {
                await MatchPlacesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Place matching failed after successful import for FeedVersion {VersionId}", feedVersionId);
                _logStore.AddEntry(feedVersionId, $"Place matching failed (non-fatal): {ex.Message}");
            }
```

### 2b — Spatial index for place lookups
**File:** `TransitInfoAPI/Managers/PlaceMatchingManager.cs`

**Step 1:** Add a grid field and constant alongside `_placeCache`:
```csharp
    private Dictionary<string, List<Place>>? _placeGrid;
    private const double GridCellSizeDeg = 0.5;
```

**Step 2:** Replace the `LoadPlacesAsync` + `FindNearestPlace` methods with:
```csharp
    public async Task LoadPlacesAsync(CancellationToken ct)
    {
        if (_placeCache is not null) return;
        _placeCache = await _db.Places.ToListAsync(ct);
        BuildPlaceGrid();
        _logger.LogInformation("Loaded {Count} places into cache ({CellCount} grid cells)", _placeCache.Count, _placeGrid?.Count);
    }

    private void BuildPlaceGrid()
    {
        _placeGrid = new Dictionary<string, List<Place>>();
        foreach (var place in _placeCache)
        {
            var key = GetGridCellKey(place.Lat, place.Lon);
            if (!_placeGrid.TryGetValue(key, out var list))
                _placeGrid[key] = list = [];
            list.Add(place);
        }
    }

    private static string GetGridCellKey(double lat, double lon)
    {
        var cellLat = Math.Round(lat / GridCellSizeDeg) * GridCellSizeDeg;
        var cellLon = Math.Round(lon / GridCellSizeDeg) * GridCellSizeDeg;
        return $"{cellLat:F1}:{cellLon:F1}";
    }

    public Place? FindNearestPlace(double lat, double lon)
    {
        if (_placeCache is null || _placeCache.Count == 0) return null;
        if (_placeGrid is null) BuildPlaceGrid();

        Place? nearest = null;
        var minDist = double.MaxValue;

        var centerKey = GetGridCellKey(lat, lon);
        var parts = centerKey.Split(':');
        var centerLat = double.Parse(parts[0]);
        var centerLon = double.Parse(parts[1]);

        for (var dLat = -1; dLat <= 1; dLat++)
        {
            for (var dLon = -1; dLon <= 1; dLon++)
            {
                var key = $"{(centerLat + dLat * GridCellSizeDeg):F1}:{(centerLon + dLon * GridCellSizeDeg):F1}";
                if (_placeGrid.TryGetValue(key, out var places))
                {
                    foreach (var place in places)
                    {
                        var dist = GeoUtils.CalculateDistanceMeters(lat, lon, place.Lat, place.Lon);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearest = place;
                        }
                    }
                }
            }
        }

        return minDist < _maxDistanceMeters ? nearest : null;
    }
```

### 2c — Spatial pre-bucketing in reconciliation
**File:** `TransitInfoAPI/Managers/ReconciliationManager.cs`

**Step 1:** Add a private helper method:
```csharp
    private static string GetSpatialCellKey(double lat, double lon)
    {
        var cellX = (int)Math.Floor(lat / 0.2);
        var cellY = (int)Math.Floor(lon / 0.2);
        return $"{cellX}:{cellY}";
    }
```

**Step 2:** After the `existingStations` filter at line 192 (after `existingLinkedStationIds` filter), build a spatial index:
```csharp
        existingStations = existingStations.Where(s => !existingLinkedStationIds.Contains(s.Id)).ToList();

        // Build spatial index for candidate lookup
        var stationGrid = new Dictionary<string, List<CanonicalStation>>();
        foreach (var station in existingStations)
        {
            var key = GetSpatialCellKey(station.Latitude, station.Longitude);
            if (!stationGrid.TryGetValue(key, out var list))
                stationGrid[key] = list = [];
            list.Add(station);
        }
```

**Step 3:** On line 210-213, replace the `FindBestMatch` call with a cell-filtered lookup:
```csharp
            var cellKey = GetSpatialCellKey(rawStop.Lat, rawStop.Lon);
            var nearbyStations = stationGrid.TryGetValue(cellKey, out var bucket) ? bucket : [];

            var match = FindBestMatch(
                rawStop.Name, rawStop.Lat, rawStop.Lon, rawStop.RouteType!.Value,
                rawStop.RawStopId, nearbyStations, autoDistThreshold * 2,
                routeLookup, stationToRawStopIds);
```

### 2d — Parallelize FeedPollingWorker
**File:** `TransitInfoAPI/Workers/FeedPollingWorker.cs`

Replace the `foreach (var feed in staticFeeds)` loop (lines 69-120) with:
```csharp
        await Parallel.ForEachAsync(staticFeeds, new ParallelOptions
        {
            MaxDegreeOfParallelism = 3,
            CancellationToken = ct
        }, async (feed, innerCt) =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var feedManager = scope.ServiceProvider.GetRequiredService<FeedManager>();
                var newVersion = await feedManager.CheckAndFetchAsync(feed.Id, innerCt);
                if (newVersion != null)
                {
                    if (newVersion.ImportStatus == FeedImportStatus.Success)
                    {
                        _consecutiveFailures.TryRemove(feed.Id, out _);
                        _logger.LogDebug("Feed {FeedId} already up to date, skipping", feed.FeedId);
                        return;
                    }
                    _logger.LogInformation(
                        "New feed version detected for {FeedId}: {Sha1}, starting import",
                        feed.FeedId, newVersion.Sha1);
                    await feedManager.ImportFeedVersionAsync(newVersion.Id, innerCt);
                }
                _consecutiveFailures.TryRemove(feed.Id, out _);
            }
            catch (Exception ex)
            {
                var count = _consecutiveFailures.AddOrUpdate(feed.Id, 1, (_, c) => c + 1);
                _logger.LogWarning(ex, "Failed to poll/import feed {FeedId} ({FailCount} consecutive failures)", feed.FeedId, count);

                var threshold = _options.CurrentValue.MaxConsecutiveFailuresBeforeDeactivate;
                if (count >= threshold)
                {
                    _logger.LogWarning(
                        "Auto-deactivating feed {FeedId} after {Count} consecutive failures",
                        feed.FeedId, count);
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<Data.TransitDbContext>();
                        var dbFeed = await db.Feeds.FindAsync([feed.Id], innerCt);
                        if (dbFeed is not null)
                        {
                            dbFeed.IsActive = false;
                            await db.SaveChangesAsync(innerCt);
                        }
                    }
                    catch (Exception inner)
                    {
                        _logger.LogError(inner, "Failed to deactivate feed {FeedId}", feed.FeedId);
                    }
                    _consecutiveFailures.TryRemove(feed.Id, out _);
                }
            }
        });
```
