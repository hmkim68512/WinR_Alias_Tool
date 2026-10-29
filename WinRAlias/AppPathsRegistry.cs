using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace WinRAlias;

/// <summary>
/// HKCU / HKLM 의 App Paths 읽기·쓰기.
/// Win+R(ShellExecute)은 "이름.exe" 키를 찾아 (기본값) 경로를 실행한다.
/// </summary>
public static class AppPathsRegistry
{
    public const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string ManagedByValue = "ManagedBy";
    private const string ManagedByTag = "WinRAlias";
    private const string LaunchTargetValue = "LaunchTarget"; // 런처 .lnk가 가리키는 shell:AppsFolder\AUMID

    private static readonly char[] InvalidChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|', ' ' };

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static RegistryKey OpenRoot(AliasScope scope) =>
        RegistryKey.OpenBaseKey(
            scope == AliasScope.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
            RegistryView.Registry64); // 64비트 explorer가 보는 뷰

    // ───────────────── 읽기 ─────────────────

    public static List<AliasEntry> LoadAll()
    {
        var user = Load(AliasScope.CurrentUser);
        var machine = Load(AliasScope.LocalMachine);

        // HKCU에 같은 이름이 있으면 HKLM 항목은 가려진 것으로 표시
        var userNames = new HashSet<string>(user.Select(a => a.KeyName), StringComparer.OrdinalIgnoreCase);
        foreach (var m in machine)
            m.ShadowedByUser = userNames.Contains(m.KeyName);

        return user.Concat(machine)
                   .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    public static List<AliasEntry> Load(AliasScope scope)
    {
        var result = new List<AliasEntry>();
        using var root = OpenRoot(scope);
        using var appPaths = root.OpenSubKey(AppPathsKey);
        if (appPaths == null) return result;

        foreach (var keyName in appPaths.GetSubKeyNames())
        {
            try
            {
                using var k = appPaths.OpenSubKey(keyName);
                if (k == null) continue;

                // DoNotExpandEnvironmentNames: 원문 그대로 보존(REG_EXPAND_SZ 유지 목적)
                var target = k.GetValue("", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                var path = k.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                var managed = k.GetValue(ManagedByValue) as string;
                var shellTarget = k.GetValue(LaunchTargetValue) as string;

                result.Add(new AliasEntry
                {
                    KeyName = keyName,
                    Name = StripExe(keyName),
                    RawTarget = target,
                    WorkingPath = path,
                    Scope = scope,
                    CreatedByThisApp = managed == ManagedByTag,
                    ShellTarget = ShellLauncher.IsShellTarget(shellTarget) ? shellTarget : null
                });
            }
            catch
            {
                // 권한 없는 키 등은 건너뜀
            }
        }
        return result;
    }

    // ───────────────── 쓰기 ─────────────────

    /// <summary>
    /// alias 생성/수정. oldKeyName이 있고 이름이 바뀌었으면 기존 키를 지운다(이름 변경).
    /// target이 shell:AppsFolder\... 이면 런처 .lnk를 만들어 그 경로를 저장.
    /// 덮어쓰기/삭제 전에는 항상 백업.
    /// </summary>
    public static void Save(AliasScope scope, string aliasName, string target,
                            string? workingPath, string? oldKeyName = null)
    {
        var keyName = ToKeyName(aliasName);

        using var root = OpenRoot(scope);
        using var appPaths = root.CreateSubKey(AppPathsKey, writable: true)
                             ?? throw new InvalidOperationException("App Paths 키를 열 수 없어요.");

        // 덮어쓸 대상이 이미 있으면 백업
        BackupIfExists(appPaths, scope, keyName);

        bool renamed = oldKeyName != null &&
                       !string.Equals(oldKeyName, keyName, StringComparison.OrdinalIgnoreCase);
        if (renamed)
            BackupIfExists(appPaths, scope, oldKeyName!);

        // 덮어써지거나 이름이 바뀌는 키가 쓰던 런처는 저장 후 정리
        var oldTargets = new[] { keyName, renamed ? oldKeyName : null }
            .Where(n => n != null).Select(n => ReadTarget(appPaths, n!)).ToList();

        string? shellTarget = null;
        if (ShellLauncher.IsShellTarget(target))
        {
            shellTarget = target;
            target = ShellLauncher.Create(scope, StripExe(keyName), target);
        }

        using (var k = appPaths.CreateSubKey(keyName, writable: true))
        {
            // 경로에 %VAR%가 있으면 REG_EXPAND_SZ로 저장
            var kind = target.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String;
            k.SetValue("", target, kind);

            if (!string.IsNullOrWhiteSpace(workingPath))
                k.SetValue("Path", workingPath,
                    workingPath.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String);
            else if (k.GetValue("Path") != null)
                k.DeleteValue("Path", false);

            if (shellTarget != null) k.SetValue(LaunchTargetValue, shellTarget);
            else if (k.GetValue(LaunchTargetValue) != null) k.DeleteValue(LaunchTargetValue, false);

            k.SetValue(ManagedByValue, ManagedByTag);
        }

        if (renamed)
            appPaths.DeleteSubKeyTree(oldKeyName!, throwOnMissingSubKey: false);

        foreach (var old in oldTargets)
            if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                ShellLauncher.DeleteIfOwned(old);
    }

    public static void Delete(AliasEntry entry)
    {
        using var root = OpenRoot(entry.Scope);
        using var appPaths = root.OpenSubKey(AppPathsKey, writable: true)
                             ?? throw new InvalidOperationException("App Paths 키를 열 수 없어요.");
        BackupIfExists(appPaths, entry.Scope, entry.KeyName);
        appPaths.DeleteSubKeyTree(entry.KeyName, throwOnMissingSubKey: false);
        ShellLauncher.DeleteIfOwned(entry.ResolvedTarget);
    }

    private static string? ReadTarget(RegistryKey appPaths, string keyName)
    {
        using var k = appPaths.OpenSubKey(keyName);
        return k == null ? null : PathUtil.Normalize(k.GetValue("") as string);
    }

    // ───────────────── 검증 ─────────────────

    /// <summary>문제 없으면 null, 있으면 에러 메시지</summary>
    public static string? ValidateName(string alias)
    {
        var n = StripExe(alias.Trim());
        if (n.Length == 0) return "alias를 입력해 주세요.";
        if (n.IndexOfAny(InvalidChars) >= 0)
            return "공백이나 \\ / : * ? \" < > | 는 쓸 수 없어요. (Win+R은 공백 뒤를 인자로 처리해요)";
        return null;
    }

    /// <summary>같은 이름의 exe가 PATH에 있으면 경로 반환 (App Paths가 우선이라 가려짐)</summary>
    public static string? FindInPath(string alias)
    {
        var file = ToKeyName(alias);
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in paths)
        {
            try
            {
                var full = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim()), file);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    public static string ToKeyName(string alias)
    {
        var n = StripExe(alias.Trim());
        return n + ".exe";
    }

    public static string StripExe(string s) =>
        s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;

    // ───────────────── 백업 ─────────────────

    public static string BackupFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "WinRAlias", "backup.jsonl");

    private static void BackupIfExists(RegistryKey appPaths, AliasScope scope, string keyName)
    {
        using var k = appPaths.OpenSubKey(keyName);
        if (k == null) return;

        var values = new Dictionary<string, string?>();
        foreach (var v in k.GetValueNames())
            values[v.Length == 0 ? "(Default)" : v] =
                k.GetValue(v, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();

        var record = new
        {
            time = DateTime.Now.ToString("s"),
            scope = scope.ToString(),
            key = keyName,
            values
        };

        Directory.CreateDirectory(Path.GetDirectoryName(BackupFile)!);
        File.AppendAllText(BackupFile, JsonSerializer.Serialize(record) + Environment.NewLine);
    }
}
