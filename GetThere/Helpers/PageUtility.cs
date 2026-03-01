namespace GetThere.Helpers;

public static class PageUtility
{
    // UI
    public static void ShowError(Label label, string message)
    {
        label.Text = message;
        label.IsVisible = true;
    }

    public static void HideError(Label label) =>
        label.IsVisible = false;

    public static void SetBusy(ActivityIndicator indicator, Button button, bool isBusy)
    {
        indicator.IsVisible = isBusy;
        indicator.IsRunning = isBusy;
        button.IsEnabled = !isBusy;
    }

    // Validation
    public static bool IsValidEmail(string email)
    {
        try { return new System.Net.Mail.MailAddress(email).Address == email; }
        catch { return false; }
    }

    public static bool IsValidPhone(string phone) =>
        phone.Length >= 10 && phone.All(char.IsDigit);

    // Formatting
    public static string FormatPrice(decimal amount, string currency = "€") =>
        $"{currency}{amount:F2}";

    public static string FormatDateTime(DateTime dt) =>
        dt.ToString("dd MMM yyyy, HH:mm");
}