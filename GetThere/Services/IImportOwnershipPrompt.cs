namespace GetThere.Services;

/// <summary>
/// Asks the person at the device what to do with queued imports that are not theirs. Behind an
/// interface so <see cref="ImportSyncService"/> stays testable and free of UI types.
/// </summary>
public interface IImportOwnershipPrompt
{
    /// <returns><c>true</c> to add the entries to the signed-in account, <c>false</c> to leave them on the device.</returns>
    Task<bool> AskAdoptAsync(int count);
}
