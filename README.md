# Filociraptor

A fast Windows file manager in 100% pure C#, DirectX, Direct2D and NativeAOT for x86, x64 and ARM64.

The published **standalone** executable is 9.5 MB (2.9 MB with UPX) and needs nothing to be installed, only Windows 7 SP1 and later.

<img width="256" src="Filociraptor.png" />

The point of this project is speed. It exists to show that a managed, ahead of time compiled application can list, sort and draw very large folders as fast as a native one.

**It is a demonstration, not a replacement for Explorer.** Explorer has decades of behaviour behind it, and this reads the file system directly wherever it can, asking the shell only where there is no folder on disk to read. 
What is here, though, is not a mock up. It browses, sorts, previews and opens real files, at speeds Explorer does not reach, and it is pleasant enough to use for real work.

## Numbers

Measured with the built in benchmark, Release NativeAOT x64, warm file system cache, best of three runs.
The frame is read from the performance overlay in a 1904 by 1272 window, while scrolling continuously.

| Folder | Items | Enumerate | Sort by name | Frame | Working set |
| --- | --- | --- | --- | --- | --- |
| `C:\Windows\System32` | 5064 | 1.8 ms | 1.3 ms | 2.1 ms | 13 MB |
| `C:\Windows\WinSxS` | 25444 | 23.0 ms | 15.5 ms | 2.0 ms | 21 MB |

A frame costs the same in both, because only the rows on screen are ever drawn. A scan allocates nothing and causes no garbage collection at all.

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
* the title bar and the menus are sized by the monitor alone, the way any ordinary window behaves, and only the listing takes the zoom on top of that.
Everything is measured in device independent units and turned into pixels once, so moving to a screen at 150 per cent redraws rather than rescales, and stays sharp.

## What it does

### Browsing

* browses drives and folders, with back, forward and up, and the left pane always current.
* browses the shell namespace as well, This PC, the recycle bin, a phone or a camera plugged in, the network, the libraries and your own folders. A folder on disk still takes the fast path above, the namespace is used only where there is no path to read.
* the left pane lists the drives, with their space used, and under them the same places Explorer shows at the top of its tree.
* browses an archive as a folder, the way Windows 11 does, with the proper icon for every file in it and real thumbnails for the pictures, decoded from the archive itself. An option turns that off and opens an archive with its application instead. On Windows 10 the option is there and greyed, because Windows 10 does not present archives that way.
* supports all unicode characters in names.
* notices drives arriving and leaving, including mapped network drives, and moves off a drive that is pulled out.
* follows the folder on screen, so a file created or deleted by anything else appears or disappears on its own. Namespace folders are watched too, so emptying the recycle bin shows up straight away.

### Looking at it

* details, small, medium and large icons, and thumbnails, with real shell icons and thumbnails.
* shows or hides hidden and protected operating system files, faded rather than merely listed.
* a large view of any image WIC can decode, on hover.
* global zoom, sortable columns, keyboard navigation, and its own title bar, with the buttons, the column headers and the rows lighting up under the pointer.
* light or dark, following the system setting or held to one of them. Two palettes, and everything moves together, the listing, the left pane, the title bar, the menus and the hover preview.
* the window itself is a colour, or a material. Mica is a flat tint of the wallpaper, acrylic is whatever is behind the window, blurred. The listing keeps enough of itself for the rows to stay readable and the chrome keeps more. Both materials want Windows 11, and a colour is the default, because a material costs the text its subpixel antialiasing.
* follows the monitor it is on. It is per monitor v2 aware, so the text, the icons and the title bar are drawn at the scaling of the screen the window is on, and they change with it when the window is dragged to a screen with different scaling, or when that scaling is changed under it. The zoom is a separate thing that multiplies the listing on top of that.

### Choosing and moving

* several items at once. A click chooses one, `Ctrl` adds or removes one, `Shift` takes everything between, and `Ctrl+A` takes the lot. A right click on any of them brings up the menu Explorer shows for a set of files rather than for a single one.
* drag and drop, both ways. What leaves the window is the shell's own data object, so anything that accepts a drop from Explorer accepts one from here. What arrives is handed to the drop target of the folder under the pointer, so the feedback, the menu on a right drag and the copy or move itself are the shell's.
* copy, cut and paste, marked on the clipboard the way Explorer marks them, so a cut here is a move when it is pasted anywhere.
* delete, to the recycle bin with `Del` and for good with `Shift+Del`, which the shell asks about first.

