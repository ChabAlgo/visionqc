# Local Agent 유지보수 시작점

이 폴더는 VisionQC Web이 로컬 PC의 파일, VPDL Runtime, SQLite에 안전하게 접근하도록 하는 x64 .NET Framework 4.8 Agent다.

- 시작 파일: `Program.cs`
- HTTP/SSE 조율: `AgentServer.cs`
- API DTO: `AgentDtos.cs`
- 포트: `127.0.0.1:17891` (외부 인터페이스에 바인딩하지 않는다)
- 영구 이력: `%LOCALAPPDATA%\VisionQC\LocalAgent\data\visionqc-history.sqlite`

시작 시 `Program.cs`가 Cognex VPDL Studio와 설치 루트의 `bin`/`Service` 폴더를 현재 프로세스 DLL 검색 경로에 넣는다. 설치본에서 Runtime 오류 126이 발생하면 이 경로 설정과 VPDL Runtime/라이선스 설치 상태를 먼저 확인한다.

하위 폴더의 `README_KR.md`에는 목적·의존성·제한·수정 규칙을 적었다. 새 대화에서는 이 문서와 `..\USER_REQUESTS_V4.6.0_KR.md`, `..\ARCHITECTURE_V4.6.0_KR.md`를 먼저 읽는다.

`bin`, `obj`는 빌드 산출물이며 직접 수정하지 않는다. `OfflineInstaller`에 새 런타임 DLL을 추가했다면 설치 프로그램의 Payload와 `.csproj` EmbeddedResource도 반드시 함께 추가한다.
