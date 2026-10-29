# WinR Alias

[English](README.md) | **한국어**

macOS Spotlight처럼, **Win+R**에 짧은 이름만 입력하면 설치된 프로그램이 바로 실행돼요.

```
Win+R → sl → Enter   # Slack 실행
```

## 다운로드

[Releases](../../releases)에서 zip을 받아 압축을 풀고 `WinRAlias.exe`를 실행하세요.
`win-x64`는 .NET 런타임이 포함돼 있고, `win-x64-framework`는 용량이 작은 대신 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)이 필요해요.

## 사용법

1. 프로그램 검색 → **Enter**
2. alias 입력 → **Enter**
3. **Win+R** → alias 입력 → **Enter**

등록한 alias는 **등록된 alias** 탭에서 확인하고 수정하거나 지울 수 있어요.

## 작동 원리

실행 창이 찾아보는 아래 레지스트리 키에 프로그램 경로를 등록해요.

```
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\<alias>.exe
```

프로그램 목록은 시작 메뉴의 앱 목록에서 가져와서 스토어 앱도 포함돼요. exe가 없는 앱은 바로가기(`.lnk`)를 거쳐 실행해요.

- 관리자 권한, 인터넷 연결, 상주 프로세스가 필요 없어요.
- 수정하거나 지우기 전 값은 `%LOCALAPPDATA%\WinRAlias\backup.jsonl`에 백업돼요.
- 앱을 꺼도 등록한 alias는 계속 동작해요.
- 실행 창(Win+R)에서만 동작하고, cmd나 PowerShell에서는 안 돼요.

## 빌드

```bash
cd WinRAlias
dotnet run
```

.NET 8 SDK가 필요해요.

## 라이선스

[MIT](LICENSE)
