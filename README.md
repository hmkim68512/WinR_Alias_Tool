# WinR Alias

**English** | [한국어](README.ko.md)

Launch any installed program from the Windows **Run dialog (Win+R)** with a short name you choose —
like Spotlight on macOS, but using a mechanism Windows already has built in.

```
Win+R → sl → Enter        # opens Slack
Win+R → putty → Enter     # opens PuTTY
```

- Finds **every app in the Start menu**: regular desktop apps, Microsoft Store (MSIX) apps, and Squirrel-installed apps such as Slack and Discord.
- Search-first UI: **type a name → Enter → type an alias → Enter**, and you're done.
- No background process, no PATH changes, and no admin rights needed.

> UI language is currently Korean.

---

## Getting started

### Download

Download a zip from [Releases](../../releases), extract it, and run `WinRAlias.exe`. No installation is needed.

| File | Size | Notes |
|---|---|---|
| `WinRAlias-vX.Y.Z-win-x64.zip` | ~70 MB | Includes the .NET runtime, so it runs on any Windows 10/11 PC |
| `WinRAlias-vX.Y.Z-win-x64-framework.zip` | < 1 MB | Requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

> The exe is not code-signed, so Windows SmartScreen may show a warning the first time you run it.
> Click **More info → Run anyway**, or build it yourself from source (see below).

### Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```bash
git clone https://github.com/<your-account>/WinRAlias.git
cd WinRAlias/WinRAlias
dotnet run
```

Single-file build:

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Output: `bin/Release/net8.0-windows/win-x64/publish/WinRAlias.exe`

---

## Usage

### Add an alias

1. Start the app. The search box has focus, so type a program name, for example `slack`.
2. Press **Enter**. Focus moves to the alias box on the right, prefilled with a suggested name.
3. Change the name if you like (for example `sl`) and press **Enter**.
4. Press **Win+R**, type `sl`, and press **Enter**.

The hint under the alias box tells you right away if the name is already taken or if it collides with a program on your `PATH`.

### Keyboard shortcuts

| Key | Action |
|---|---|
| `Ctrl+F` / `F3` | Focus the search box |
| Just start typing | Typing anywhere in a list goes into the search box |
| `Enter` (search box / list) | Jump to the alias box for the selected program |
| `Enter` (alias box) | Register the alias |
| `Esc` | Clear the search box, or return to the list |
| `F5` | Rescan installed programs |
| `Ctrl+N` | New alias with a custom target path |
| `Enter` / `Delete` (alias tab) | Edit / delete the selected alias |

### Manage aliases

The **등록된 alias** (registered aliases) tab lists every Run-dialog alias on the system,
including those that other programs registered.

| Color | Meaning |
|---|---|
| Green | Created by this app |
| Black | Registered by another program. Be careful when editing or deleting these |
| Red | The target file no longer exists |
| Gray | Hidden by an alias with the same name in HKCU |

Use **Advanced** (`고급 설정으로 추가…`) to set a working folder or to choose the scope
(current user or all users). The all-users scope requires running the app as administrator.

---

## How it works

### The App Paths registry key

When you type a name in the Run dialog, Windows (via `ShellExecuteEx`) looks for a registry key named after it:

```
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<alias>.exe   (current user)
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<alias>.exe   (all users)
```

If the key exists, Windows runs the path stored in its `(Default)` value. This feature has existed since
Windows 95, and installers use it all the time (`chrome`, `excel`, and so on). WinR Alias simply creates
and edits these keys for you:

```
HKCU\...\App Paths\sl.exe
  (Default)    = C:\Users\me\AppData\Local\Microsoft\WindowsApps\Slack.exe
  Path         = (optional working folder)
  ManagedBy    = WinRAlias                  ← marks keys created by this app
  LaunchTarget = shell:AppsFolder\...       ← only for launcher-based aliases (see below)
