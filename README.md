# iTunes Invert for Windows

![Blue sun and music note icon](blue-sun-note.png)

A small utility that inverts the active iTunes window to reduce its bright white appearance. The overlay follows the window as it moves or resizes, passes clicks through to iTunes, and hides when you switch to another app. It uses the Windows Magnification API and does not change iTunes files or your music library.

## Download

Get either build from [Releases](https://github.com/mariocles/iTunes-Invert-Windows/releases):

- **iTunesInvert-Portable.exe** — one compressed executable with the .NET runtime included. Run it directly on Windows x64.
- **iTunesInvert-Small.zip** — smaller download. Extract all four files together and install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) if it is not already present.

Run the program, then bring iTunes to the front. Use the blue sun icon in the system tray to pause or quit. Double-click the tray icon to toggle pause. **Ctrl+Alt+F12** quits if that shortcut is available.

## Limitations

- Every color in the active iTunes window is inverted, including album artwork.
- Some menus, popups, and unusual multi-monitor arrangements may not be covered perfectly.
- The overlay disappears when another app is active; it does not recolor iTunes itself.
- The binaries are unsigned, so Windows may show a SmartScreen warning.

The builds were checked for launch, color-filter initialization, window tracking, and an embedded icon on Windows x64. Visual behavior should be checked on each iTunes and display setup.

## Build from source

Requires the .NET 8 SDK on Windows x64. The project defaults to the compressed, self-contained version:

```powershell
dotnet publish iTunesInvert.csproj -c Release -r win-x64 -o publish-portable
```

For the smaller build that uses an installed .NET 8 Desktop Runtime:

```powershell
dotnet publish iTunesInvert.csproj -c Release -r win-x64 -o publish-small -p:SelfContained=false -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -p:EnableCompressionInSingleFile=false
```

The blue sun icon is embedded in the executable and used in the system tray. Source and icon artwork are in this repository.
