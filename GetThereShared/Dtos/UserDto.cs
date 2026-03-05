namespace GetThereShared.Dtos
{
    // DTO = Data Transfer Object
    // This is what gets sent over the network between API and MAUI
    public class UserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? Token { get; set; }  // <-- the JWT travels in here
    }
}