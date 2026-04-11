using GetThere.Components;
using GetThereShared.Enums;
using System.Globalization;

namespace GetThere.Helpers;

// ═══════════════════════════════════════════════════════════
//  UI helpers — used in code-behind across all pages
// ═══════════════════════════════════════════════════════════
public static class PageUtility
{
    public const double DefaultResponsiveRatio = 0.60;
    public const double DefaultResponsiveMinWidth = 340;
    public const double MobileBreakpoint = 700;

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

    public static void ApplyResponsiveWidth(double pageWidth, View element, double ratio = DefaultResponsiveRatio, double minWidth = DefaultResponsiveMinWidth)
    {
        if (pageWidth <= 0 || element is null)
            return;

        element.WidthRequest = Math.Max(minWidth, pageWidth * ratio);
    }

    public static bool ApplyTicketsStyleResponsive(double pageWidth, View element, double ratio = DefaultResponsiveRatio, double minWidth = DefaultResponsiveMinWidth)
    {
        if (pageWidth <= 0 || element is null)
            return false;

        var isMobile = pageWidth < MobileBreakpoint;

        if (isMobile)
        {
            element.WidthRequest = -1;
            element.HorizontalOptions = LayoutOptions.Fill;
            return true;
        }

        element.WidthRequest = Math.Max(minWidth, pageWidth * ratio);
        element.HorizontalOptions = LayoutOptions.Center;
        return false;
    }
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


/// <summary>
/// Converts a provider Name string to an emoji icon.
/// Emoji never stored in DB — mapped client-side from name.
/// </summary>
public class ProviderIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name ? name.ToLower() switch
        {
            var n when n.Contains("visa") || n.Contains("mastercard") || n.Contains("card") => "💳",
            var n when n.Contains("paypal") => "🅿️",
            var n when n.Contains("apple") => "🍎",
            var n when n.Contains("google") => "🔵",
            var n when n.Contains("stripe") => "⚡",
            _ => "💰"
        } : "💰";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}


/// <summary>
/// Converts a WalletTransactionType to an amount text color:
/// TopUp/Refund = green, TicketPurchase = red.
/// </summary>
public class TxTypeToAmountColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WalletTransactionType type && type == WalletTransactionType.TicketPurchase
            ? Color.FromArgb("#F44336")
            : Color.FromArgb("#4CAF50");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}


public class InstallBtnTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Remove" : "Install";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}


public class InstallBtnColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Color.FromArgb("#F44336") : Color.FromArgb("#4CAF50");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
