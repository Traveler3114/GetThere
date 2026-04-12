namespace GetThereAPI.Transit;

public class OtpOptions
{
    public string DefaultInstance { get; set; } = "eu";
    public Dictionary<string, OtpInstanceOptions> Instances { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, string> CountryInstanceMap { get; set; } = [];
}

public class OtpInstanceOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string GraphQlPath { get; set; } = "/otp/routers/default/index/graphql";
}
