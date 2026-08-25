# VisionQC Local Agent v1.2.3

## v1.2.3 유지보수 기준

- `Services/HistoryService.cs`: 이력 HTTP 요청과 비동기 import 작업 관리.
- `Services/CsvHistoryFileImporter.cs`: CSV 한 줄씩 SQLite에 직접 적재.
- `Persistence/SqliteRunStore.cs`: 영구 검사 이력과 서버 측 검색/페이지네이션.
- `API_V1.2_HISTORY_KR.md`, `Services/HISTORY_SERVICE_KR.md`, `Persistence/HISTORY_STORE_KR.md`를 새 작업 전에 확인합니다.
- Keyword 모드는 공통 Root를 모든 활성 Position에 전달하며, 중복 제거 키는 `Position + 파일 경로`입니다.
- SQLite 조회 집계는 같은 `Cell ID + Position`의 마지막 기록을 대표값으로 사용합니다.
- Integrated Runtime은 동일 GPU 조건 및 Green Workspace·Position 집합이면 Green 단독 Simulation에서 재사용합니다.
- 배포 파일은 `VisionQC_Agent_Installer_v1.2.3.exe`, `VisionQC_Offline_v4.7.6.zip`입니다.

# 이전 변경 이력

## v1.1.0 오프라인 패키지
- `VisionQC_Agent_Installer_v1.1.0.exe` 하나로 Agent 설치, `visionqc-agent://` 프로토콜 등록, Agent 실행, 오프라인 UI 열기를 처리합니다.
- 설치된 Agent가 `http://127.0.0.1:17891/`에서 Web UI와 로컬 글꼴/스크립트를 직접 제공하므로 인터넷과 GitHub Pages 없이 사용할 수 있습니다.
- Cognex VPDL Runtime과 라이선스는 별도 제품이므로 시뮬레이션 PC에 기존 설치가 필요합니다.

VisionQC Web과 사용자 PC의 Cognex VPDL Runtime/GPU/로컬 파일을 연결하는 loopback 실행 엔진입니다.

## v0.2.15 핵심
- Chrome이 전면을 계속 소유하는 경우에도 Explorer 선택 창이 생성되는 즉시 전면으로 승격하도록 보완
- 화면 중앙의 실제 1px 최상위 owner 창을 사용해 Windows Shell Dialog의 안정적인 표시 유지
- Image Folder의 Windows 기본 다중 선택(Ctrl/Shift) 유지

## v0.2.12 핵심
- Explorer 선택 대화상자의 UI 완료 신호를 사용해 파일·폴더 선택 결과가 Web 작업으로 즉시 반환되도록 보완했습니다.
- Image Folder 다중 선택 결과를 배열로 반환하고 Green/Blue Simulation의 모든 입력 폴더에 적용합니다.
- 취소 요청은 Explorer 종료 지연과 무관하게 Web의 대기 상태를 즉시 해제합니다.

## v0.2.11 핵심
- 폴더 선택창의 왼쪽 탐색 트리를 숨기던 Shell 옵션 제거
- 일반 Windows 탐색기 형식의 Quick Access/드라이브 탐색 영역 복원
- stale 저장 경로 초기화와 안전한 로컬 시작 위치는 유지
- 이전 선택 작업 자동 정리 로그를 WARN에서 INFO로 변경

## v0.2.10 핵심
- 설치 폴더에서 감지한 VPDL 버전과 실제 Simulation Runtime preload 버전을 API에서 분리
- Agent 시작 직후 License 확인용 Control은 검사 후 해제하고, Runtime File Load 성공 전에는 Runtime 미로드 상태 유지
- Windows Shell이 기억한 H:/UNC/최근 위치를 초기화하고 안전한 로컬 폴더에서 선택창 시작
- 최근 위치·고정 위치 자동 열거를 끄고 끊긴 네트워크 경로가 Dialog 시작을 막는 현상 차단

## v0.2.9 핵심
- 파일·폴더 선택을 `start -> status` 비동기 작업으로 바꿔 사용자 선택 시간 동안 HTTP 연결과 요청 thread를 점유하지 않음
- requestId 멱등 처리, 같은 탭의 잔류 선택창 자동 취소, 완료 작업 30초 만료
- Dialog 생성 전 취소도 예약해 숨은 선택창이 뒤늦게 나타나는 race 차단
- H: 가상/네트워크/UNC 초기 경로의 느린 존재 확인을 건너뛰고 Dialog 종류별 마지막 위치 사용
- HTTP 처리 예외를 Agent log와 500 JSON으로 반환해 Web의 원인 없는 `Failed to fetch` 제거

## v0.2.8 핵심
- 브라우저 탭별 파일 선택창 소유권을 추적하고 같은 탭에 남은 선택창만 자동 취소/재시도
- `/api/pick/cancel`과 `IFileDialog.Close`로 Web 타임아웃 뒤 Agent에 남던 BUSY 상태 복구
- 선택창 소유창을 현재 마우스 모니터 중앙의 보이는 FixedDialog로 변경
- VPDL 4.2 설치 경로 자동 탐지
- 여러 VPDL 설치가 있을 때 Build/Status/AssemblyResolve가 같은 버전을 고르도록 우선순위 통일
- 빌드 스크립트가 경로 인자와 `COGNEX_VPDL_DLL_DIR` 환경변수를 지원

