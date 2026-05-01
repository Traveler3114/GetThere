using System.Globalization;
using System.Resources;

namespace GetThere.Localization;

public sealed class LocalizationService
{
    private static readonly Lazy<LocalizationService> _instance =
        new(() => new LocalizationService());

    public static LocalizationService Instance => _instance.Value;

    private static readonly ResourceManager _resourceManager =
        new("GetThere.Resources.Strings.AppResources", typeof(LocalizationService).Assembly);

    private static readonly string[] _supportedLanguages = ["en", "hr"];

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentUICulture;

    public event EventHandler? CultureChanged;

    private LocalizationService() { }

    public string this[string key]
    {
        get
        {
            try
            {
                var value = _resourceManager.GetString(key, CurrentCulture);
                return value ?? key;
            }
            catch
            {
                return key;
            }
        }
    }

    public void SetCulture(CultureInfo culture)
    {
        CurrentCulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Preferences.Default.Set("app_language", culture.TwoLetterISOLanguageName);
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    public static void Initialize()
    {
        var saved = Preferences.Default.Get("app_language", string.Empty);
        var lang = string.IsNullOrEmpty(saved)
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : saved;

        if (!_supportedLanguages.Contains(lang))
            lang = "en";

        var culture = lang == "hr"
            ? new CultureInfo("hr-HR")
            : new CultureInfo("en-US");

        Instance.SetCulture(culture);
    }
}
