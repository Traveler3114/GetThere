using TransitInfoAPI.Common;

namespace GetThere.Tests;

public class PermissionKeysTests
{
    [Fact]
    public void AlertSources_permissions_appear_in_All_and_Meta()
    {
        Assert.Contains(PermissionKeys.AlertSourcesView, PermissionKeys.All);
        Assert.Contains(PermissionKeys.AlertSourcesManage, PermissionKeys.All);
        Assert.True(PermissionKeys.Meta.ContainsKey(PermissionKeys.AlertSourcesView));
        Assert.True(PermissionKeys.Meta.ContainsKey(PermissionKeys.AlertSourcesManage));
    }
}
