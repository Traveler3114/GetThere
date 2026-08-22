using System.Reflection;

using GetThereAPI.Common;
using GetThereAPI.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GetThere.Tests;

/// <summary>
/// Guards the endpoint/permission matrix.
/// <para>
/// Regression origin: <c>GET /admin/stats</c> and <c>GET /admin/purchases</c> shipped gated on
/// <c>tickets.view</c>, which the User role holds — so any registered account could read every
/// user's purchases (with their email) and the platform's wallet float. These tests fail if an
/// admin-console endpoint is ever gated on a permission ordinary users are granted.
/// </para>
/// </summary>
public class AuthorizationMatrixTests
{
    private static IEnumerable<MethodInfo> ActionsOf<TController>() =>
        typeof(TController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    private static IEnumerable<string> PoliciesOn(MethodInfo action) =>
        action.GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrEmpty(p))!;

    [Fact]
    public void AdminController_endpoints_are_never_gated_on_a_user_role_permission()
    {
        var offenders = new List<string>();

        foreach (var action in ActionsOf<AdminController>())
        {
            foreach (var policy in PoliciesOn(action))
            {
                if (PermissionKeys.UserRoleDefaults.Contains(policy))
                    offenders.Add($"{action.Name} -> {policy}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Admin endpoints gated on a permission every registered user holds: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_admin_action_declares_an_authorization_policy()
    {
        var unguarded = ActionsOf<AdminController>()
            .Where(a => a.GetCustomAttributes<HttpMethodAttribute>().Any())
            .Where(a => !PoliciesOn(a).Any())
            .Select(a => a.Name)
            .ToList();

        Assert.True(unguarded.Count == 0, "Admin actions without a policy: " + string.Join(", ", unguarded));
    }

    [Fact]
    public void Admin_console_permissions_are_not_granted_to_the_user_role()
    {
        Assert.DoesNotContain(PermissionKeys.AdminStatsView, PermissionKeys.UserRoleDefaults);
        Assert.DoesNotContain(PermissionKeys.AdminPurchasesView, PermissionKeys.UserRoleDefaults);
        Assert.DoesNotContain(PermissionKeys.AuditView, PermissionKeys.UserRoleDefaults);
        Assert.DoesNotContain(PermissionKeys.UsersView, PermissionKeys.UserRoleDefaults);
        Assert.DoesNotContain(PermissionKeys.UsersManage, PermissionKeys.UserRoleDefaults);
        Assert.DoesNotContain(PermissionKeys.RolesManage, PermissionKeys.UserRoleDefaults);
        Assert.DoesNotContain(PermissionKeys.AdaptersManage, PermissionKeys.UserRoleDefaults);
    }

    [Fact]
    public void Every_user_role_default_is_a_known_permission()
    {
        foreach (var perm in PermissionKeys.UserRoleDefaults)
            Assert.Contains(perm, PermissionKeys.All);
    }

    [Fact]
    public void Every_permission_has_display_metadata()
    {
        var missing = PermissionKeys.All.Where(p => !PermissionKeys.Meta.ContainsKey(p)).ToList();
        Assert.True(missing.Count == 0, "Permissions missing from Meta: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Every action on a user-scoped controller must carry a policy. These endpoints return one
    /// account's tickets and trips, so an action that forgot its policy would be reachable by any
    /// authenticated caller.
    /// </summary>
    [Theory]
    [InlineData(typeof(ImportedTicketsController))]
    [InlineData(typeof(JourneysController))]
    public void Every_user_scoped_action_declares_an_authorization_policy(Type controller)
    {
        var unguarded = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(a => a.GetCustomAttributes<HttpMethodAttribute>().Any())
            .Where(a => !PoliciesOn(a).Any())
            .Select(a => a.Name)
            .ToList();

        Assert.True(unguarded.Count == 0, $"{controller.Name} actions without a policy: " + string.Join(", ", unguarded));
    }

    /// <summary>
    /// Both features are useless to an ordinary account without these, and the grant has been
    /// forgotten before — WalletsManage and ImportedTicketsManage both shipped missing from the
    /// User role, leaving the endpoints reachable only by an admin.
    /// </summary>
    [Fact]
    public void Ticket_and_journey_permissions_are_granted_to_the_user_role()
    {
        Assert.Contains(PermissionKeys.ImportedTicketsView, PermissionKeys.UserRoleDefaults);
        Assert.Contains(PermissionKeys.ImportedTicketsCreate, PermissionKeys.UserRoleDefaults);
        Assert.Contains(PermissionKeys.ImportedTicketsManage, PermissionKeys.UserRoleDefaults);
        Assert.Contains(PermissionKeys.JourneysView, PermissionKeys.UserRoleDefaults);
        Assert.Contains(PermissionKeys.JourneysCreate, PermissionKeys.UserRoleDefaults);
        Assert.Contains(PermissionKeys.JourneysManage, PermissionKeys.UserRoleDefaults);
    }

    /// <summary>
    /// The mirror of the admin test above. A user-facing controller gated on a permission the User
    /// role does not hold is a 403 for every ordinary account — which is how settings.manage
    /// shipped: SettingsController.Update required it and UserRoleDefaults never granted it.
    /// </summary>
    [Fact]
    public void User_facing_endpoints_are_gated_only_on_permissions_the_user_role_holds()
    {
        var offenders = new List<string>();

        foreach (var action in ActionsOf<SettingsController>()
            .Concat(ActionsOf<ProfileController>())
            .Concat(ActionsOf<WalletController>())
            .Concat(ActionsOf<ImportedTicketsController>())
            .Concat(ActionsOf<JourneysController>()))
        {
            foreach (var policy in PoliciesOn(action))
            {
                // WalletsTopUp is deliberately excluded from UserRoleDefaults — see PermissionKeys.cs
                if (policy == PermissionKeys.WalletsTopUp) continue;
                if (!PermissionKeys.UserRoleDefaults.Contains(policy))
                    offenders.Add($"{action.DeclaringType!.Name}.{action.Name} -> {policy}");
            }
        }

        Assert.True(offenders.Count == 0,
            "User-facing endpoints require permissions the User role is not granted: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The upload endpoint accepts a 10 MB file and runs image, archive and PDF parsing on it, so
    /// it must not sit on the global limiter alone.
    /// </summary>
    [Fact]
    public void The_upload_endpoint_has_its_own_rate_limit()
    {
        var upload = typeof(ImportedTicketsController).GetMethod(nameof(ImportedTicketsController.Upload));
        Assert.NotNull(upload);

        var limiter = upload!.GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>();
        Assert.NotNull(limiter);
        Assert.Equal("Upload", limiter!.PolicyName);
    }
}
