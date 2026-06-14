using GetThereShared.Enums;

namespace GetThereAPI.Models;

public class TicketPayload
{
    public TicketFormat Format { get; set; }
    public string Data { get; set; } = string.Empty;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
