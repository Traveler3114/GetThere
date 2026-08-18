namespace TransitInfoAPI.Routing.Export;

/// <summary>
/// Per-feed-version tally of how stop times resolved during an export. A source whose stop
/// references stop resolving should be <b>visible</b> — a bundle that quietly sheds a source's trips
/// looks healthy right up until a user gets no itinerary. So the exporter records, per version, how
/// many stop times resolved through each arm and how many were dropped, keeping a bounded sample of
/// drop reasons for diagnosis without unbounded logging.
/// </summary>
public sealed class ResolutionReport
{
    private const int MaxDropSamplesPerVersion = 20;

    private readonly Dictionary<int, VersionResolution> _byVersion = [];

    public IReadOnlyDictionary<int, VersionResolution> ByFeedVersion => _byVersion;

    public void Record(int feedVersionId, ResolvedStop resolved)
    {
        if (!_byVersion.TryGetValue(feedVersionId, out var v))
        {
            v = new VersionResolution();
            _byVersion[feedVersionId] = v;
        }

        switch (resolved.Kind)
        {
            case ExportedStopKind.RawStop:
                v.ResolvedViaRawStop++;
                break;
            case ExportedStopKind.CanonicalStation:
                v.ResolvedViaCanonicalStation++;
                break;
            case ExportedStopKind.Dropped:
                v.Dropped++;
                if (v.DropReasonSamples.Count < MaxDropSamplesPerVersion && resolved.DropReason is not null)
                    v.DropReasonSamples.Add(resolved.DropReason);
                break;
        }
    }

    /// <summary>Total stop times dropped across every version — the export's headline defect count.</summary>
    public int TotalDropped => _byVersion.Values.Sum(v => v.Dropped);

    /// <summary>True if any stop time was dropped, i.e. the bundle is missing timetable it was fed.</summary>
    public bool AnyDropped => TotalDropped > 0;
}

/// <summary>One feed version's resolution counts within a <see cref="ResolutionReport"/>.</summary>
public sealed class VersionResolution
{
    public int ResolvedViaRawStop { get; set; }
    public int ResolvedViaCanonicalStation { get; set; }
    public int Dropped { get; set; }
    public List<string> DropReasonSamples { get; } = [];

    public int TotalResolved => ResolvedViaRawStop + ResolvedViaCanonicalStation;
}
