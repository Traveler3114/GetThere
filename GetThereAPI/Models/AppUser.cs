using Microsoft.AspNetCore.Identity;

namespace GetThereAPI.Models
{
    public class AppUser : IdentityUser
    {
        public string? FullName { get; set; }
        public string? City { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}