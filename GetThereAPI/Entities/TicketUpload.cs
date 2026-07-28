using GetThereShared.Enums;

namespace GetThereAPI.Entities;

/// <summary>
/// A file the user uploaded but has not yet turned into a ticket.
/// <para>
/// This row is what makes it safe to let a client name a stored file on create. The blob key is
/// server-minted and recorded here against its owner, so <c>POST /importedtickets</c> can verify
/// that the caller uploaded this exact file and has not already spent it, rather than accepting an
/// arbitrary string as a storage path.
/// </para>
/// </summary>
public class TicketUpload
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    /// <summary>Server-minted handle to the stored file. Unique across users.</summary>
    public string BlobKey { get; set; } = string.Empty;

    /// <summary>The sniffed type, not the declared one.</summary>
    public TicketFileType FileType { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Set when an imported ticket claims this upload. A consumed key cannot be reused.</summary>
    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
