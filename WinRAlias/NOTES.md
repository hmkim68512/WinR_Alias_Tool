# WinR Alias — 작업 노트 (인계용)

## 목표

Win+R(실행 창)에서 쓸 alias를 GUI로 관리하는 Windows 프로그램.
- 설치된 프로그램 목록에서 골라 alias 지정
- 이미 등록된 alias(다른 프로그램이 만든 것 포함)도 조회/수정/삭제

## 핵심 결정사항

### 메커니즘: App Paths 레지스트리 (PATH 방식 아님)
- Win+R(ShellExecute)은 입력값을 다음 두 곳에서 찾는다 (OR 관계, 하나만 있어도 실행됨)
  1. `...\CurrentVersion\App Paths\<이름>.exe` 키 (HKCU, HKLM)
  2. PATH 환경 변수 폴더들
- App Paths를 선택한 이유
  - alias 1개 = 키 1개 → exe마다 다른 이름 붙이기 쉬움 (PATH는 폴더 단위라 alias에 안 맞음)
  - HKCU에 쓰면 관리자 권한 불필요
  - 삭제 = 키 삭제, 시스템 PATH 오염 없음
- 키 구조
  ```
  HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\<alias>.exe
    (기본값)  = 실행 파일 경로   (%VAR% 포함 시 REG_EXPAND_SZ)
    Path      = 작업 경로 (선택)
    ManagedBy = "WinRAlias"     ← 이 앱이 만든 키 표시
  ```
- 레지스트리는 `RegistryView.Registry64`로 연다 (64비트 explorer가 보는 뷰).

### 기존 alias 편집 정책
- HKCU 항목: 바로 수정/이름 변경/삭제. 다른 프로그램이 만든 것(ManagedBy 없음)이면 경고.
- HKLM 항목 + 일반 권한: 두 가지 선택지
  - HKCU에 같은 이름으로 덮어쓰기 (원본 유지)
  - 관리자로 재실행(`Verb = "runas"`) 후 원본 직접 수정
- 덮어쓰기/삭제 전 값은 항상 `%LOCALAPPDATA%\WinRAlias\backup.jsonl`에 한 줄씩 백업.
- 같은 이름이 HKCU, HKLM 둘 다 있으면 HKLM 쪽을 "HKCU에 가려짐"으로 표시.
  - ⚠ HKCU가 HKLM보다 우선한다는 건 전제일 뿐 **미검증**. 실제 PC에서 확인 필요.

### 검증 규칙
- alias에 공백, `\ / : * ? " < > |` 불가 (Win+R은 공백 뒤를 인자로 해석).
- PATH에 같은 이름 exe가 있으면 경고 (Win+R은 alias가 이기고, cmd는 PATH가 이김).
- 대상 파일이 없으면 경고 후 저장 여부 확인.

## 구조

C# / .NET 8 / WinForms (디자이너 없이 코드로 UI 구성)

| 파일 | 역할 |
|---|---|
| `Models.cs` | `AliasEntry`, `InstalledApp`, `AliasScope`, `AppKind`, `PathUtil`(따옴표 제거 + 환경변수 확장, 패키지 이름 추출) |
| `AppPathsRegistry.cs` | App Paths 읽기/쓰기/삭제, 이름 검증, PATH 충돌 검사, 백업, 관리자 여부, 런처 정리 |
| `AppScanner.cs` | 설치 프로그램 스캔 (아래 "스캐너" 참고) + 결과 캐시 |
| `ShellLauncher.cs` | exe 없는 대상용 런처 `.lnk` (`explorer.exe shell:AppsFolder\ID`) |
| `IconLoader.cs` / `NativeShell.cs` | 셸 아이콘 비동기 로딩 / Win32 P/Invoke |
| `Ui.cs` | 공통 폰트·색상, 탐색기 테마 ListView (열 비율 자동 맞춤) |
| `AliasDialog.cs` | 추가/수정 창 (이름, 대상, 작업 경로, 범위, 실시간 경고) |
| `MainForm.cs` | 상단 공용 검색 / 탭1: 프로그램 목록 + 오른쪽 상세·빠른 등록 / 탭2: alias 목록(필터, 상태 색상) |
| `app.manifest` | `asInvoker` (기본 일반 권한) |

