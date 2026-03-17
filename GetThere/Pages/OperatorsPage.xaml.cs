using GetThereShared.Dtos;
using GetThere.Services;

namespace GetThere.Pages;

public partial class OperatorsPage : ContentPage
{
    private readonly OperatorService _operatorService;
    private List<OperatorDto> _allOperators = [];
    private string _filter = "All";

    public OperatorsPage(OperatorService operatorService)
    {
        InitializeComponent();
        _operatorService = operatorService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadOperatorsAsync();
    }

    private async Task LoadOperatorsAsync()
    {
        BusyIndicator.IsVisible = BusyIndicator.IsRunning = true;
        try
        {
            var ops = await _operatorService.GetOperatorsAsync();
            if (ops != null)
            {
                _allOperators = ops;
                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            BusyIndicator.IsVisible = BusyIndicator.IsRunning = false;
        }
    }

    private void ApplyFilter(string? search = null)
    {
        var q = search ?? SearchBar.Text ?? string.Empty;
        IEnumerable<OperatorDto> list = _allOperators;

        if (!string.IsNullOrWhiteSpace(q))
            list = list.Where(o =>
                o.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (o.City ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                o.Country.Contains(q, StringComparison.OrdinalIgnoreCase));

        OperatorList.ItemsSource = list.Select(o => new
        {
            o.Id,
            o.Name,
            o.City,
            o.Country,
            IsInstalled = false,
            SizeText = "",
        }).ToList();

        UpdateFilterButtons();
    }

    private void UpdateFilterButtons()
    {
        bool isDark = Application.Current!.RequestedTheme == AppTheme.Dark;
        var active = Color.FromArgb(isDark ? "#2C2C2E" : "#EBEBEC");
        var activeText = isDark ? Colors.White : Colors.Black;
        var inactive = Colors.Transparent;
        var inactiveText = isDark ? Color.FromArgb("#AAAAAA") : Colors.Gray;

        void UpdateBtn(Button btn, bool isActive)
        {
            btn.Background = null;
            btn.BackgroundColor = isActive ? active : inactive;
            btn.TextColor = isActive ? activeText : inactiveText;
        }

        if (AllBtn != null) UpdateBtn(AllBtn, _filter == "All");
        if (InstalledBtn != null) UpdateBtn(InstalledBtn, _filter == "Installed");
        if (NotInstalledBtn != null) UpdateBtn(NotInstalledBtn, _filter == "Not Installed");
    }

    private void Filter_Clicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        _filter = btn.Text;
        ApplyFilter();
    }

    private void SearchBar_TextChanged(object? sender, TextChangedEventArgs e)
        => ApplyFilter(e.NewTextValue);

    private async void InstallRemove_Clicked(object? sender, EventArgs e)
    {
        await DisplayAlert("Info", "GTFS Service missing. Reconstructing...", "OK");
    }
}
