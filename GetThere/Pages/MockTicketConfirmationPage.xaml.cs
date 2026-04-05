#nullable enable
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
        BindTicket(ticket);
    }

    private void BindTicket(MockTicketResultDto ticket)
    {
        OperatorLabel.Text   = ticket.OperatorName;
        TicketNameLabel.Text = ticket.TicketName;
        PriceLabel.Text      = $"€{ticket.Price:F2}";
        TicketIdLabel.Text   = ticket.TicketId;

        if (DateTime.TryParse(ticket.ValidFrom, null, System.Globalization.DateTimeStyles.RoundtripKind, out var from))
            ValidFromLabel.Text = from.ToLocalTime().ToString("dd MMM HH:mm");
        else
            ValidFromLabel.Text = ticket.ValidFrom;

        if (DateTime.TryParse(ticket.ValidUntil, null, System.Globalization.DateTimeStyles.RoundtripKind, out var until))
            ValidUntilLabel.Text = until.ToLocalTime().ToString("dd MMM HH:mm");
        else
            ValidUntilLabel.Text = ticket.ValidUntil;
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        // Pop back to shop root (two pages: TicketPurchasePage + this)
        await Navigation.PopToRootAsync();
    }
}
