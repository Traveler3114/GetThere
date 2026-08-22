using System.ComponentModel.DataAnnotations;

namespace TransitInfoAPI.Contracts;

/// <summary>An alert scraper and the selectors that read it.</summary>
public class AlertSourceResponse
{
    public int Id { get; set; }
    public int FeedId { get; set; }
    public string FeedSlug { get; set; } = string.Empty;
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public string SourceKey { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? ItemSelector { get; set; }
    public string? TitleSelector { get; set; }
    public string? DescriptionSelector { get; set; }
    public string? DateSelector { get; set; }
    public string? LinkSelector { get; set; }
    public string? CategorySelector { get; set; }
    public int IntervalMinutes { get; set; }

    public DateTime? LastRunAt { get; set; }
    public int? LastItemCount { get; set; }
    public string? LastError { get; set; }
}

public record CreateAlertSourceRequest
{
    [Range(1, int.MaxValue)] public int OperatorId { get; set; }
    [Required, StringLength(64)] public string SourceKey { get; set; } = string.Empty;
    [Required, StringLength(32)] public string Kind { get; set; } = "Transit";
    [Required, StringLength(32)] public string Format { get; set; } = "Html";
    [Required, StringLength(1024)] public string Url { get; set; } = string.Empty;
    [StringLength(400)] public string? ItemSelector { get; set; }
    [StringLength(400)] public string? TitleSelector { get; set; }
    [StringLength(400)] public string? DescriptionSelector { get; set; }
    [StringLength(400)] public string? DateSelector { get; set; }
    [StringLength(400)] public string? LinkSelector { get; set; }
    [StringLength(400)] public string? CategorySelector { get; set; }
    [Range(1, 1440)] public int IntervalMinutes { get; set; } = 15;
}

public record UpdateAlertSourceRequest
{
    [Required, StringLength(32)] public string Kind { get; set; } = "Transit";
    [Required, StringLength(32)] public string Format { get; set; } = "Html";
    [Required, StringLength(1024)] public string Url { get; set; } = string.Empty;
    [StringLength(400)] public string? ItemSelector { get; set; }
    [StringLength(400)] public string? TitleSelector { get; set; }
    [StringLength(400)] public string? DescriptionSelector { get; set; }
    [StringLength(400)] public string? DateSelector { get; set; }
    [StringLength(400)] public string? LinkSelector { get; set; }
    [StringLength(400)] public string? CategorySelector { get; set; }
    [Range(1, 1440)] public int IntervalMinutes { get; set; } = 15;
    public bool IsActive { get; set; } = true;
}

/// <summary>What the selectors currently extract, without writing any alerts.</summary>
public class AlertSourcePreviewResponse
{
    public int ItemCount { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<AlertPreviewItem> Items { get; set; } = [];
}

public class AlertPreviewItem
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Link { get; set; }
    public string? Date { get; set; }
    public string? Category { get; set; }
}
