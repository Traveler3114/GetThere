namespace GetThere.State;

/// <summary>
/// Persists the user's selected country across sessions using MAUI Preferences.
/// For guests the preference is stored in local device storage.
/// For logged-in users the same store is used; server-side persistence can be
/// added later without changing this interface.
/// </summary>
public class CountryPreferenceService
{
    private const string Key     = "selectedCountryId";
    private const string NameKey = "selectedCountryName";

    /// <summary>
    /// Returns the stored country ID, or -1 if no country has been selected yet.
    /// </summary>
    public int GetSelectedCountryId()
        => Preferences.Default.Get(Key, -1);

    /// <summary>Returns the stored country name, or an empty string if none selected.</summary>
    public string GetSelectedCountryName()
        => Preferences.Default.Get(NameKey, string.Empty);

    /// <summary>Returns true when a country has already been chosen by the user.</summary>
    public bool HasSelection => GetSelectedCountryId() != -1;

    /// <summary>Persists the selected country ID and name.</summary>
    public void SetSelectedCountry(int id, string name)
    {
        Preferences.Default.Set(Key, id);
        Preferences.Default.Set(NameKey, name);
    }

    /// <summary>Clears the stored country preference.</summary>
    public void Clear()
    {
        Preferences.Default.Remove(Key);
        Preferences.Default.Remove(NameKey);
    }
}