### Opening things

* the real Explorer context menu, with installed handlers appearing in it (ie: where "Share" works), on an item, and on the empty space around them the same menu for the folder you are in.
* opens a folder from that menu in place rather than handing it to Explorer, and "open in new process" starts another one of these instead, beside the window it came from.
* opens files with their default command, and reveals anything in Explorer.
* takes the folder to open on the command line, `filo "C:\Windows"`, and shell names as well, so `filo "shell:Downloads"` works.

### What it remembers

* remembers what you change. The gear in the title bar opens a menu for the font and its size, the colour of the text, the theme and what the window is made of, the space around thumbnails, whether they are square, their titles and the wrapping of them, the size of the hover preview, and the folders you have been to, with a sweep for the ones that are no longer there.
* comes back where it was. The same monitor, the same size and position, the same zoom, maximized if it was, and on the folder it was last showing. The settings are one readable file, beside the executable when there is one there, which is what makes a copied folder portable, and in your profile otherwise.

## Where it runs

Windows 7 SP1 and later, on x86, x64 and ARM64.

.NET has not supported Windows 7 since version 8, and this is built with 10, so running there is not something anyone promises.
It does though, and the reason is that a NativeAOT binary carries its own runtime and asks the system for very little.
What Windows 7 does need is the Universal CRT, which comes with KB2999226, and the Platform Update, KB2670838, for the Direct2D and DXGI versions the drawing uses.

Where something is missing, it is asked for once and given up on rather than assumed:

* the icons are Segoe MDL2 Assets, which came with Windows 10. Without it they are drawn from Segoe UI Symbol, which has shipped since Windows 7.
* the flip presentation model is Windows 10 and later. Without it the swap chain is the older kind that copies, made the older way, since there is no IDXGIFactory2 before Windows 8 either.
* per monitor scaling is Windows 8.1 and later. Without it the window follows the system setting, which is what Windows 7 has.
* archives are shown as folders the way Windows 11 does it. On Windows 10 and earlier that option is there and greyed.
* acrylic and mica are Windows 11 22H2 and later. Earlier, the choice is there and greyed.

**It also runs where there is no 3D adapter at all.** A display adapter with no Direct3D behind it is enough to stop most DirectX programs before they draw anything,
and depending on the host, the guest and what is shared between them, a virtual machine or a sandbox can present exactly that.
The device is asked of the display adapter first and of the rasterizer that comes with Windows secondly, so where there is nothing to draw with, it draws in software,
and says so in its trace rather than being quietly slow. It has been run in Hyper-V (Windows 7+) and in Windows Sandbox.

## What it does not do

* no rename and no new folder. The context menu can do both, because that menu is Explorer's, not ours.
* no address bar, no typing or pasting a path, and no search.
* no tabs and no favourites.
* no accessibility. Everything is custom drawn, so a screen reader sees an empty window.

## Where it could go

None of the above is blocked by anything structural. The gap between this and something you would use every day is ordinary work, not research, 
because the hard parts are already solved by three libraries that between them cover mostly everything Windows can do here:

