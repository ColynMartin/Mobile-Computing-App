using Microsoft.Maui.ApplicationModel;

namespace BookClubApp.App;

public class LocalNetworkPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        new[]
        {
            ("android.permission.ACCESS_LOCAL_NETWORK", true)
        };
}