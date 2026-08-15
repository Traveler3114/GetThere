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

    /// <summary>
    /// Raised after <see cref="SetCulture"/>. <b>Nothing subscribes to it.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists because the problem it solves is real, and it is worth stating that the problem is
    /// therefore still there. Every string in the app resolves <em>once</em>:
    /// <see cref="TranslateExtension"/> is a markup extension whose <c>ProvideValue</c> returns a
    /// plain <c>string</c> at XAML parse time rather than a binding, so a page keeps whatever
    /// language it was constructed in; and <c>AppShell</c> reads its tab titles once in
    /// <c>BuildNavigation</c>, from a constructor that runs once because the shell is registered
    /// <c>AddSingleton</c>.
    /// </para>
    /// <para>
    /// So switching language in Profile → Language does not change the app's language. The switch
    /// calls <c>App.GoToApp()</c> straight after this, evidently meaning to rebuild the UI — but
    /// that resolves the same singleton <c>AppShell</c> instance it is already showing, so the tab
    /// bar and the visible page stay as they were. Pages are transient, so navigating somewhere new
    /// afterwards does pick the new culture up, which is what makes this look intermittent rather
    /// than broken.
    /// </para>
    /// <para>
    /// Fixing it is a design choice rather than a line: subscribe here and rebuild the shell,
    /// register <c>AppShell</c> as transient so <c>GoToApp</c> constructs a fresh one, or make
    /// <see cref="TranslateExtension"/> return a binding that tracks this event. The last is the
    /// only one that also updates a page already on screen.
    /// </para>
    /// </remarks>
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

    public string GetString(string key) => this[key];

    public void SetCulture(CultureInfo culture)
    {
        CurrentCulture = culture;

        // Setting only the calling thread leaves every other thread — and every continuation that
        // resumes on the thread pool — on the old culture, so a language change appears to half-apply.
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
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
