using GetThereShared.Dtos;
using GetThere.Services;

namespace GetThere.Pages;

public partial class OperatorsPage : ContentPage
{
    private readonly OperatorService _operatorService;
    private readonly GtfsService _gtfsService;
    private List<TransitOperatorDto> _allOperators = [];
    private string _filter = "All";

    public OperatorsPage(OperatorService operatorService, GtfsService gtfsService)
    {
        InitializeComponent();
        _operatorService = operatorService;
        _gtfsService = gtfsService;
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
            _allOperators = await _operatorService.GetAllAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            BusyIndicator.IsVisible = BusyIndicator.IsRunning = false;
        }
    }

    private void ApplyFilter(string? search = null)
    {
        var q = search ?? SearchBar.Text ?? string.Empty;
        IEnumerable<TransitOperatorDto> list = _allOperators;

        if (_filter == "Installed")
            list = list.Where(o => _gtfsService.IsInstalled(o.Id));
        else if (_filter == "Not Installed")
            list = list.Where(o => !_gtfsService.IsInstalled(o.Id));

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
            IsInstalled = _gtfsService.IsInstalled(o.Id),
            SizeText = FormatSize(_gtfsService.GetSizeBytes(o.Id)),
        }).ToList();

        UpdateFilterButtons();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return string.Empty;
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    private void UpdateFilterButtons()
    {
        var active = Color.FromArgb("#4CAF50");
        var inactive = Color.FromArgb("#E0E0E0");
        var activeText = Colors.White;
        var inactiveText = Color.FromArgb("#333333");

        AllBtn.BackgroundColor = _filter == "All" ? active : inactive;
        InstalledBtn.BackgroundColor = _filter == "Installed" ? active : inactive;
        NotInstalledBtn.BackgroundColor = _filter == "Not Installed" ? active : inactive;

        AllBtn.TextColor = _filter == "All" ? activeText : inactiveText;
        InstalledBtn.TextColor = _filter == "Installed" ? activeText : inactiveText;
        NotInstalledBtn.TextColor = _filter == "Not Installed" ? activeText : inactiveText;
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
        if (sender is not Button btn || btn.CommandParameter == null)
            return;

        var opId = (int)btn.CommandParameter;
        var realOp = _allOperators.FirstOrDefault(o => o.Id == opId);
        if (realOp == null) return;

        if (_gtfsService.IsInstalled(opId))
        {
            var confirm = await DisplayAlertAsync("Remove", $"Remove {realOp.Name}?", "Remove", "Cancel");
            if (!confirm) return;
            _gtfsService.Remove(opId);
        }
        else
        {
            var progress = new Progress<double>(p => btn.Text = $"{p:P0}");
            btn.IsEnabled = false;
            try
            {
                await _gtfsService.InstallAsync(realOp, progress);
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
        ApplyFilter();
    }
}