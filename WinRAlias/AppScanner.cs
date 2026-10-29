using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinRAlias;

/// <summary>
/// 설치된 프로그램 스캔. 시작 메뉴 "모든 앱"과 같은 목록을 만드는 게 목표.
///  1) shell:AppsFolder — 시작 메뉴 앱 목록 그대로 (스토어/MSIX 앱 포함)
///  2) 시작 메뉴 .lnk — 바로가기 인자 확인 + AppsFolder가 실패했을 때 보조
///  3) Uninstall 레지스트리의 DisplayIcon — 바로가기 없는 앱 보조
/// Shell.Application COM을 쓰므로 STA 스레드에서 호출할 것 (NativeShell.RunSta).
/// </summary>
public static class AppScanner
{
    private static readonly string[] JunkNameWords =
        { "uninstall", "unins0", "제거", "삭제", "setup", "installer", "repair", "crashpad", "crash report" };

    private static readonly Regex ProcessStartArg =
        new(@"--processStart(?:=|\s+)""?([^""\s]+)", RegexOptions.IgnoreCase);

    private sealed record LinkInfo(string Name, string LnkPath, string Target, string Args);

    public static List<InstalledApp> Scan()
    {
        // AppsFolder 열거가 2~3초 걸려서 바로가기 읽기는 다른 STA 스레드에서 동시에
        var linksTask = NativeShell.RunSta(ReadStartMenuLinks);
        var appsFolder = ReadAppsFolder();
        var links = linksTask.GetAwaiter().GetResult();

        // 인자가 있어야 제대로 뜨는 exe (크롬 PWA 등). 인자 없는 바로가기가 하나라도 있으면 제외
        var needArgs = new HashSet<string>(
            links.Where(l => l.Args.Trim().Length > 0 && FixSquirrel(l.Target, l.Args) == l.Target)
                 .Select(l => l.Target),
            StringComparer.OrdinalIgnoreCase);
        needArgs.ExceptWith(links.Where(l => l.Args.Trim().Length == 0).Select(l => l.Target));

        var result = new List<InstalledApp>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        void Add((InstalledApp App, string? Exe) x, bool dedupeByName)
        {
            if (seenTargets.Contains(x.App.Target) || (x.Exe != null && seenTargets.Contains(x.Exe))) return;
            if (dedupeByName && seenNames.Contains(x.App.DisplayName)) return;
            seenTargets.Add(x.App.Target);
            if (x.Exe != null) seenTargets.Add(x.Exe);
            seenNames.Add(x.App.DisplayName);
            result.Add(x.App);
        }

        foreach (var x in ClassifyAppsFolder(appsFolder, needArgs)) Add(x, dedupeByName: false);
        foreach (var x in FromStartMenuLinks(links, needArgs)) Add(x, dedupeByName: true);
        foreach (var x in ScanUninstallKeys()) Add(x, dedupeByName: true);

        return result.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ───────────────── 캐시 ─────────────────
    // 스캔이 몇 초 걸려서 시작할 때는 지난 결과를 먼저 보여주고 백그라운드에서 갱신

    private static string CacheFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "WinRAlias", "apps-cache.json");

