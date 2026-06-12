namespace GetThere.Localization;

public static class ApiMessageMapper
{
    private static readonly Dictionary<string, string> _map = new()
    {
        ["EMAIL_ALREADY_IN_USE"] = "Error_EmailAlreadyInUse",
        ["USER_REGISTERED"] = "Error_UserRegisteredSuccessfully",
        ["LOGGED_OUT"] = "Error_LoggedOut",
        ["INVALID_CREDENTIALS"] = "Error_InvalidCredentials",
        ["INVALID_REFRESH_TOKEN"] = "Error_InvalidRefreshToken",
        ["REFRESH_TOKEN_EXPIRED"] = "Error_RefreshTokenExpired",
        ["USER_NOT_AUTHENTICATED"] = "Error_UserNotAuthenticated",
        ["STOP_NOT_FOUND"] = "Error_LoadFailed",
    };

    public static string Localize(string? code, string? englishFallback)
    {
        if (code is not null && _map.TryGetValue(code, out var resxKey))
            return LocalizationService.Instance[resxKey];
        return englishFallback ?? string.Empty;
    }
}