## v0.2.7 핵심
- `Workspaces.Add()`가 반환한 실제 Workspace 객체를 Registry에 보관하고 Simulation에서 직접 재사용
- VPDL 4.0에서 동작하지 않던 `control.Workspaces["ws_CA_TOP"]` 동적 이름 조회 제거
- 파일·폴더 선택창을 투명 8×8 부모창이 아닌 실제 STA 메시지 루프와 보이는 소유창에서 실행
- 선택창은 한 번에 하나만 허용하며 두 번째 요청은 대기시키지 않고 즉시 BUSY 반환
- Runtime preload token/signature를 Web에 제공해 저장된 화면 상태와 실제 Agent 메모리 상태를 구분
- 사용되지 않는 `analysisBoundary` 제거로 Release 빌드 경고 0개 정리

## v0.2.6 핵심
- 5분 유휴 자동 종료 제거: Web의 `Agent 종료` 또는 `/api/agent/exit` 전까지 계속 실행
- 시작 직후 Runtime/License 자동 확인
- `/api/status`를 위한 hardware 정보 캐시와 `instanceId`, License 결과 제공
- Web v4.4.29의 2초 실시간 상태 감시 및 실제 Runtime File Load 세션 지원
- 사전 로드한 VPDL Control/Workspace를 Simulation Start가 그대로 재사용
- 파일·폴더 선택 창의 전용 TopMost 부모 창 적용
- Batch 진행 로그 중복 제거
- Agent 종료 시 protocol 등록은 유지하므로 다음 실행에 재등록 불필요

## v0.2.4 핵심
- VPDL Control 단일 소유: Runtime Check와 Workspace Inspect는 하나의 Control을 재사용
- Simulation 시작 전 Inspect Control을 해제하고, 실행 중 새 Control 생성을 차단
- 같은 Workspace 경로/파일 상태/GPU 설정의 구조 결과를 캐시해 Position별 중복 로드 제거
- 동시에 들어오는 Runtime/Workspace 요청을 직렬화해 Cognex `하나 이상의 컨트롤` LogicException 차단

## v0.2.3 핵심
- Build hotfix: Workspace Inspect fallback에서 GPU 장치 번호를 GPU 메모리 byte 수로 잘못 전달하던 호출 제거
- Windows CMD가 첫 명령을 `癤?echo`로 해석하지 않도록 모든 `.cmd`를 UTF-8 BOM 없이 제공
- Progress Update exact batching: 1이면 매 이미지, 5이면 5/10/15... + 마지막 잔여
- analysis/progress SSE 분리 및 동일 processed count 중복 제거
- START/INFO/WARN/PROGRESS/DONE/STOP/ERROR 상세 log SSE
- Elapsed / ETA / Images/sec / BatchSize 상태 제공
- Workspace Inspect 1차: 실제 DL_Simulation 엔진과 동일한 Control + path 로드
- 1차 실패 시 BeadGridInspector의 Deferred + FileStream 방식으로 재시도
- Stream / Tool Type / Green KnownTags / Red KnownClasses / Blue KnownFeatures 반환
- Tool별 Position 적용 요청 필드 제거. Tool은 실행 대상으로 선택한 모든 Position에 동일하게 적용
- Custom Position은 내부 ToolRoiConfig.PositionKeys에 실행 Position 전체를 넣어 원본 4-Position 제한을 확장

## 빌드/업데이트
1. `BUILD_RELEASE_x64.cmd`
2. 반드시 오류 0개 확인
3. `REGISTER_PROTOCOL.cmd`
4. `CHECK_PROTOCOL.cmd`에서 현재 EXE 경로 확인
5. `RUN_AGENT.cmd` 또는 Web의 Agent 실행
6. `http://127.0.0.1:17891/api/status`에서 `agentVersion: 1.1.0` 확인

`OptimizedGPUMemory(ulong)`은 GPU 장치 번호가 아니라 예약 메모리의 byte 수를 받습니다. 이 패키지는 장치 선택을 `InitializeComputeDevices`에만 맡기고, 최적화 메모리는 설치된 VPDL Runtime의 기본 설정을 유지합니다.

## 구버전 Agent가 실행되는 경우
`visionqc-agent://`는 Windows Registry에 EXE 절대경로를 저장합니다. 새 Agent를 BUILD만 하면 등록 경로가 바뀌지 않습니다. 새 폴더에서 반드시 `REGISTER_PROTOCOL.cmd`를 다시 실행하세요.

## 실행 구조
`GitHub Pages VisionQC -> 127.0.0.1:17891 -> Local Agent -> VPDL Runtime/GPU/Local Files`

Agent는 Loopback에서만 대기하고 사용자가 종료할 때까지 유지됩니다. 부팅 자동 실행은 사용하지 않습니다.
