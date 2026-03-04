using GetThereShared.Enums;
using System.Globalization;

namespace GetThere.Helpers;

// ═══════════════════════════════════════════════════════════
//  UI helpers — used in code-behind across all pages
// ═══════════════════════════════════════════════════════════
public static class PageUtility
{
    // ── Error label ────────────────────────────────────────
    public static void ShowError(Label label, string message)
    {
        label.Text = message;
        label.IsVisible = true;
    }

    public static void HideError(Label label) =>
        label.IsVisible = false;

    // ── Activity indicator + button lock ───────────────────
    public static void SetBusy(ActivityIndicator indicator, Button? button, bool isBusy)
    {
        indicator.IsVisible = isBusy;
        indicator.IsRunning = isBusy;
        if (button != null)
            button.IsEnabled = !isBusy;
    }

    // ── Validation ─────────────────────────────────────────
    public static bool IsValidEmail(string email)
    {
        try { return new System.Net.Mail.MailAddress(email).Address == email; }
        catch { return false; }
    }

    public static bool IsValidPhone(string phone) =>
        phone.Length >= 10 && phone.All(char.IsDigit);

    // ── Formatting ─────────────────────────────────────────
    public static string FormatPrice(decimal amount, string currency = "€") =>
        $"{currency}{amount:F2}";

    public static string FormatDateTime(DateTime dt) =>
        dt.ToString("dd MMM yyyy, HH:mm");
}

// ═══════════════════════════════════════════════════════════
//  XAML value converters — used in data bindings
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Converts a TicketStatus enum to a background Color for the status badge.
/// Active=green, Expired=grey, Used=blue, Cancelled=red.
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TicketStatus status ? status switch
        {
            TicketStatus.Active => Color.FromArgb("#4CAF50"), // green
            TicketStatus.Expired => Color.FromArgb("#9E9E9E"), // grey
            TicketStatus.Used => Color.FromArgb("#2196F3"), // blue
            TicketStatus.Cancelled => Color.FromArgb("#F44336"), // red
            _ => Color.FromArgb("#9E9E9E")
        } : Color.FromArgb("#9E9E9E");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// Converts a WalletTransactionType enum to an emoji icon string for the history list.
/// TopUp=💳, TicketPurchase=🎫, Refund=↩️
/// </summary>
public class TxTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WalletTransactionType type ? type switch
        {
            WalletTransactionType.TopUp => "💳",
            WalletTransactionType.TicketPurchase => "🎫",
            WalletTransactionType.Refund => "↩️",
            _ => "💰"
        } : "💰";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}