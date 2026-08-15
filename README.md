# Filociraptor

A fast Windows file manager in C#, Direct2D and NativeAOT.

The point of this project is speed. It exists to show that a managed, ahead of time compiled application can list,
sort and draw very large folders as fast as a native one, provided it refuses the things that normally make Windows
file listings slow.

## Numbers

Measured with the built in benchmark, Release NativeAOT x64, warm file system cache, three runs.

| Folder | Items | Enumerate | Sort by name | Frame | Working set | Allocated during scan |
| --- | --- | --- | --- | --- | --- | --- |
| `C:\Windows\System32` | 5 064 | 1.8 ms | 0.3 ms | 0.74 ms | 10 MB | 0 bytes |
| `C:\Windows\WinSxS` | 25 444 | 20.5 ms | 4.5 ms | 0.80 ms | 17 MB | 0 bytes |

A frame costs the same in both, because only the rows on screen are ever drawn. Zero garbage collections happen
during a scan, in any generation. The published executable is 7.2 MB and needs no runtime installed.

Reproduce with:

```bash
filo bench "C:\Windows\WinSxS" 3
```

There is also a self test, which builds the device, scans, and draws real frames without a human having to look at a
window. It returns a non zero exit code on failure.

```bash
filo selftest "C:\Windows\System32"
```

## How it gets there

**Nothing is allocated per file.** A folder is two flat unmanaged buffers, one holding every name end to end and one
holding a 32 byte record per entry, plus a single integer array for the sort order. A million files cost a handful of
allocations rather than a million objects, so there is no garbage to collect and no pause to hide.

**The shell is never on the hot path.** Enumeration and metadata come from plain file APIs. The shell is reserved for
icons and thumbnails, which are only ever requested for the rows currently on screen. A shell call in the enumeration
path is what makes a listing stall for seconds on a network or placeholder folder.

**Sorting stays on the integer path.** The first characters of each name are folded into a 64 bit key, so the bulk of
the work is a plain integer sort. Runs that share a key are refined on the characters that follow, skipping whatever
prefix they already have in common, and a real comparison is only reached once a run is down to a handful of entries.

**The listing is virtualised and drawn on the GPU.** Direct2D on a flip model swapchain, text drawn straight from the
character arena and from stack buffers, so the cost of a frame follows the height of the window rather than the size
of the folder.

## Status

This is an early vertical slice. What works today:

* asynchronous batched enumeration, so the first rows appear a few milliseconds in whatever the folder size.
* a details view with virtualised scrolling, keyboard navigation and sortable columns.
* navigation into folders and back up, with an in flight scan cancelled instantly on navigation.
* a live performance overlay, and a headless benchmark mode.

Not there yet: the drive pane and splitter, icons and thumbnails, the icon and thumbnail view modes.

## Keys

| Key | Action |
| --- | --- |
| `Enter`, double click | open the selected folder |
| `Backspace` | go up |
| `F5` | rescan |
| `F11` | render continuously, for frame time measurement |
| `F12` | toggle the performance overlay |

## Building

```bash
dotnet build Filociraptor.slnx -c Release -p:Platform=x64
```

For the ahead of time compiled build:

```bash
dotnet publish Filociraptor/Filociraptor.csproj -c Release -p:Platform=x64 -r win-x64
```

The native link step needs the Visual Studio toolchain, so run it from a developer prompt, or make sure `vswhere.exe`
is reachable from the path.

## Dependencies

Everything comes from NuGet. No native code of its own, no wrapper layer.

* [DirectNAot](https://www.nuget.org/packages/DirectNAot) and DirectNAot.Extensions, for Direct2D, DirectWrite,
Direct3D, DXGI and the Win32 window.
* ShellN and ShellN.Extensions will join for icons and thumbnails, which need the shell.

## License

MIT.
