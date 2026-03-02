using System.ComponentModel.DataAnnotations.Schema;

namespace GetThereAPI.Entities
{
    public class Wallet
    {
        public int Id { get; set; }

        [Column(TypeName = "decimal(16,2)")]
        public decimal Balance { get; set; } = 0;

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public string UserId { get; set; } = string.Empty;
        public AppUser User { get; set; } = null!;
    }
}
