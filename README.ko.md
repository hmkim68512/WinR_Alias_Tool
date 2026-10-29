# WinR Alias

[English](README.md) | **한국어**

**Win+R(실행 창)** 에 짧은 이름만 입력하면 설치된 프로그램이 바로 실행되게 해 주는 도구예요.
macOS의 Spotlight 같은 경험을, Windows가 원래 가지고 있는 기능으로 만들어요.

```
Win+R → sl → Enter        # Slack 실행
Win+R → putty → Enter     # PuTTY 실행
```

- **시작 메뉴에 있는 앱을 전부** 찾아요. 일반 데스크톱 앱, Microsoft Store(MSIX) 앱, Slack·Discord 같은 Squirrel 설치 앱 모두요.
- 검색 중심 UI: **이름 입력 → Enter → alias 입력 → Enter** 면 등록이 끝나요.
- 상주 프로세스도, PATH 수정도, 관리자 권한도 필요 없어요.

---

## 시작하기

### 다운로드

[Releases](../../releases)에서 zip을 받아 압축을 풀고 `WinRAlias.exe`를 실행하면 돼요. 설치 과정은 없어요.

| 파일 | 크기 | 비고 |
|---|---|---|
| `WinRAlias-vX.Y.Z-win-x64.zip` | 약 70MB | .NET 런타임 포함. Windows 10/11이면 바로 실행돼요 |
| `WinRAlias-vX.Y.Z-win-x64-framework.zip` | 1MB 미만 | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)이 필요해요 |

> 코드 서명이 안 된 exe라서 처음 실행할 때 Windows SmartScreen 경고가 뜰 수 있어요.
> **추가 정보 → 실행**을 누르거나, 아래 방법으로 소스에서 직접 빌드하세요.

### 소스에서 빌드

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)가 필요해요 (`winget install Microsoft.DotNet.SDK.8`).

```bash
git clone https://github.com/<your-account>/WinRAlias.git
cd WinRAlias/WinRAlias
dotnet run
```

exe 하나로 빌드하기:

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

결과물: `bin/Release/net8.0-windows/win-x64/publish/WinRAlias.exe`

---

## 사용법

### alias 등록

1. 앱을 실행하면 검색창에 커서가 있어요. 프로그램 이름을 입력하세요. (예: `slack`)
2. **Enter**를 누르면 오른쪽 alias 입력칸으로 이동해요. 추천 이름이 미리 채워져 있어요.
3. 원하는 이름(예: `sl`)으로 고치고 **Enter**를 누르세요.
4. **Win+R** → `sl` → **Enter** 하면 실행돼요.

입력칸 아래 안내 문구가 이미 쓰이는 이름인지, `PATH`에 있는 프로그램과 겹치는지를 바로 알려줘요.

### 단축키

| 키 | 동작 |
|---|---|
| `Ctrl+F` / `F3` | 검색창으로 이동 |
| 그냥 타이핑 | 목록에서 글자를 치면 검색창에 입력돼요 |
| `Enter` (검색창 / 목록) | 선택한 프로그램의 alias 입력칸으로 |
| `Enter` (alias 입력칸) | 등록 |
| `Esc` | 검색어 지우기 / 목록으로 돌아가기 |
| `F5` | 설치된 프로그램 다시 스캔 |
| `Ctrl+N` | 경로를 직접 지정해서 새 alias 만들기 |
| `Enter` / `Delete` (alias 탭) | 선택한 alias 수정 / 삭제 |

### alias 관리

**등록된 alias** 탭에는 실행 창에서 쓰이는 alias가 모두 나와요. 다른 프로그램이 등록한 것도 포함돼요.

| 색 | 의미 |
|---|---|
| 초록 | 이 앱으로 만든 alias |
| 검정 | 다른 프로그램이 등록한 alias (수정하거나 지울 때 주의하세요) |
| 빨강 | 대상 파일이 더 이상 없음 |
| 회색 | HKCU에 있는 같은 이름에 가려짐 |

**고급 설정으로 추가…** 에서는 작업 폴더와 저장 위치(현재 사용자 / 모든 사용자)를 정할 수 있어요.
모든 사용자 범위는 관리자로 실행해야 쓸 수 있어요.

---

## 작동 원리

### App Paths 레지스트리

실행 창에 이름을 입력하면 Windows(`ShellExecuteEx`)는 그 이름으로 된 레지스트리 키를 찾아요.

```
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<alias>.exe   (현재 사용자)
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<alias>.exe   (모든 사용자)
```

키가 있으면 `(기본값)`에 적힌 경로를 실행해요. Windows 95 때부터 있던 기능이고, 설치 프로그램들이
`chrome`, `excel` 같은 이름을 등록할 때도 이걸 써요. WinR Alias는 이 키를 대신 만들고 고쳐 주는 도구예요.

```
HKCU\...\App Paths\sl.exe
  (기본값)     = C:\Users\me\AppData\Local\Microsoft\WindowsApps\Slack.exe
  Path         = (선택) 작업 폴더
  ManagedBy    = WinRAlias                  ← 이 앱이 만든 키 표시
  LaunchTarget = shell:AppsFolder\...       ← 런처를 쓰는 alias에만 (아래 참고)
```

Windows 기본 기능이라서 WinR Alias를 꺼도, 심지어 지워도 등록한 alias는 계속 동작해요.

### 설치된 프로그램 찾기

| 순서 | 출처 | 비고 |
|---|---|---|
| 1 | `shell:AppsFolder` | 시작 메뉴 "모든 앱"과 같은 목록. 스토어 앱 포함 |
| 2 | 시작 메뉴 `.lnk` 바로가기 | 바로가기 인자 확인, 1번의 보조 |
| 3 | Uninstall 레지스트리 (`DisplayIcon`) | 시작 메뉴 바로가기가 없는 앱 보조 |

