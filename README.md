# Filociraptor

A fast Windows file manager in C#, DirectX, Direct2D and NativeAOT for x86, x64 and ARM64.

<img width="256" src="Filociraptor.png" />

The point of this project is speed. It exists to show that a managed, ahead of time compiled application can list, sort and draw very large folders as fast as a native one.

**It is a demonstration, not a replacement for Explorer.** Explorer has decades of behaviour behind it, and this reads the file system directly wherever it can, asking the shell only where there is no folder on disk to read. 
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
* the namespace costs several times what reading a folder costs, an object and a handful of calls for every item, so it is used only where there is nothing on disk to read. This PC, a phone, the recycle bin. Those hold a few items, never thousands, so it never shows.
* the window draws only when something changed, and while something is fading it draws in step with the monitor rather than as fast as it can.

## What it does

* browses drives and folders, with back, forward and up, and the left pane always current.
* browses the shell namespace as well, This PC, the recycle bin, a phone or a camera plugged in, the network, the libraries and your own folders. A folder on disk still takes the fast path above, the namespace is used only where there is no path to read.
* the left pane lists the drives, with their space used, and under them the same places Explorer shows at the top of its tree.
* supports all unicode characters in names.
* notices drives arriving and leaving, including mapped network drives, and moves off a drive that is pulled out.
* follows the folder on screen, so a file created or deleted by anything else appears or disappears on its own. Namespace folders are watched too, so emptying the recycle bin shows up straight away.
* details, small, medium and large icons, and thumbnails, with real shell icons and thumbnails.
* shows or hides hidden and protected operating system files, faded rather than merely listed.
* a large view of any image WIC can decode, on hover.
* the real Explorer context menu, with installed handlers appearing in it (ie: where "Share" works), on an item, and on the empty space around them the same menu for the folder you are in.
* opens a folder from that menu in place rather than handing it to Explorer, and "open in new process" starts another one of these instead, beside the window it came from.
* opens files with their default command, and reveals anything in Explorer.
* takes the folder to open on the command line, `filo "C:\Windows"`, and shell names as well, so `filo "shell:Downloads"` works.
* global zoom, sortable columns, keyboard navigation, and its own title bar, with the buttons, the column headers and the rows lighting up under the pointer.

## What it does not do

* **no file operations at all.** No copy, move, rename, delete or new folder, and no drag and drop. The context menu can do some of it because that menu is Explorer's, not ours.
* no multiple selection, one item at a time.
* no address bar, no typing or pasting a path, and no search.
* the namespace is for browsing only. You can walk into This PC, a phone or the recycle bin and look, but nothing comes back out, because there are no file operations.
* no tabs, no favourites, no settings, and nothing is remembered between runs.
* no accessibility. Everything is custom drawn, so a screen reader sees an empty window.

## Where it could go

None of the above is blocked by anything structural. The gap between this and something you would use every day is ordinary work, not research, 
because the hard parts are already solved by three libraries that between them cover everything Windows can do here:

* [DirectN](https://github.com/smourier/DirectNAot) gives the whole of DirectX, Direct2D, DirectWrite and Win32, ahead of time compiled and without a wrapper in the way.
* [ShellN](https://github.com/smourier/ShellBat) gives the shell itself, so the namespace, the property system, context menus, drag and drop and file operations are all reachable.
* [WicNet](https://github.com/smourier/WicNet) gives every image format Windows can decode.

The namespace was the first of these steps and it is done. Multiple selection and file operations are the next two, and both are shell work that ShellN already exposes. After that an address bar, and at that point the word demonstration stops applying.

## Using it

The drives and the places you can go on the left, the listing on the right, and a splitter between them. The title bar carries the navigation buttons, the view slider and the current zoom, and shortens the path when there is no room for it.

| Action | How |
| --- | --- |
| open the selected item, a folder in place or a file in its application | `Enter`, or double click |
| back, forward, up | the first three buttons in the title bar, or `Backspace` to go up |
| show the folder in Explorer | the fourth button |
| show hidden and protected system files | the fifth button, they appear faded |
| a large view of an image, decoded by WIC | hover it, `Esc` or move away to dismiss |
| the Explorer context menu for an item | right click it |
| the same menu for the folder you are in | right click the empty space around the items |
| another window on a folder, as its own process | "open in new process" in that menu, it opens beside this one |
| start on a particular folder | `filo "C:\Windows"`, or a shell name like `filo "shell:Downloads"` |
| details, small, medium and large icons, thumbnails | the slider, or `Ctrl` with `1` to `5` |
| zoom text, icons and thumbnails together | `Ctrl` with the wheel |
| rescan now | `F5` |
| show the performance overlay | `F12` |

## Dependencies

Everything comes from NuGet.

* [DirectNAot](https://www.nuget.org/packages/DirectNAot), for Direct2D, DirectWrite, Direct3D and the window itself.
* [ShellN](https://www.nuget.org/packages/ShellN), for the icons, thumbnails and context menus.
* [WicNetCore](https://www.nuget.org/packages/WicNetCore), to know which files WIC can decode.

## Screenshots

### Icon view

<img width="1260" height="764" alt="Icon view" src="https://github.com/user-attachments/assets/a83f21ed-4d9d-4cfb-8a57-e4756c82573b" />

### Zoomed out icon view

<img width="1821" height="1225" alt="Zoomed out view" src="https://github.com/user-attachments/assets/4480ed20-85e0-40d2-a8f0-fd584c58fd53" />

### Thumbnail view (on fonts)

<img width="1921" height="1074" alt="Fonts thumbnails" src="https://github.com/user-attachments/assets/2ef7ead2-6213-43e1-b21f-9315e2baf3fc" />

### Zoomed out Thumbnail view

<img width="1820" height="1226" alt="Thumbnail view" src="https://github.com/user-attachments/assets/b8127177-62f6-473c-a0ff-dcc2db5ba93d" />

### Hover view

<img width="1821" height="1222" alt="Hover view" src="https://github.com/user-attachments/assets/e97e1983-fb4b-4858-99f2-a4a003d49643" />

### Diags / Performance overlay

<img width="1818" height="1226" alt="Overlay" src="https://github.com/user-attachments/assets/7c773b50-c4ff-4be5-a47a-12d269b9a749" />

### Full Explorer context menu

<img width="846" height="976" alt="Context menu" src="https://github.com/user-attachments/assets/2ecbb4c6-c756-4864-84f3-4334c626998b" />

