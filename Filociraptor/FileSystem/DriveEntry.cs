namespace Filociraptor.FileSystem;

internal sealed class DriveEntry
{
    public required string Root { get; init; }
    public required string Label { get; init; }
    public required DriveType Type { get; init; }
    public bool IsReady { get; init; }
    public bool IsPending { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }

    public long UsedBytes => TotalBytes - FreeBytes;
    public float UsedRatio => TotalBytes > 0 ? (float)((double)UsedBytes / TotalBytes) : 0;

    public string TypeName => Type switch
    {
        DriveType.Fixed => Res.DriveLocal,
        DriveType.Removable => Res.DriveRemovable,
        DriveType.Network => Res.DriveNetwork,
        DriveType.CDRom => Res.DriveCdRom,
        DriveType.Ram => Res.DriveRam,
        _ => Res.DriveOther,
    };
}
