# WinR Alias

**English** | [한국어](README.ko.md)

Launch any installed program from **Win+R** with a short name, like Spotlight on macOS.

```
Win+R → sl → Enter   # opens Slack
```

## Download

Get the zip from [Releases](../../releases), extract it, and run `WinRAlias.exe`.
`win-x64` includes the .NET runtime. `win-x64-framework` is smaller but needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

## Usage

1. Search for a program → **Enter**
2. Type an alias → **Enter**
3. **Win+R** → type the alias → **Enter**

Manage, edit, or delete aliases in the **등록된 alias** tab. (The UI is in Korean.)

## How it works

The app writes the program path to the registry key that the Run dialog looks up:

```
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<alias>.exe
```

It finds programs from the Start menu's app list, including Store apps. Apps with no exe are launched through a small shortcut (`.lnk`).

- No admin rights, no network access, no background process.
- The previous value is backed up to `%LOCALAPPDATA%\WinRAlias\backup.jsonl` before any change.
- Aliases keep working after you close the app.
- Works in the Run dialog only, not in cmd or PowerShell.

## Build

```bash
cd WinRAlias
dotnet run
```

Requires the .NET 8 SDK.

## License

[MIT](LICENSE)
