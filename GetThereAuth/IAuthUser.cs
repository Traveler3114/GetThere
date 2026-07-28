namespace GetThereAuth;

/// <summary>
/// The one thing the shared auth code needs from a user beyond what <c>IdentityUser</c> already
/// provides. Both APIs' <c>AppUser</c> types implement this, which is what lets the token and claims
/// code below be written once without moving the entity types out of their own projects.
/// </summary>
public interface IAuthUser
{
    string FullName { get; set; }
}
