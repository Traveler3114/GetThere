namespace GetThere.Services;

public sealed class ImportOwnershipPrompt : IImportOwnershipPrompt
{
    public Task<bool> AskAdoptAsync(int count) =>
        Application.Current!.Windows[0].Page!.DisplayAlertAsync(
            "Tickets from before you signed in",
            $"{count} ticket(s) were saved on this device before you signed in. Add them to this account?",
            "Add",
            "Keep on device");
}
