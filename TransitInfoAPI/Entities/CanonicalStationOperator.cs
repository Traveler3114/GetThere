namespace TransitInfoAPI.Entities;

public class CanonicalStationOperator
{
    public int CanonicalStationId { get; set; }
    public CanonicalStation CanonicalStation { get; set; } = null!;

    public int OperatorId { get; set; }
    public Operator Operator { get; set; } = null!;

    public string? PlatformInfo { get; set; }
}