## 현재 상태

- [x] 설계 + MVP 코드 작성
- [x] 전체 빌드 (경고 0) + 실제 Windows 11(한국어, 120 DPI)에서 화면 확인
- [x] 스캐너 재작성: PuTTY, Claude, Slack, Discord, 계산기 등 시작 메뉴 앱 전부 잡힘
- [x] 스토어 앱 런처(.lnk) 생성 → ShellExecute 실행 → 이름 변경 → 삭제 e2e 확인
- [ ] Win+R 창에서 직접 입력해 보는 수동 테스트 (ShellExecute 경로로는 확인함)

## 스캐너 — 원인과 해결 (2026-10)

**PuTTY/Claude가 안 잡히던 진짜 원인**: 한국어 Windows 시작 메뉴 폴더 안에 있는 `프로그램` 호환용
정션(접근 거부)을 `Directory.EnumerateFiles(..., AllDirectories)`가 만나면 예외가 나고,
`catch { continue; }` 때문에 **시작 메뉴 전체 스캔이 통째로 버려졌다.** 그래서 Uninstall 레지스트리 결과만 남았음.
→ `EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = ReparsePoint }`로 해결.

**Slack**: 스토어(MSIX)판. `WindowsApps\Slack.exe` 실행 별칭으로 연결.

현재 구조:
1. `shell:AppsFolder` (Shell.Application COM) — 시작 메뉴 "모든 앱"과 동일한 목록.
   - 데스크톱 앱: `System.Link.TargetParsingPath`로 exe 경로
   - `{KnownFolderGUID}\...` 경로: `SHGetKnownFolderPath`로 풀기
   - 스토어 앱(`PFN!AppId`): `%LOCALAPPDATA%\Microsoft\WindowsApps\<PFN>\*.exe` 실행 별칭 중 AppId/이름이 맞는 것
   - 그 외 / 인자가 필요한 바로가기: `shell:AppsFolder\<ID>` → 저장 시 런처 `.lnk` 생성
2. 시작 메뉴 `.lnk` (WScript.Shell) — 인자 확인(`needArgs`), Squirrel `--processStart` 처리
3. Uninstall 레지스트리 — 보조. 이름이 이미 있으면 건너뜀 (버전 경로 중복 방지)

- `folder.Items()`가 매번 2~3초 걸림 (셸 내부). → 스캔 결과를 `apps-cache.json`에 캐시해서 시작 시 즉시 표시, 백그라운드 재스캔.
- 셸 아이콘 추출은 개당 수십~600ms (스토어 앱이 느림). → `IconLoader`: STA 3스레드 병렬 + 화면에 보이는 것 우선 + PNG 디스크 캐시.
- `IShellItemImageFactory` 비트맵은 bottom-up이라 `Stride`가 음수 → 줄 단위로 읽어야 함.
- 스토어 앱이 직접 등록한 App Paths(예: HKCU `Slack.exe` → `Program Files\WindowsApps\<PackageFullName>\...`)는
  패키지 패밀리 이름으로 앱과 매칭 (앱이 하나뿐인 패키지만). 안 그러면 "다른 대상"으로 보고 덮어쓰기를 권하게 됨.
- App Paths (기본값)에 `.lnk`를 넣어도 ShellExecute가 실행함 — 실측 확인.

## 그 밖의 한계 / TODO 후보

- [ ] 실행 인자 UI (예: 특정 폴더로 `code` 열기) — 런처 `.lnk` 구조가 있으니 인자 칸만 추가하면 됨
- [ ] cmd/PowerShell 지원 — App Paths는 ShellExecute 전용. alias 전용 폴더를 PATH에 1번 등록 + `.bat` 생성 방식 고려 (콘솔창 깜빡임 있음)
- [ ] 백업 복원 UI (현재는 `backup.jsonl` 보고 수동 복원)
- [ ] HKCU vs HKLM 우선순위 실측
- [ ] 아이콘 캐시 무효화 (현재는 키(경로/앱 ID)가 같으면 계속 재사용)
- [ ] 다크 모드 (WinForms .NET 8은 미지원, .NET 9+ 실험 기능)

## 빌드

```
winget install Microsoft.DotNet.SDK.8
dotnet run
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```
