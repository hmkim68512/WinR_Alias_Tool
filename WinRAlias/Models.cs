using System.Text.Json.Serialization;

namespace WinRAlias;

/// <summary>App Paths 레지스트리가 어느 하이브에 있는지</summary>
public enum AliasScope
{
    CurrentUser,   // HKCU: 일반 권한으로 수정 가능
    LocalMachine   // HKLM: 관리자 권한 필요
}

/// <summary>App Paths 아래의 키 하나 = alias 하나</summary>
public sealed class AliasEntry
{
    /// <summary>Win+R에 입력하는 이름 (".exe" 뺀 형태)</summary>
    public required string Name { get; init; }

    /// <summary>레지스트리 키 이름 그대로 (예: "chrome.exe")</summary>
    public required string KeyName { get; init; }

    /// <summary>(기본값)에 들어있는 실행 파일 경로. 따옴표/환경변수 포함 가능</summary>
    public string RawTarget { get; init; } = "";

    /// <summary>"Path" 값 (작업 디렉터리 / 추가 DLL 탐색 경로)</summary>
    public string? WorkingPath { get; init; }

    public AliasScope Scope { get; init; }

    /// <summary>이 앱이 만든 alias인지 (ManagedBy 값으로 표시)</summary>
    public bool CreatedByThisApp { get; init; }

    /// <summary>스토어 앱 등 shell:AppsFolder\AUMID 대상. 이때 (기본값)은 런처 .lnk 경로</summary>
    public string? ShellTarget { get; init; }

    /// <summary>따옴표 제거 + 환경변수 확장한 실제 경로</summary>
    public string ResolvedTarget => PathUtil.Normalize(RawTarget);

    /// <summary>화면 표시 / 프로그램 매칭용 대상</summary>
    public string DisplayTarget => ShellTarget ?? RawTarget;
    public string EffectiveTarget => ShellTarget ?? ResolvedTarget;

    /// <summary>스토어 앱 패키지 폴더를 가리키면 그 패키지 패밀리 이름 (스토어 앱이 직접 등록한 alias 매칭용)</summary>
    public string? PackageFamily => PathUtil.PackageFamilyFromPath(ResolvedTarget);

    public bool TargetExists => PathUtil.FileExists(ResolvedTarget);

    /// <summary>HKCU와 HKLM에 같은 이름이 있을 때 HKLM 쪽은 가려짐</summary>
    public bool ShadowedByUser { get; set; }
}

public enum AppKind
{
    Desktop,  // 일반 exe
    Store,    // MSIX/스토어 앱
    Other     // 제어판 등 그 밖의 셸 항목
}

/// <summary>스캔해서 찾은 설치 프로그램</summary>
public sealed class InstalledApp
{
    public required string DisplayName { get; init; }

    /// <summary>App Paths에 넣을 대상: exe 경로, 바로가기(.lnk) 경로, 또는 shell:AppsFolder\AUMID</summary>
    public required string Target { get; init; }

    public AppKind Kind { get; init; }
    public required string Source { get; init; } // "시작 메뉴" / "설치 목록"

    /// <summary>아이콘 조회용 셸 이름 (없으면 Target)</summary>
    public string? IconSource { get; init; }

    /// <summary>앱이 하나뿐인 스토어 패키지의 패키지 패밀리 이름 (그 패키지를 가리키는 기존 alias 매칭용)</summary>
    public string? PackageFamily { get; init; }

    [JsonIgnore] public string IconKey => IconSource ?? Target;
    [JsonIgnore] public bool IsShellTarget => ShellLauncher.IsShellTarget(Target);
}

public static class PathUtil
{
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();

        // "C:\a b\x.exe" -args 같은 형태 → 첫 번째 따옴표 구간만
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            s = end > 0 ? s.Substring(1, end - 1) : s.Trim('"');
        }

        return Environment.ExpandEnvironmentVariables(s);
    }

    /// <summary>
    /// ...\WindowsApps\Name_Version_Arch_Res_PubId\... (설치 폴더) 또는
    /// ...\WindowsApps\Name_PubId\x.exe (실행 별칭 폴더) → "Name_PubId". 아니면 null
    /// </summary>
    public static string? PackageFamilyFromPath(string path)
    {
        const string marker = @"\WindowsApps\";
        var i = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var rest = path[(i + marker.Length)..];
        var slash = rest.IndexOf('\\');
        if (slash < 0) return null; // WindowsApps\x.exe 최상위 별칭은 패키지를 알 수 없음

        var parts = rest[..slash].Split('_');
        return parts.Length switch
        {
            5 => parts[0] + "_" + parts[4],
            2 => parts[0] + "_" + parts[1],
            _ => null
        };
    }

    public static bool FileExists(string path)
    {
        try { return !string.IsNullOrEmpty(path) && File.Exists(path); }
        catch { return false; }
    }
}
