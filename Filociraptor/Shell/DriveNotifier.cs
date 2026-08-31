using ShellN;
using ShellN.Extensions;

namespace Filociraptor.Shell;

// tells the drive pane when a drive appears or disappears.
// the shell is the right source rather than device notifications, because a mapped network drive is no device.
internal sealed class DriveNotifier : ChangeNotifier
{
    private const SHCNE_ID _driveEvents =
        SHCNE_ID.SHCNE_DRIVEADD |
        SHCNE_ID.SHCNE_DRIVEREMOVED |
        SHCNE_ID.SHCNE_DRIVEADDGUI |
        SHCNE_ID.SHCNE_MEDIAINSERTED |
        SHCNE_ID.SHCNE_MEDIAREMOVED |
        SHCNE_ID.SHCNE_NETSHARE |
        SHCNE_ID.SHCNE_NETUNSHARE;

    public Task Start() => Run(null, true, _driveEvents);
}