* [DirectN](https://github.com/smourier/DirectNAot) gives the whole of DirectX, Direct2D, DirectWrite and Win32, ahead of time compiled and without a wrapper in the way.
* [ShellN](https://github.com/smourier/ShellBat) gives the shell itself, so the namespace, the property system, context menus, drag and drop and file operations are all reachable.
* [WicNet](https://github.com/smourier/WicNet) gives every image format Windows can decode.

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
| the settings, and the folders you have been to | the gear in the title bar |
| light or dark, and what the window is made of | the gear in the title bar |
| pick a zoom, or reset it | click the percentage in the title bar |
| open an archive as a folder, or as a file | it is a folder by default, the settings turn it into a file |
| choose several items | `Ctrl` click for one more, `Shift` click for everything between, `Ctrl+A` for all |
| copy, cut, paste | `Ctrl+C`, `Ctrl+X`, `Ctrl+V` |
| delete to the recycle bin, or for good | `Del`, `Shift+Del` |
| take items somewhere else | drag them, from anywhere in the selection |
| rescan now | `F5` |
| show the performance overlay | `F12` |

## Dependencies

Everything comes from NuGet.

* [DirectNAot](https://www.nuget.org/packages/DirectNAot), for Direct2D, DirectWrite, Direct3D and the window itself.
* [ShellN](https://www.nuget.org/packages/ShellN), for the icons, thumbnails and context menus.
* [WicNetCore](https://www.nuget.org/packages/WicNetCore), to know which files WIC can decode.

## Screenshots

### A large folder, in details

25 444 items in `C:\Windows\WinSxS`, read straight from the file system and ordered the way Explorer orders names. Only the rows on screen are ever drawn, so the size of the folder does not change what a frame costs.

![A large folder, in details](docs/details-winsxs.png)

### What it costs, while it costs it

`F12` puts the counters on screen. The scan, the time to the first rows, the sort, the cost of a frame and everything allocated. Reading twenty five thousand items causes no garbage collection at all.

![What it costs, while it costs it](docs/overlay-winsxs.png)

### Thumbnails

The shell's own thumbnails, asked for only where they are on screen, and drawn as they arrive rather than after the folder is complete.

![Thumbnails](docs/thumbnails-photos.jpg)

### A closer look, on hover

Hovering a picture decodes it with WIC at the size it is shown, rather than blowing up a thumbnail the shell had kept. Anything WIC can read works. `Esc` or moving away puts it back.

![A closer look, on hover](docs/preview-photos.jpg)

### Fonts, each in its own face

`C:\Windows\Fonts`, where the thumbnail of a font is the font itself.

![Fonts, each in its own face](docs/thumbnails-fonts.png)

### Large icons

The five sizes come off the slider in the title bar, or `Ctrl` with `1` to `5`.

![Large icons](docs/icons-windows.png)

### Zoomed out

The zoom takes the whole listing with it, the thumbnails, the titles and the rows of the left pane together. This is the same folder at half size.

![Zoomed out](docs/zoomed-out-thumbnails.jpg)

### Square thumbnails, close together

Two settings. One crops every thumbnail to a square so the grid lines up whatever shape the pictures are, the other decides how much room is left around them.

![Square thumbnails, close together](docs/square-thumbnails.jpg)

### As much as will fit

Square, no titles, no spacing at all and zoomed out to half. A hundred and forty photographs in one window, each one a real thumbnail.

![As much as will fit](docs/photo-wall.jpg)

### The settings

The gear in the title bar. The font and its size, the colour of the text, the theme and the window material, the room around thumbnails, the size of the hover preview, what a thumbnail shows, how archives open, and the folders you have been to. All of it is written to one readable file.

![The settings](docs/settings.jpg)

### A window made of acrylic

Acrylic takes what is behind the window and blurs it, mica takes the wallpaper and tints it flat. Neither is on unless it is asked for.

![A window made of acrylic](docs/acrylic.png)

### The zoom

Clicking the percentage offers the usual sizes and ticks the one in use. The wheel with `Ctrl` still works.

![The zoom](docs/zoom.jpg)

### An archive, browsed as a folder

Windows 11 presents an archive as a folder and so does this. The pictures inside it are decoded from the archive itself, because the shell has no thumbnail to offer for something that is not a file on disk.

![An archive, browsed as a folder](docs/archive.jpg)

### The namespace

This PC, with the drives and whatever else is plugged in. A phone, a camera, a media server. There is no path to read for any of it, so the shell answers instead.

![The namespace](docs/this-pc.png)

### Windows 7

The same thing on Windows 7, in a Hyper-V machine with no 3D adapter at all, drawn by the software rasterizer (WARP).

![Windows 7](docs/windows-7.png)

### The real Explorer context menu

Not an imitation of it. Everything installed on the machine appears in it and works, which is where "Share", "7-Zip" and the rest come from.

![The real Explorer context menu](docs/context-menu.png)

### The same in the recycle bin

A namespace folder with a menu of its own, so what it offers is what the recycle bin offers.

![The same in the recycle bin](docs/context-menu-recycle-bin.png)

