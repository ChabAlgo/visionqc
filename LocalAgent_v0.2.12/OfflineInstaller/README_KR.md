# OfflineInstaller

`VisionQC_Agent_Installer.exe` 단일 설치 파일을 만드는 .NET Framework 4.8 프로젝트다.

설치 순서: 기존 설치 Agent 종료 → Embedded Payload 추출 → `--register` 프로토콜 등록 → Agent `--offline` 실행 → 바탕화면 바로가기 생성.

포함 대상은 Agent EXE/config, `System.Drawing.Common.dll`, `System.Data.SQLite.dll`, `x64\SQLite.Interop.dll`, 그리고 Web/폰트/정적 자산이다. 새 의존성 DLL을 Agent에 추가하면 다음 세 곳을 함께 수정한다.

1. `Program.cs`의 `Payload` 배열
2. `VisionQC.AgentInstaller.csproj`의 `EmbeddedResource`
3. `BUILD_VPDL_WORKERS.ps1`의 Worker 묶음 생성
4. 오프라인 패키지와 설치 후 실제 실행 검증

Cognex VPDL Runtime과 라이선스는 용량·라이선스 이유로 설치 파일에 포함하지 않는다.
