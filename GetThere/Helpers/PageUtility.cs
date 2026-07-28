using System.Globalization;

using GetThereShared.Common;
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

/// <summary>
/// Formats a ticket's price in the currency that ticket was actually saved in.
/// <para>
/// The ticket list previously formatted every price with a hardcoded "EUR" suffix while each
/// ticket carries its own currency and the import form offers a picker, so a ticket saved in GBP
/// was displayed to the user as euros. Takes price and currency as a multi-binding because neither
/// alone is enough to render the value, and defers to <see cref="MoneyFormatter"/> so wallet
/// balances and ticket prices format identically.
/// </para>
/// </summary>
public class PriceCurrencyConverter : IMultiValueConverter
{
    public object? Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Length: >= 1 }) return string.Empty;

        // A ticket with no price shows nothing rather than "0.00" — free and unrecorded are not
        // the same thing, and the import form leaves price optional.
        if (values[0] is not decimal price) return string.Empty;

        var currency = values.Length > 1 ? values[1] as string : null;
        return MoneyFormatter.Format(price, currency, culture);
    }

    public object?[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Prices are display-only.");
}

/// <summary>
/// Ticket status → badge colour, per the imported design system. Pass "surface" as the
/// converter parameter for the pill background, anything else for the label colour.
/// </summary>
public class TicketStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var surface = string.Equals(parameter as string, "surface", StringComparison.OrdinalIgnoreCase);

        var hex = value?.ToString() switch
        {
            "Active" => surface ? "#064E3B" : "#10B981",
            "Used" => surface ? "#374151" : "#D1D5DB",
            "Expired" => surface ? "#3B1214" : "#F87171",
            "Cancelled" => surface ? "#3B2A08" : "#FBBF24",
            _ => surface ? "#374151" : "#D1D5DB"
        };

        return Color.FromArgb(hex);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Spent tickets are dimmed rather than hidden — the design keeps Used and Expired rows in the
/// list at reduced opacity so the history stays visible without competing with live tickets.
/// </summary>
public class TicketStatusOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() is "Used" or "Expired" or "Cancelled" ? 0.7d : 1d;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Filter-chip styling. The bound value is the active filter key; the parameter is
/// "&lt;key&gt;:&lt;part&gt;" where part is bg, stroke or text — for example "Active:bg".
/// </summary>
public class FilterChipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? ":text").Split(':');
        var key = parts[0];
        var part = parts.Length > 1 ? parts[1] : "text";
        var isSelected = string.Equals(value?.ToString() ?? string.Empty, key, StringComparison.OrdinalIgnoreCase);

        return part switch
        {
            "bg" => isSelected ? Color.FromArgb("#134E4A") : Colors.Transparent,
            "stroke" => isSelected ? Color.FromArgb("#10B981") : Color.FromArgb("#4B5563"),
            _ => isSelected ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#D1D5DB")
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Chip styling driven by a plain <c>bool</c> selected flag, for chip lists that own their own
/// state (the map's transport modes) rather than comparing against one active key — see
/// <see cref="FilterChipConverter"/> for the single-selection case. Pass "bg", "stroke" or "text".
/// </summary>
public class ChipPartConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSelected = value is bool b && b;

        return (parameter as string) switch
        {
            "bg" => isSelected ? Color.FromArgb("#134E4A") : Colors.Transparent,
            "stroke" => isSelected ? Color.FromArgb("#10B981") : Color.FromArgb("#4B5563"),
            _ => isSelected ? Color.FromArgb("#5EEAD4") : Color.FromArgb("#D1D5DB")
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// True when the bound string has content — for optional lines such as the adapter slug, which
/// the design only prints when the integration actually reports one.
/// </summary>
public class NotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Selected-card outline. The design marks the chosen fare and the chosen journey with a
/// 1.5px emerald edge and leaves everything else on the default hairline; this returns the
/// stroke colour for that, so the selected state is one binding rather than two.
/// </summary>
public class SelectedStrokeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool selected && selected
            ? Color.FromArgb("#10B981")
            : Color.FromArgb(parameter as string ?? "#1F2937");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
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