```

Because this is a native Windows feature, aliases keep working after you close or even delete WinR Alias.

### Finding installed programs

| Order | Source | Notes |
|---|---|---|
| 1 | `shell:AppsFolder` | The same list as the Start menu's "All apps", including Store apps |
| 2 | Start menu `.lnk` shortcuts | Reads shortcut arguments; fallback for item 1 |
| 3 | Uninstall registry (`DisplayIcon`) | Fallback for apps without a Start menu shortcut |

What the alias points to depends on the kind of app:

| App kind | Alias target |
|---|---|
| Regular desktop app | The `.exe` path |
| Squirrel app (`Update.exe --processStart X`) | `X` in the install root, which stays valid across updates |
| Store app with an execution alias (Slack, Spotify, Teams…) | `%LOCALAPPDATA%\Microsoft\WindowsApps\<name>.exe` |
| Store app without an exe (Calculator…), or a shortcut that needs arguments (Git Bash, Chrome PWAs…) | A launcher shortcut (see below) |

**Launcher shortcuts.** An App Paths key can only store a file path, not command-line arguments.
`ShellExecute` does follow a `.lnk` file, though, so for these apps WinR Alias creates
`%LOCALAPPDATA%\WinRAlias\launchers\<alias>.lnk`, which runs `explorer.exe shell:AppsFolder\<AppID>`,
and stores that `.lnk` path in the key. Launching through `shell:AppsFolder` is exactly how the Start menu does it.

### Files this app writes

Everything lives in `%LOCALAPPDATA%\WinRAlias\`. All of it can be deleted safely.

| File | Purpose |
|---|---|
| `backup.jsonl` | One JSON line per key, saved right before an alias is overwritten or deleted, so you can restore it |
| `launchers\*.lnk` | Launcher shortcuts (described above). For all-users aliases: `C:\Program Files\WinRAlias\launchers` |
| `apps-cache.json` | Last scan result, shown instantly at startup while a fresh scan (3–8 s) runs in the background |
| `icons\*.png` | Icon cache |

---

## Security & privacy

- **No network access and no telemetry.** The app never connects to the internet.
- **No admin rights by default.** Aliases go into `HKCU`, which only affects your own account.
  Other users are not affected, and system files and `PATH` are left untouched.
- **It writes only what you ask for.** The app changes a registry key only when you register, edit,
  or delete an alias, and it saves a backup to `backup.jsonl` first.
- **Name collisions are flagged.** If an alias would shadow a program on `PATH` or an existing alias,
  the app warns you before saving.
- **Administrator mode** (`관리자로 재실행`) is needed only for all-users (`HKLM`) aliases.
  - Programs you start from the app ("Run", "Test", "Open file location") are handed to the desktop shell,
    so they run with **normal** rights instead of inheriting admin rights.
  - Launcher shortcuts for all-users aliases are stored in `C:\Program Files\WinRAlias\launchers`,
    which only administrators can write to. Other users can't swap them out.

> Note: App Paths is a well-known Windows feature, and malware can abuse it too. Any program running
> under your account can already write these keys, so this tool does not open a new hole.
> It does make existing aliases easy to see and review: check the alias tab for entries you don't recognize.

### Uninstall / clean up

1. Delete your aliases in the alias tab, or remove every key this app created with PowerShell:

   ```powershell
   Get-ChildItem 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths' |
     Where-Object { (Get-ItemProperty $_.PSPath).ManagedBy -eq 'WinRAlias' } |
     Remove-Item -Recurse
   ```

2. Delete `%LOCALAPPDATA%\WinRAlias\` and `WinRAlias.exe`.

---

## Limitations

- Works only in the Run dialog (and anything else that uses `ShellExecute`), **not** in `cmd` or PowerShell.
- There is no UI for command-line arguments yet. As a workaround, create a `.lnk` with the arguments you need and set it as the target.
- There is no restore UI for `backup.jsonl` yet; restore entries manually.
- The UI is in Korean only for now.

## Project structure

| File | Role |
|---|---|
| `AppScanner.cs` | Installed-program discovery and scan cache |
| `AppPathsRegistry.cs` | Read, write, and delete App Paths keys; name validation; PATH collision checks; backups |
| `ShellLauncher.cs` | Launcher `.lnk` files for apps without an exe |
| `IconLoader.cs`, `NativeShell.cs` | Asynchronous shell icon loading and Win32 interop |
| `MainForm.cs`, `AliasDialog.cs`, `Ui.cs` | WinForms UI, built in code (no designer) |
| `Models.cs` | Data models and path helpers |

## License

[MIT](LICENSE)
