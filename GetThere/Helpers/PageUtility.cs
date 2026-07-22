using System.Globalization;

using GetThereShared.Enums;

namespace GetThere.Helpers;

// ═══════════════════════════════════════════════════════════
//  UI helpers — used in code-behind across all pages
// ═══════════════════════════════════════════════════════════
public static class PageUtility
{
    public const double DefaultResponsiveRatio = 0.80;
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
        if (button is not null)
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

public class TxTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WalletTransactionType type ? type switch
        {
            WalletTransactionType.Deposit => "💳",
            WalletTransactionType.TicketPurchase => "🎫",
            WalletTransactionType.Refund => "↩️",
            _ => "💰"
        } : "💰";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class TxTypeToAmountColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WalletTransactionType type && type == WalletTransactionType.TicketPurchase
            ? Color.FromArgb("#F44336")
            : Color.FromArgb("#4CAF50");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class BoolToStrokeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? 2.0 : 0.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

public class CountryBgConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
            return parameter is string hex ? Color.FromArgb(hex) : Color.FromArgb("#134E4A");
        return Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
