# Filociraptor

A fast Windows file manager in C#, DirectX, Direct2D and NativeAOT for x86, x64 and ARM64.

The point of this project is speed. It exists to show that a managed, ahead of time compiled application can list, sort and draw very large folders as fast as a native one.

**It is a demonstration, not a replacement for Explorer.** Explorer is a shell namespace browser with decades of behaviour behind it, and this is a file browser that reads the file system directly. 
What is here, though, is not a mock up. It browses, sorts, previews and opens real files, at speeds Explorer does not reach, and it is pleasant enough to use for real work.

## Numbers

Measured with the built in benchmark, Release NativeAOT x64, warm file system cache.

| Folder | Items | Enumerate | Sort by name | Frame | Working set |
| --- | --- | --- | --- | --- | --- |
| `C:\Windows\System32` | 5064 | 1.9 ms | 1.1 ms | 0.74 ms | 10 MB |
| `C:\Windows\WinSxS` | 25444 | 20.0 ms | 15.3 ms | 0.80 ms | 17 MB |

A frame costs the same in both, because only the rows on screen are ever drawn. A scan allocates nothing and causes no garbage collection at all. The published executable is 7.2 MB (2.5MB with UPX) and needs nothing to be installed.

```bash
filo bench "C:\Windows\WinSxS" 3
```

## How it gets there

* nothing is allocated per file, a folder is a couple of flat buffers rather than a million objects.
* the shell is never on the hot path, it is asked only for the icons and thumbnails actually on screen.
* one icon per extension rather than one per file, so a folder of 2 000 files costs a single call.
* the listing is virtualised and drawn on the GPU, so the folder size does not change what a frame costs.
* names are ordered exactly as Explorer orders them, punctuation before letters and runs of digits compared as numbers, which costs more than a plain sort and is worth it.

## What it does

* browses drives and folders, with back, forward and up, and the drive pane always current.
* supports all unicode characters in names.
* notices drives arriving and leaving, including mapped network drives, and moves off a drive that is pulled out.
* follows the folder on screen, so a file created or deleted by anything else appears or disappears on its own.
* details, small, medium and large icons, and thumbnails, with real shell icons and thumbnails.
* shows or hides hidden and protected operating system files, faded rather than merely listed.
* a large view of any image WIC can decode, on hover.
* the real Explorer context menu, with installed handlers appearing in it (ie: where "Share" works).
* opens files with their default command, and reveals anything in Explorer.
* zoom, sortable columns, keyboard navigation, and its own title bar.

## What it does not do

* **no file operations at all.** No copy, move, rename, delete or new folder, and no drag and drop. The context menu can do some of it because that menu is Explorer's, not ours.
* no multiple selection, one item at a time.
* no address bar, no typing or pasting a path, and no search.
* no shell namespace, so no This PC, no Recycle Bin, no zip folders, no libraries and no cloud placeholders. It browses the file system, nothing more.
* no tabs, no favourites, no settings, and nothing is remembered between runs.
* no accessibility. Everything is custom drawn, so a screen reader sees an empty window.

## Where it could go

None of the above is blocked by anything structural. The gap between this and something you would use every day is ordinary work, not research, 
because the hard parts are already solved by three libraries that between them cover everything Windows can do here:

* [DirectN](https://github.com/smourier/DirectNAot) gives the whole of DirectX, Direct2D, DirectWrite and Win32, ahead of time compiled and without a wrapper in the way.
* [ShellN](https://github.com/smourier/ShellBat) gives the shell itself, so the namespace, the property system, context menus, drag and drop and file operations are all reachable.
* [WicNet](https://github.com/smourier/WicNet) gives every image format Windows can decode.
* 
Multiple selection and file operations are the first two steps, and both are shell work that ShellN already exposes. After that an address bar, then the namespace, and at that point the word demonstration stops applying.

## Using it

A pane of drives on the left, the listing on the right, and a splitter between them. The title bar carries the navigation buttons, the view slider and the current zoom, and shortens the path when there is no room for it.

| Action | How |
| --- | --- |
| open the selected item, a folder in place or a file in its application | `Enter`, or double click |
| back, forward, up | the first three buttons in the title bar, or `Backspace` to go up |
| show the folder in Explorer | the fourth button |
| show hidden and protected system files | the fifth button, they appear faded |
| a large view of an image, decoded by WIC | hover it, `Esc` or move away to dismiss |
| the Explorer context menu for an item | right click |
| details, small, medium and large icons, thumbnails | the slider, or `Ctrl` with `1` to `5` |
| zoom text, icons and thumbnails together | `Ctrl` with the wheel |
| rescan now | `F5` |
| show the performance overlay | `F12` |

## Dependencies

Everything comes from NuGet.

* [DirectNAot](https://www.nuget.org/packages/DirectNAot), for Direct2D, DirectWrite, Direct3D and the window itself.
* [ShellN](https://www.nuget.org/packages/ShellN), for the icons, thumbnails and context menus.
* [WicNetCore](https://www.nuget.org/packages/WicNetCore), to know which files WIC can decode.
