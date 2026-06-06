using GetThereAPI.Entities;
using GetThereShared.Contracts;

namespace GetThereAPI.Mapping;

public static class AuthMapper
{
    public static UserResponse ToResponse(AppUser user, string token) => new()
    {
        Id = user.Id,
        Email = user.Email ?? string.Empty,
        FullName = user.FullName,
        Token = token,
    };
}
