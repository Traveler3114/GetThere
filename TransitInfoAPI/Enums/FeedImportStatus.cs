namespace TransitInfoAPI.Enums;

public enum FeedImportStatus
{
    Pending,
    Importing,
    Success,
    Failed,
    Skipped,

    /// <summary>
    /// The GTFS data committed, but station reconciliation did not finish.
    /// <para>
    /// Reconciliation runs after the import transaction commits, so a failure there cannot roll the
    /// import back. The version's trips, stops and stop times are present and correct; what is
    /// missing is the link from its raw stops to canonical stations, which is what departures and the
    /// map resolve through. That is a repairable state, not a broken one — re-run it with
    /// <c>POST /feeds/versions/{id}/reconcile</c>.
    /// </para>
    /// <para>
    /// It is deliberately distinct from both Success and Failed: calling it Success would hide a
    /// version whose stops resolve to nothing, and calling it Failed would suggest the imported data
    /// should be thrown away.
    /// </para>
    /// </summary>
    ReconciliationPending
}
