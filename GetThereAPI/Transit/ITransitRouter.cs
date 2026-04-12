namespace GetThereAPI.Transit;

public interface ITransitRouter
{
    Task<string> ResolveInstanceKeyAsync(int? countryId, CancellationToken ct = default);
}
