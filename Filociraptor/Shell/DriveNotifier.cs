using ShellN;
using ShellN.Extensions;

namespace Filociraptor.Shell;

// tells the drive pane when a drive appears or disappears. the shell is the right source for this rather than
// device notifications, because it also covers a mapped network drive, which is no device at all.
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

    // the same as the base, without the one line that turns off exiting when the last window goes.
    // that setting is static and shared, and with it off this application keeps running after its window closes.
    public override Task Run(
        ItemIdList? idList,
        bool recursive = true,
        SHCNE_ID events = SHCNE_ID.SHCNE_ALLEVENTS,
        SHCNRF_SOURCE flags = SHCNRF_SOURCE.SHCNRF_InterruptLevel | SHCNRF_SOURCE.SHCNRF_ShellLevel) =>
        TaskUtilities.RunWithSTAThread(() =>
        {
            using var app = new Application();
            NotifyWindow = CreateNotifyWindow(idList, recursive, events, flags);
            app.Run();
        }, true);
}
