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
}
