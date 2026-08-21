namespace TransitInfoAPI.Services;

public sealed class AlertSourceOptions
{
    public string Id { get; set; } = string.Empty;
    public int? OperatorId { get; set; }
    public string? OperatorSlug { get; set; }
    public string Kind { get; set; } = "Transit";
    public string Format { get; set; } = "Html";
    public string Url { get; set; } = string.Empty;
    public string? ItemSelector { get; set; }
    public AlertSourceFieldOptions Fields { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 15;
}

public sealed class AlertSourceFieldOptions
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Date { get; set; }
    public string? Link { get; set; }
    public string? Category { get; set; }
}

public sealed class AlertsOptions
{
    public const string SectionName = "Alerts";
    public List<AlertSourceOptions> Sources { get; set; } = [];
}