    public static List<InstalledApp>? LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            return JsonSerializer.Deserialize<List<InstalledApp>>(File.ReadAllText(CacheFile));
        }
        catch { return null; }
    }

    public static void SaveCache(List<InstalledApp> apps)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(apps));
        }
        catch { /* 캐시는 없어도 그만 */ }
    }

    // ───────────────── shell:AppsFolder ─────────────────

    private static List<(string Name, string Path, string Link)> ReadAppsFolder()
    {
        var raw = new List<(string Name, string Path, string Link)>();
        try
        {
            dynamic? shell = CreateCom("Shell.Application");
            dynamic? folder = shell?.NameSpace("shell:AppsFolder");
            if (folder == null) return raw;

            dynamic items = folder.Items();
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic item = items.Item(i);
                    string name = item.Name ?? "";
                    string path = item.Path ?? "";  // AUMID 또는 {KnownFolder}\경로
                    string link = "";
                    try { link = item.ExtendedProperty("System.Link.TargetParsingPath") as string ?? ""; }
                    catch { }
                    if (name.Length > 0 && path.Length > 0 && !path.Contains("://")) raw.Add((name, path, link));
                }
                catch { }
            }
        }
        catch { }
        return raw;
    }

    private static List<(InstalledApp, string?)> ClassifyAppsFolder(
        List<(string Name, string Path, string Link)> raw, HashSet<string> needArgs)
    {
        // 패키지 하나에 앱이 하나뿐이면 실행 별칭을 그 앱 것으로 봐도 됨
        var appsPerPackage = raw.Where(r => IsStoreAppId(r.Path))
                                .GroupBy(r => r.Path[..r.Path.IndexOf('!')], StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var result = new List<(InstalledApp, string?)>();
        foreach (var (name, path, link) in raw)
        {
            if (IsJunkName(name)) continue;
            var shellTarget = ShellLauncher.Prefix + path;

            InstalledApp Make(string target, AppKind kind, string? packageFamily = null) => new()
            {
                DisplayName = name, Target = target, Kind = kind, Source = "시작 메뉴", IconSource = shellTarget,
                PackageFamily = packageFamily
            };

            // 1) 바로가기 대상 exe가 있는 데스크톱 앱 (Claude, PuTTY처럼 AUMID가 지정된 앱 포함)
            if (link.Length > 0)
            {
                var exe = FixSquirrel(link, "");
                if (IsUsableExe(exe))
                {
                    // 인자가 필요한 바로가기는 exe만으로 못 띄우니 AppsFolder 경유
                    result.Add((Make(needArgs.Contains(exe) ? shellTarget : exe, AppKind.Desktop), exe));
                    continue;
                }
                if (!link.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue; // 문서, 웹 링크 등
                // exe인데 못 쓰는 경우(Squirrel Update.exe 등) → 아래 AppsFolder 경유로
            }
            else if (ResolveParsingPath(path) is string file)
            {
                // {KnownFolder}\폴더\x.exe 형태. exe가 아니면(설명서, .url) 건너뜀
                if (IsUsableExe(file)) result.Add((Make(file, AppKind.Desktop), file));
                continue;
            }

            // 2) 스토어(MSIX) 앱: 실행 별칭 exe가 있으면 그걸로, 없으면 AppsFolder 경유
            if (IsStoreAppId(path))
            {
                var bang = path.IndexOf('!');
                var pfn = path[..bang];
                bool onlyApp = appsPerPackage[pfn] == 1;
                var alias = FindExecutionAlias(pfn, path[(bang + 1)..], name, onlyApp);
                result.Add((Make(alias ?? shellTarget, AppKind.Store, onlyApp ? pfn : null), null));
                continue;
            }

            // 3) 그 밖의 셸 항목 (exe를 못 찾은 데스크톱 앱, 제어판 등)
            result.Add((Make(shellTarget, link.Length > 0 ? AppKind.Desktop : AppKind.Other), null));
        }
        return result;
    }

    /// <summary>"PackageFamilyName!AppId" 형태인지</summary>
    private static bool IsStoreAppId(string path) =>
        path.IndexOf('!') > 0 && !path.Contains('\\') && !path.Contains(':');

    /// <summary>"{GUID}\sub\x.exe" 또는 절대 경로 → 파일 경로. 둘 다 아니면 null</summary>
    private static string? ResolveParsingPath(string path)
    {
        if (path.Length > 38 && path[0] == '{' && path[37] == '}' && Guid.TryParse(path[..38], out var id))
        {
            var root = NativeShell.KnownFolderPath(id);
            return root == null ? null : root + path[38..];
        }
        return Path.IsPathFullyQualified(path) ? path : null;
    }

    /// <summary>
    /// 스토어 앱의 실행 별칭(%LOCALAPPDATA%\Microsoft\WindowsApps\x.exe).
    /// 패키지 폴더에 있는 별칭 중 AppId/표시 이름과 맞는 것, 또는 앱이 하나뿐인 패키지의 유일한 별칭.
    /// </summary>
    private static string? FindExecutionAlias(string pfn, string appId, string displayName, bool onlyAppInPackage)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "Microsoft", "WindowsApps");
        string[] exes;
        try { exes = Directory.GetFiles(Path.Combine(root, pfn), "*.exe"); }
        catch { return null; }
        if (exes.Length == 0) return null;

        string Key(string f) => Simplify(Path.GetFileNameWithoutExtension(f));
        var pick = exes.FirstOrDefault(e => Key(e) == Simplify(appId))
                   ?? exes.FirstOrDefault(e => Key(e) == Simplify(displayName))
                   ?? (onlyAppInPackage && exes.Length == 1 ? exes[0] : null);
        if (pick == null) return null;

        // PATH에 있는 최상위 별칭이 있으면 그쪽 (보기 쉬움)
        var top = Path.Combine(root, Path.GetFileName(pick));
        return File.Exists(top) ? top : pick;
    }

    private static string Simplify(string s) =>
        new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // ───────────────── 시작 메뉴 .lnk ─────────────────

    private static List<LinkInfo> ReadStartMenuLinks()
    {
        var list = new List<LinkInfo>();
        dynamic? shell = CreateCom("WScript.Shell");
        if (shell == null) return list;

        // 한국어 Windows의 "프로그램" 같은 호환용 정션은 접근 거부라서
        // 기본 옵션으로 재귀하면 예외가 나서 폴더 전체를 놓친다 → 정션 건너뛰고 접근 불가 무시
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };

        foreach (var dir in dirs.Where(Directory.Exists))
        {
            List<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.lnk", options).ToList(); }
            catch { continue; }

            foreach (var lnk in files)
            {
                try
                {
                    dynamic sc = shell.CreateShortcut(lnk);
                    string target = sc.TargetPath ?? "";
                    string args = sc.Arguments ?? "";
                    list.Add(new LinkInfo(Path.GetFileNameWithoutExtension(lnk), lnk, target, args));
                }
                catch { }
            }
        }
        return list;
    }

    private static IEnumerable<(InstalledApp, string?)> FromStartMenuLinks(List<LinkInfo> links, HashSet<string> needArgs)
    {
        foreach (var l in links)
        {
            if (IsJunkName(l.Name)) continue;
            var exe = FixSquirrel(l.Target, l.Args);
            if (!IsUsableExe(exe)) continue;

            // 인자가 필요한 바로가기는 .lnk 자체를 대상으로 (App Paths가 .lnk도 실행함)
            var target = l.Args.Trim().Length > 0 && needArgs.Contains(exe) ? l.LnkPath : exe;
            yield return (new InstalledApp { DisplayName = l.Name, Target = target, Kind = AppKind.Desktop, Source = "시작 메뉴" }, exe);
        }
    }

    // ───────────────── Uninstall 레지스트리 ─────────────────

    private static IEnumerable<(InstalledApp, string?)> ScanUninstallKeys()
    {
        const string sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var roots = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser,  RegistryView.Registry64),
        };

        foreach (var (hive, view) in roots)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(sub);
            if (uninstall == null) continue;

            foreach (var name in uninstall.GetSubKeyNames())
            {
                InstalledApp? app = null;
                try
                {
                    using var k = uninstall.OpenSubKey(name);
                    var display = k?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(display) || IsJunkName(display)) continue;
                    if ((k!.GetValue("SystemComponent") as int?) == 1) continue;

                    // DisplayIcon: "C:\x\app.exe,0" 형태
                    var icon = k.GetValue("DisplayIcon") as string;
                    var exe = icon == null ? "" : FixSquirrel(PathUtil.Normalize(icon.Split(',')[0]), "");
                    if (IsUsableExe(exe))
                        app = new InstalledApp { DisplayName = display, Target = exe, Kind = AppKind.Desktop, Source = "설치 목록" };
                }
                catch { }

                if (app != null) yield return (app, app.Target);
            }
        }
    }

    // ───────────────── 공통 ─────────────────

    /// <summary>
    /// Squirrel 설치 앱(Slack/Discord 데스크톱판 등)은 바로가기가 "Update.exe --processStart X".
    /// 업데이트 때 바뀌는 app-x.y.z\ 경로 대신 설치 루트의 X(항상 최신 버전을 띄우는 스텁)를 쓴다.
    /// </summary>
    private static string FixSquirrel(string target, string args)
    {
        if (string.IsNullOrEmpty(target)) return target;
        var dir = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(dir)) return target;
        var file = Path.GetFileName(target);

        if (file.Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
        {
            var m = ProcessStartArg.Match(args);
            if (m.Success && File.Exists(Path.Combine(dir, m.Groups[1].Value)))
                return Path.Combine(dir, m.Groups[1].Value);

            // 인자를 모르면 루트에 Update.exe 말고 exe가 하나뿐일 때 그걸로
            try
            {
                var others = Directory.GetFiles(dir, "*.exe")
                    .Where(f => !Path.GetFileName(f).Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (others.Length == 1) return others[0];
            }
            catch { }
            return target;
        }

        // ...\app-1.2.3\x.exe → ...\x.exe (루트 스텁이 있을 때만)
        if (Path.GetFileName(dir).StartsWith("app-", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(dir);
            if (parent != null && File.Exists(Path.Combine(parent, "Update.exe")) && File.Exists(Path.Combine(parent, file)))
                return Path.Combine(parent, file);
        }
        return target;
    }

    private static bool IsUsableExe(string path) =>
        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && PathUtil.FileExists(path) && !IsJunkExe(path);

    private static bool IsJunkName(string name)
    {
        var lower = name.ToLowerInvariant();
        return JunkNameWords.Any(lower.Contains);
    }

    private static bool IsJunkExe(string path)
    {
        var f = Path.GetFileName(path).ToLowerInvariant();
        return f == "update.exe" || f.StartsWith("unins") || f.Contains("setup") || f.Contains("installer") ||
               f.Contains("crashpad") ||
               path.Contains(@"\Windows\Installer\", StringComparison.OrdinalIgnoreCase); // MSI 광고 바로가기 아이콘
    }

    private static dynamic? CreateCom(string progId)
    {
        try
        {
            var t = Type.GetTypeFromProgID(progId);
            return t == null ? null : Activator.CreateInstance(t);
        }
        catch { return null; }
    }
}
