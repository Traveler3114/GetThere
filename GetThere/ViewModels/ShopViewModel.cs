using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using GetThere.Localization;
using GetThere.Services;

using GetThereShared.Common;
using GetThereShared.Contracts;

namespace GetThere.ViewModels;

/// <summary>
/// One row in the Shop's operator directory: an adapter, plus everything the row needs to
/// render without reaching back into the option list.
/// </summary>
public partial class ShopOperator : ObservableObject
{
    public int AdapterId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string AdapterType { get; init; } = string.Empty;

    /// <summary>Two-or-three letter badge, e.g. ZET / HŽ / NB.</summary>
    public string Monogram { get; init; } = string.Empty;

    /// <summary>"Tram, bus · 4 fares from €0.66" — what the operator sells and the cheapest fare.</summary>
    public string Subtitle { get; init; } = string.Empty;

    public Color MonogramBackground { get; init; } = Colors.Transparent;
    public Color MonogramForeground { get; init; } = Colors.White;

    /// <summary>
    /// False when the operator has no sellable options — the design still lists them, dimmed,
    /// as "Timetable only — no ticketing yet".
    /// <para>
    /// <b>It is never false.</b> <see cref="ShopViewModel.BuildDirectory"/> builds the directory by
    /// grouping the option list itself, and a LINQ group always has at least one element — so
    /// <c>items.Count > 0</c> holds for every row that can exist. An operator with nothing to sell
    /// produces no group and is simply absent from the screen, which is the opposite of the design
    /// this property describes.
    /// </para>
    /// <para>
    /// Everything downstream of it is therefore unreachable: <see cref="RowOpacity"/> is always
    /// <c>1.0</c>, the muted monogram palette never applies, <c>Shop_NoOperators</c> can never be
    /// rendered, and <c>OpenOperator</c>'s <c>!IsBuyable</c> guard never fires.
    /// </para>
    /// <para>
    /// Not fixed here because the fix is not in this class: listing a ticketless operator needs the
    /// directory built from an operator list rather than from a fare list, and
    /// <c>GET /tickets/options</c> returns only fares. Whether the Shop should show operators it
    /// cannot sell for is a product decision with an API change behind it.
    /// </para>
    /// </summary>
    public bool IsBuyable { get; init; }

    public double RowOpacity => IsBuyable ? 1d : 0.72d;

    public IReadOnlyList<TicketOptionResponse> Options { get; init; } = [];
}

public partial class ShopViewModel : BaseViewModel
{
    // Design palette for the operator monograms: teal for the local operators, violet for the
    // long-distance ones. Matches frames 3b/3g of "GetThere App.dc.html".
    private static readonly Color TealSurface = Color.FromArgb("#134E4A");
    private static readonly Color TealText = Color.FromArgb("#5EEAD4");
    private static readonly Color VioletSurface = Color.FromArgb("#1C1740");
    private static readonly Color VioletText = Color.FromArgb("#C4B5FD");
    private static readonly Color MutedSurface = Color.FromArgb("#1F2937");
    private static readonly Color MutedText = Color.FromArgb("#94A3B8");

    private readonly AuthService _authService;
    private readonly TicketService _ticketService;

    private List<ShopOperator> _allNearYou = [];
    private List<ShopOperator> _allLongDistance = [];

    /// <summary>Shown instead of the directory when the user is a guest or signed out.</summary>
    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _descriptionText = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _hasNearYou;

    [ObservableProperty]
    private bool _hasLongDistance;

    /// <summary>True once a load has finished and left nothing to show — drives the empty state.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    public ObservableCollection<ShopOperator> NearYou { get; } = [];
    public ObservableCollection<ShopOperator> LongDistance { get; } = [];