앱 종류에 따라 alias가 가리키는 대상이 달라요.

| 앱 종류 | alias 대상 |
|---|---|
| 일반 데스크톱 앱 | `.exe` 경로 |
| Squirrel 앱 (`Update.exe --processStart X`) | 설치 폴더의 `X` (업데이트돼도 경로가 안 바뀜) |
| 실행 별칭이 있는 스토어 앱 (Slack, Spotify, Teams…) | `%LOCALAPPDATA%\Microsoft\WindowsApps\<이름>.exe` |
| exe가 없는 스토어 앱 (계산기…), 인자가 필요한 바로가기 (Git Bash, 크롬 PWA…) | 런처 바로가기 (아래 참고) |

**런처 바로가기.** App Paths에는 파일 경로만 넣을 수 있고 실행 인자는 넣을 수 없어요.
대신 `ShellExecute`가 `.lnk` 파일을 따라가서 실행하기 때문에,
`explorer.exe shell:AppsFolder\<앱ID>`를 실행하는 `%LOCALAPPDATA%\WinRAlias\launchers\<alias>.lnk`를 만들고
그 경로를 키에 넣어요. `shell:AppsFolder`를 통한 실행은 시작 메뉴가 앱을 실행하는 방식과 같아요.

### 이 앱이 만드는 파일

모두 `%LOCALAPPDATA%\WinRAlias\` 아래에 있고, 지워도 괜찮아요.

| 파일 | 용도 |
|---|---|
| `backup.jsonl` | alias를 덮어쓰거나 지우기 직전의 값을 한 줄씩 기록 (복원용) |
| `launchers\*.lnk` | 런처 바로가기 (위 참고). 모든 사용자용은 `C:\Program Files\WinRAlias\launchers` |
| `apps-cache.json` | 마지막 스캔 결과. 시작할 때 먼저 보여주고 백그라운드에서 다시 스캔해요 (3~8초) |
| `icons\*.png` | 아이콘 캐시 |

---

## 보안 / 개인정보

- **네트워크 통신과 사용 통계 수집이 전혀 없어요.** 인터넷에 연결하지 않아요.
- **기본적으로 관리자 권한을 쓰지 않아요.** alias는 `HKCU`에 저장돼서 내 계정에만 적용돼요.
  다른 사용자에게는 영향이 없고, 시스템 파일과 `PATH`도 건드리지 않아요.
- **요청한 것만 바꿔요.** alias를 등록·수정·삭제할 때만 레지스트리를 바꾸고, 바꾸기 전에 `backup.jsonl`에 백업해요.
- **이름 충돌을 알려줘요.** `PATH`에 있는 프로그램이나 기존 alias를 가리게 되면 저장하기 전에 경고해요.
- **관리자 모드**(`관리자로 재실행`)는 모든 사용자(`HKLM`) alias를 다룰 때만 필요해요.
  - 앱에서 실행하는 프로그램("실행", "실행 테스트", "파일 위치 열기")은 바탕화면 셸에게 맡겨서
    관리자 권한을 물려받지 않고 **일반 권한**으로 실행돼요.
  - 모든 사용자용 런처 바로가기는 관리자만 쓸 수 있는 `C:\Program Files\WinRAlias\launchers`에 저장돼서
    다른 사용자가 바꿔치기할 수 없어요.

> 참고: App Paths는 잘 알려진 Windows 기능이라 악성 프로그램이 악용하기도 해요.
> 다만 내 계정으로 실행되는 프로그램이면 원래 이 키를 쓸 수 있어서, 이 도구 때문에 새로운 구멍이 생기지는 않아요.
> 오히려 등록된 alias를 한눈에 볼 수 있으니, alias 탭에서 모르는 항목이 있는지 가끔 확인해 보세요.

### 삭제 / 정리

1. alias 탭에서 내가 만든 alias를 지우거나, PowerShell로 이 앱이 만든 키를 한 번에 지울 수 있어요.

   ```powershell
   Get-ChildItem 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths' |
     Where-Object { (Get-ItemProperty $_.PSPath).ManagedBy -eq 'WinRAlias' } |
     Remove-Item -Recurse
   ```

2. `%LOCALAPPDATA%\WinRAlias\` 폴더와 `WinRAlias.exe`를 지우세요.

---

## 한계

- 실행 창(그리고 `ShellExecute`를 쓰는 곳)에서만 동작해요. `cmd`나 PowerShell에서는 **안 돼요**.
- 실행 인자를 넣는 UI는 아직 없어요. 필요한 인자를 넣은 `.lnk`를 만들어서 대상으로 지정하면 돼요.
- `backup.jsonl` 복원 UI는 아직 없어서 직접 복원해야 해요.
- UI는 아직 한국어만 지원해요.

## 프로젝트 구조

| 파일 | 역할 |
|---|---|
| `AppScanner.cs` | 설치 프로그램 탐색 + 스캔 결과 캐시 |
| `AppPathsRegistry.cs` | App Paths 읽기/쓰기/삭제, 이름 검증, PATH 충돌 검사, 백업 |
| `ShellLauncher.cs` | exe 없는 앱용 런처 `.lnk` |
| `IconLoader.cs`, `NativeShell.cs` | 셸 아이콘 비동기 로딩, Win32 호출 |
| `MainForm.cs`, `AliasDialog.cs`, `Ui.cs` | WinForms UI (디자이너 없이 코드로 작성) |
| `Models.cs` | 데이터 모델, 경로 헬퍼 |

## 라이선스

[MIT](LICENSE)
