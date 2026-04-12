#nullable enable
using GetThere.Helpers;
using GetThereShared.Dtos;

namespace GetThere.Pages;

/// <summary>
/// Displays a mock ticket confirmation after a successful purchase.
/// Shows the operator name, ticket type, validity window, price,
/// and a placeholder QR code visual.
/// Always displays a prominent "MOCK TICKET — NOT VALID FOR TRAVEL" banner.
/// </summary>
public partial class MockTicketConfirmationPage : ContentPage
{
    public MockTicketConfirmationPage(MockTicketResultDto ticket)
    {
        InitializeComponent();
        SizeChanged += OnPageSizeChanged;
        BindTicket(ticket);
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        PageUtility.ApplyTicketsStyleResponsive(Width, ConfirmationContent);
    }

    private void BindTicket(MockTicketResultDto ticket)
    {
        OperatorLabel.Text   = ticket.OperatorName;
        TicketNameLabel.Text = ticket.TicketName;
        PriceLabel.Text      = $"€{ticket.Price:F2}";
        TicketIdLabel.Text   = ticket.TicketId;
        ValidFromLabel.Text  = ParseAndFormatLocal(ticket.ValidFrom);
        ValidUntilLabel.Text = ParseAndFormatLocal(ticket.ValidUntil);
    }

    /// <summary>Parses an ISO 8601 datetime string and formats it as local time.</summary>
    private static string ParseAndFormatLocal(string? iso)
        => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToLocalTime().ToString("dd MMM HH:mm")
            : iso ?? string.Empty;

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//shop");
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        // Pop back to shop root (two pages: TicketPurchasePage + this)
        await Navigation.PopToRootAsync();
    }
}
