# FigmaShell

A lightweight native Windows wrapper for figma.com with in-app tabs. Built with WPF (.NET 9) and WebView2.

## Requirements
- Windows 10/11
- .NET 9 Desktop Runtime (the SDK is needed to build)
- Microsoft Edge WebView2 Runtime (preinstalled on Windows 11)

## Build & run
```powershell
dotnet run --project src/FigmaShell
```

## Publish (framework-dependent, ReadyToRun)
```powershell
dotnet publish src/FigmaShell -p:PublishProfile=win-x64
# output: publish/win-x64/FigmaShell.exe
```

## Installer (Setup.exe)
Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).
```powershell
.\build-installer.ps1
# output: dist/FigmaShell-Setup-<version>.exe
```
- Installs per-user by default, with no admin prompt. The user can choose "all users" instead.
- Downloads and installs the **.NET 9 Desktop Runtime** and the **WebView2 Runtime** only if they are missing.
- Adds a Start menu shortcut and an optional desktop shortcut. It appears in Apps & Features with an uninstaller.
- When uninstalling, it asks whether to also delete the Figma sign-in and session data in `%LOCALAPPDATA%\FigmaShell`.
- Bump `<Version>` in `src/FigmaShell/FigmaShell.csproj` for each release. The installer takes its version from the exe.

## Shortcuts
| Keys | Action |
|---|---|
| Ctrl+T | Go to Home tab |
| Ctrl+N | New design file (new tab) |
| "+" button | Menu: new design file / FigJam board / Slides deck, or open an existing file (goes to Home) |
| Ctrl+W / Ctrl+F4 / middle-click tab | Close tab |
| Ctrl+Shift+T | Reopen closed tab |
| Ctrl+Tab / Ctrl+Shift+Tab, Ctrl+PgDn / Ctrl+PgUp | Next / previous tab |
| Ctrl+1–8, Ctrl+9 | Jump to tab (Ctrl+1 Home, Ctrl+2 Community), last tab |
| F5, Ctrl+F5 | Reload, hard reload |
| F11 | Fullscreen |
| F12 (Debug builds) | DevTools |

All other keys go straight to Figma. Browser shortcuts such as Ctrl+R, Ctrl+F, Ctrl+P and Ctrl+G are disabled so Figma receives them.

## How it stays fast
- All tabs share one WebView2 environment: one browser process, one GPU process and one login, with a separate renderer per tab.
- Restored tabs load lazily, only when you first open them.
- Background tabs are hidden, which lets Chromium throttle them. They are also set to a low memory target, and after 5 minutes idle they are suspended with `TrySuspendAsync`. They resume instantly when clicked.
- Optional hard discard: set `TabManager.DiscardAfter`. It is off by default.

## Behaviour
- Two permanent icon-only tabs sit at the left: **Home** (file browser) and **Community**. They can't be closed or moved.
- Files never open inside the Home or Community tab; they always open in their own tab. A file tab that goes to Home or Community, for example through "Back to files", switches to the matching permanent tab.
- Links to Figma files open as new tabs. Ctrl+click or middle-click opens them in the background.
- Sign-in popups (Google, Apple, Microsoft, SSO) open as real popups so OAuth works.
- All other external links open in your default browser.
- Tabs and window position are restored on launch. Data is stored in `%LOCALAPPDATA%\FigmaShell`.
- Running the exe again with a figma.com URL opens that URL as a tab in the existing window.
