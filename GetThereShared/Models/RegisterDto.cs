using System.ComponentModel.DataAnnotations;

namespace GetThereAPI.Models
{
    public class RegisterDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

        public string? FullName { get; set; }
    }
}