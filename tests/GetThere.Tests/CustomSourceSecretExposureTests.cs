using TransitInfoAPI.Contracts;
using TransitInfoAPI.Entities;
using TransitInfoAPI.Mapping;

namespace GetThere.Tests;

/// <summary>
/// <c>CustomSource.AuthConfig</c> holds an operator's live credential — a bearer token, a basic-auth
/// password, or an arbitrary API-key header, whichever <c>CustomSourceEngine.ApplyAuth</c> is asked
/// to send. It was copied verbatim into <c>CustomSourceResponse</c>, so <c>GET /custom-sources</c>
/// handed every credential to anyone holding <c>customsources.view</c>.
/// <para>
/// The response carries <c>HasAuth</c> now. These guard the shape rather than any one call site,
/// because the way this comes back is someone adding the property to the DTO again for the editor's
/// convenience — which is exactly why it was there the first time.
/// </para>
/// </summary>
public class CustomSourceSecretExposureTests
{
    [Fact]
    public void The_response_exposes_no_property_that_could_carry_the_credential()
    {
        var offending = typeof(CustomSourceResponse)
            .GetProperties()
            .Where(p => p.Name.Contains("Auth", StringComparison.OrdinalIgnoreCase)
                     && p.PropertyType != typeof(bool))
            .Select(p => $"{p.PropertyType.Name} {p.Name}")
            .ToList();

        Assert.True(
            offending.Count == 0,
            "CustomSourceResponse must not carry the credential. Found: " + string.Join(", ", offending));
    }

    [Fact]
    public void The_mapper_reports_presence_without_the_value()
    {
        var withSecret = new CustomSource
        {
            Id = 1,
            Name = "Test source",
            AuthConfig = """{"type":"bearer","token":"super-secret-value"}""",
            Requests = []
        };

        var response = CustomSourceMapper.ToResponse(withSecret);

        Assert.True(response.HasAuth);

        // Belt and braces: no string anywhere on the response may contain the secret, whatever
        // future property someone adds.
        var strings = typeof(CustomSourceResponse)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.GetValue(response) as string)
            .Where(v => v is not null);

        Assert.DoesNotContain(strings, v => v!.Contains("super-secret-value", StringComparison.Ordinal));
    }

    [Fact]
    public void No_credential_means_HasAuth_is_false()
    {
        var response = CustomSourceMapper.ToResponse(new CustomSource
        {
            Id = 2,
            Name = "No auth",
            AuthConfig = null,
            Requests = []
        });

        Assert.False(response.HasAuth);
    }

    /// <summary>
    /// Blank is not a credential. Treating whitespace as "configured" would show the editor a
    /// credential to preserve that does not exist, and leaving it blank would then keep nothing.
    /// </summary>
    [Fact]
    public void A_blank_credential_does_not_count_as_present()
    {
        var response = CustomSourceMapper.ToResponse(new CustomSource
        {
            Id = 3,
            Name = "Blank auth",
            AuthConfig = "   ",
            Requests = []
        });

        Assert.False(response.HasAuth);
    }
}
