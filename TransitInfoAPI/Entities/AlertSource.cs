using System.ComponentModel.DataAnnotations;

namespace TransitInfoAPI.Entities;

/// <summary>
/// One operator's disruption page and the selectors that read it.
/// <para>
/// These lived in <c>appsettings.json</c> under <c>Alerts:Sources</c>, which meant a drifted CSS
/// selector — the common failure, not a rare one — needed a redeploy to fix. They are rows now, so
/// the admin console can edit and preview them.
/// </para>
/// </summary>
public class AlertSource
{
    public int Id { get; set; }

    /// <summary>Was <c>AlertSourceOptions.Id</c>, e.g. "hzpp-info". Prefixes <c>Alert.SourceKey</c>.</summary>
    [MaxLength(64)]
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>Transit | Road. Road alerts (HAK) skip route matching.</summary>
    [MaxLength(32)]
    public string Kind { get; set; } = "Transit";

    /// <summary>Html | GeoJson.</summary>
    [MaxLength(32)]
    public string Format { get; set; } = "Html";

    /// <summary>Semicolon-separated list; jadrolinija-notices already relies on this.</summary>
    [MaxLength(1024)]
    public string Url { get; set; } = string.Empty;

    [MaxLength(400)] public string? ItemSelector { get; set; }
    [MaxLength(400)] public string? TitleSelector { get; set; }
    [MaxLength(400)] public string? DescriptionSelector { get; set; }
    [MaxLength(400)] public string? DateSelector { get; set; }
    [MaxLength(400)] public string? LinkSelector { get; set; }
    [MaxLength(400)] public string? CategorySelector { get; set; }

    public int IntervalMinutes { get; set; } = 15;

    // Run history — the thing configuration could never give us. A source that silently stops
    // returning rows is the failure mode worth seeing.
    public DateTime? LastRunAt { get; set; }
    public int? LastItemCount { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Feed> Feeds { get; set; } = [];
}
