namespace GetThereShared.Contracts;

public class RoleDto
{
    public string Name { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = [];
}

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
    public bool IsActive { get; set; }
}