    public ShopViewModel(AuthService authService, TicketService ticketService)
    {
        _authService = authService;
        _ticketService = ticketService;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task Load()
    {
        var token = await _authService.GetTokenAsync();
        var signedIn = !string.IsNullOrWhiteSpace(token) && !AuthService.IsGuest();
        IsAuthenticated = signedIn;

        if (!signedIn)
        {
            TitleText = LocalizationService.Instance["Shop_AccountRequired"];
            DescriptionText = LocalizationService.Instance["Shop_AccountRequiredDesc"];
            NearYou.Clear();
            LongDistance.Clear();
            HasNearYou = HasLongDistance = IsEmpty = false;
            return;
        }

        TitleText = LocalizationService.Instance["Shop_Title"];
        DescriptionText = LocalizationService.Instance["Shop_Subtitle"];

        IsBusy = true;
        HasError = false;
        try
        {
            var result = await _ticketService.GetTicketOptionsAsync();
            if (!result.Success)
            {
                ErrorText = result.Message ?? LocalizationService.Instance["Error_CouldNotLoadOperators"];
                HasError = true;
                return;
            }

            BuildDirectory(result.Data ?? []);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            HasError = true;
        }
        finally
        {
            // After IsBusy clears, so the empty state can distinguish "nothing here" from "loading".
            IsBusy = false;
            ApplyFilter();
        }
    }

    /// <summary>
    /// Turns a flat option list into the design's two sections.
    /// <para>
    /// "Near you" versus "long distance" is not a field the API carries, so it is derived from
    /// what the operator actually sells: an operator whose fares are all time-boxed
    /// (<see cref="TicketOptionResponse.DurationMinutes"/> set) is selling urban passes that
    /// start when you board, which is exactly the local group in the design. Anything with an
    /// open-ended fare is a journey ticket and belongs under long distance.
    /// </para>
    /// </summary>
    private void BuildDirectory(IReadOnlyList<TicketOptionResponse> options)
    {
        var near = new List<ShopOperator>();
        var far = new List<ShopOperator>();

        foreach (var group in options.GroupBy(o => o.AdapterId))
        {
            var items = group.OrderBy(o => o.Price).ToList();
            var name = items[0].AdapterName;
            var isLocal = items.All(o => o.DurationMinutes is > 0);

            var op = new ShopOperator
            {
                AdapterId = group.Key,
                Name = name,
                AdapterType = items[0].AdapterType,
                Monogram = BuildMonogram(name),
                Subtitle = BuildSubtitle(items),
                MonogramBackground = items.Count == 0 ? MutedSurface : isLocal ? TealSurface : VioletSurface,
                MonogramForeground = items.Count == 0 ? MutedText : isLocal ? TealText : VioletText,
                IsBuyable = items.Count > 0,
                Options = items
            };

            (isLocal ? near : far).Add(op);
        }

        _allNearYou = [.. near.OrderBy(o => o.Name)];
        _allLongDistance = [.. far.OrderBy(o => o.Name)];
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim() ?? string.Empty;

        Replace(NearYou, Match(_allNearYou, query));
        Replace(LongDistance, Match(_allLongDistance, query));

        HasNearYou = NearYou.Count > 0;
        HasLongDistance = LongDistance.Count > 0;
        IsEmpty = !IsBusy && !HasNearYou && !HasLongDistance;
    }

    private static IEnumerable<ShopOperator> Match(IEnumerable<ShopOperator> source, string query) =>
        string.IsNullOrEmpty(query)
            ? source
            : source.Where(o =>
                o.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                o.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase));

    private static void Replace(ObservableCollection<ShopOperator> target, IEnumerable<ShopOperator> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    /// <summary>Initials of the operator name, capped at three characters (ZET, HŽ, NB, FB).</summary>
    private static string BuildMonogram(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "··";

        var words = name.Split([' ', '-', '·'], StringSplitOptions.RemoveEmptyEntries);

        // A single all-caps word is already an abbreviation — keep it as-is (ZET, HŽPP → ZET, HŽPP).
        if (words.Length == 1)
            return words[0].Length <= 3 ? words[0].ToUpperInvariant() : words[0][..3].ToUpperInvariant();

        var initials = string.Concat(words.Take(3).Select(w => char.ToUpperInvariant(w[0])));
        return initials;
    }

    private static string BuildSubtitle(List<TicketOptionResponse> options)
    {
        if (options.Count == 0)
            return LocalizationService.Instance["Shop_NoOperators"];

        var cheapest = options[0];
        var price = MoneyFormatter.Format(cheapest.Price, cheapest.Currency);

        return options.Count == 1
            ? $"{cheapest.Name} · {price}"
            : $"{options.Count} fares · from {price}";
    }

    [RelayCommand]
    private static async Task OpenOperator(ShopOperator? op)
    {
        if (op is null || !op.IsBuyable)
            return;

        await Shell.Current.GoToAsync($"ticketpurchase?adapterId={op.AdapterId}");
    }

    [RelayCommand]
    private static void GoToLogin() => App.GoToLogin();
}
