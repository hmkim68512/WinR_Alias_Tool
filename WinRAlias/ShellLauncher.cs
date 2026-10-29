namespace WinRAlias;

/// <summary>
/// exe가 없는 대상(스토어/MSIX 앱, 제어판 등)용 런처 바로가기.
/// App Paths (기본값)에 .lnk를 넣어도 ShellExecute(Win+R)가 따라가서 실행하므로
/// explorer.exe shell:AppsFolder\&lt;AUMID&gt; 바로가기를 만들어 연결한다.
/// </summary>
public static class ShellLauncher
{
    public const string Prefix = @"shell:AppsFolder\";

    public static bool IsShellTarget(string? s) =>
        s != null && s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// HKCU alias는 사용자 폴더, HKLM alias는 Program Files.
    /// HKLM용을 ProgramData에 두면 일반 사용자가 같은 이름의 .lnk를 미리 만들어(소유자가 됨)
    /// 나중에 내용을 바꿔치기할 수 있음 → 관리자만 쓸 수 있는 Program Files를 사용.
    /// </summary>
    public static string Dir(AliasScope scope) =>
        Path.Combine(Environment.GetFolderPath(scope == AliasScope.CurrentUser
                         ? Environment.SpecialFolder.LocalApplicationData
                         : Environment.SpecialFolder.ProgramFiles),
                     "WinRAlias", "launchers");

    /// <summary>런처 .lnk를 만들고 그 경로를 반환 (같은 이름이 있으면 덮어씀)</summary>
    public static string Create(AliasScope scope, string aliasName, string shellTarget)
    {
        var dir = Dir(scope);
        Directory.CreateDirectory(dir);
        var lnk = Path.Combine(dir, aliasName + ".lnk");
        // 덮어쓰지 않고 새로 만듦 → 파일 소유자/권한이 항상 지금 실행 중인 계정 기준
        if (File.Exists(lnk)) File.Delete(lnk);

        var t = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell을 사용할 수 없어요.");
        dynamic shell = Activator.CreateInstance(t)!;
        dynamic sc = shell.CreateShortcut(lnk);
        sc.TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        sc.Arguments = shellTarget.Contains(' ') ? $"\"{shellTarget}\"" : shellTarget;
        sc.Save();
        return lnk;
    }

    /// <summary>이 앱이 만든 런처 폴더 안의 .lnk인지</summary>
    public static bool IsOwned(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return false;
        var dir = Path.GetDirectoryName(path);
        return string.Equals(dir, Dir(AliasScope.CurrentUser), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(dir, Dir(AliasScope.LocalMachine), StringComparison.OrdinalIgnoreCase);
    }

    public static void DeleteIfOwned(string? path)
    {
        if (!IsOwned(path)) return;
        try { File.Delete(path!); }
        catch { /* 사용 중이거나 권한 없음 → 남겨둬도 동작엔 지장 없음 */ }
    }
}
