# VisionQC v4.7.3

## Runtime 재사용 / 다크·화이트 테마
- Integrated Runtime이 동일 GPU 조건 및 Green Workspace·Position 집합의 Green 단독 Simulation을 재사용할 수 있도록 Web·Agent 검증을 함께 보완했습니다.
- Web GUI에 다크·화이트 모드 전환과 저장 기능을 추가하고, 테마 아이콘을 흰색·어두운색 반반 원형으로 교체했습니다.

---

# VisionQC v4.7.2

## 현재 분석 집계 / Keyword Simulation 안정화

- 메인 날짜별 NG율은 SQLite 누적 이력이 아닌 현재 Simulation 또는 현재 CSV 분석 결과만 표시하며, `Cell ID + Position` 중복을 한 번만 센다.
- SQLite 이력 화면도 같은 중복 키의 마지막 기록만 집계해 재실행/재저장으로 NG율이 누적되지 않게 했다.
- `NG Image를 NG로 검출한 Score`의 다른 Tool NG 제외 기준을 최소 Score뿐 아니라 차트·KPI·확대 보기·CSV에 동일 적용했다.
- Auto Scroll은 새 로그 행이 추가될 때만 하단으로 이동한다.
- Keyword 모드는 공통 Input Root의 파일을 모든 활성 Position에서 검사한다. Position별 Keyword는 선택적 필터이며, 중복 키는 `Position + 파일 경로`다.

---

# VisionQC v4.7.1

## SQLite 집계 / AI SUGGEST / 이미지 Viewer

- 메인 날짜별 NG율을 History 검색 필터와 분리하고 Simulation·CSV 이력만 집계합니다.
- Gemini 호출 기반 AI SUGGEST를 현재 사전 로드 Runtime Green Tool 검사로 교체하고 별도 AI 검사 메뉴를 제거했습니다.
- Tool Score 점 클릭 Viewer에 원본/크롭과 Tool별 Green Heatmap Overlay 전환 버튼을 추가했습니다.
- 실제 NG 폴더는 결과 CSV에 매칭되는 Cell ID만 지연 색인하고, Viewer에서만 원본 파일을 읽습니다.
- 실제 NG 검출 최소 Score에 다른 Tool NG 제외 기준 입력값(초기 0.80)을 추가했습니다.

---

# VisionQC v4.4.38

## Chrome 권한 요청 동작

- 자동 2초 상태 감시와 별도로, 사용자가 `Agent 실행`을 클릭하면 즉시 Chrome Loopback Network 권한 요청을 실행합니다.
- Agent가 이미 실행 중인 경우에도 같은 버튼으로 Chrome 연결 권한을 다시 요청할 수 있습니다.

---

# VisionQC v4.4.37

## Chrome 권한 진단

- Chrome 145+ Loopback Network 권한의 `prompt`/`denied` 상태를 감지해 Local Agent 카드에 복구 방법을 표시합니다.
- 사용자 허용이 필요한 Chrome 보안 권한은 자동 우회하지 않으며, 권한 허용 뒤 기존 2초 상태 감시가 자동으로 Agent를 연결합니다.

---

# VisionQC v4.4.36

## Chrome Loopback 연결 수정

- Chrome 실측에서 `targetAddressSpace: local` 요청이 127.0.0.1(`loopback`)과 불일치하여 CORS 차단되는 것을 확인했습니다.
- Local Agent가 바인딩된 127.0.0.1과 동일한 `targetAddressSpace: loopback`을 사용하도록 수정했습니다.

---

# VisionQC v4.4.35

## Chrome 연결 및 Simulation 경로 선택

- Chrome Local Network Access Fetch 옵션을 수정했습니다. 후속 v4.4.36에서 Local Agent loopback과 정확히 일치하도록 보정했습니다.
- Workspace 선택 직후 선택 경로가 Position 입력칸에 즉시 반영되도록 수정했습니다.
- Keyword Mode에서도 Position별 Image Folder 선택을 유지하여 Ctrl/Shift 다중 폴더 선택이 가능합니다.
- 파일·폴더 선택은 왼쪽 탐색창이 포함된 Windows 표준 Explorer `IFileOpenDialog`를 계속 사용합니다.

---

# VisionQC v4.4.34

## 파일·폴더 선택 및 다중 Image Folder

- Explorer 선택 작업의 완료 신호를 사용해 파일·폴더 선택 뒤 Web 로딩이 남는 문제를 보완했습니다.
- 취소 요청은 Agent에서 즉시 완료 상태로 반환됩니다.
- Image Folder는 Ctrl/Shift 다중 선택을 지원하며 모든 선택 경로가 Simulation에 전달됩니다.
- 여러 입력 폴더의 출력 경로를 분리해 파일명 충돌을 방지합니다.
- 이전 Local Agent, 빌드 중간 파일, 과거 보고서와 누적 CSS를 정리했습니다.

---

# VisionQC v4.4.33

## Windows 파일·폴더 선택
- 정상적으로 열린 IFileDialog를 Web이 5분 뒤 강제로 취소하던 제한시간을 제거했습니다.
- 선택창은 사용자가 파일/폴더를 선택하거나 Windows 취소 버튼을 누를 때까지 유지됩니다.
- `FOS_HIDEMRUPLACES`, `FOS_HIDEPINNEDPLACES` 때문에 사라졌던 왼쪽 탐색 트리와 드라이브 탐색 UI를 복원했습니다.
- stale ClientData 초기화와 안전한 로컬 시작 위치는 유지하되, Dialog 외형과 탐색 방식은 일반 Windows 탐색기 형태를 사용합니다.
- 이전 선택 작업 자동 정리는 WARN 알림이 아니라 INFO 로그로 남깁니다.

---

# VisionQC v4.4.32

## Runtime 상태 의미 정리
- Agent 연결 시 파일 경로로 감지한 VPDL 설치 버전과 실제 Simulation Runtime preload 상태를 분리했습니다.
- 시작 직후 License 확인용 Control은 확인 후 즉시 해제합니다.
- `VPDL Runtime` 카드는 Runtime File Load 전 `미로드`, Workspace preload 성공 후 실제 버전을 표시합니다.

## Workspace Runtime Structure
- Runtime File Load 전에는 Position별 `LOAD WAIT` 카드를 만들지 않고 compact header와 Load 버튼만 표시합니다.
- Load를 시작하면 진행 카드가 나타나고, 성공 후에만 Stream / Tool / Tag / Class / Feature 구조를 유지합니다.
- Load 실패 또는 Agent 연결 해제 시 구조 카드를 다시 접습니다.

## 파일·폴더 선택
- 상단의 별도 `선택 창 취소` 버튼을 제거했습니다. 취소는 Windows 선택창의 취소 버튼을 사용합니다.
- Windows Shell에 저장된 H:/UNC/최근 위치를 요청마다 초기화합니다.
- 최근 위치와 고정 위치 자동 열거를 비활성화하고 안전한 로컬 폴더에서 Dialog를 먼저 띄워, 끊긴 네트워크·가상 드라이브가 선택창 시작을 막지 않게 했습니다.

---

# VisionQC v4.4.31

## Local Agent 통신 / 파일·폴더 선택
- Windows 선택창이 닫힐 때까지 HTTP 요청 하나를 계속 유지하던 구조를 제거했습니다. Web은 짧은 `/api/pick/start` 요청 뒤 `/api/pick/status`로 결과를 확인합니다.
- Web requestId를 Agent가 멱등 처리해 시작 응답이 유실되어도 선택창이 중복으로 열리지 않습니다.
- 같은 탭에 남은 선택창 자동 취소, 상단 `선택 창 취소` 버튼, 5분 안전 제한, 완료 작업 만료를 추가했습니다.
- Agent 내부 예외를 소켓 종료로 숨기지 않고 HTTP 500 JSON과 Agent 로그로 반환합니다.
- H: 가상 드라이브·UNC·연결이 끊긴 네트워크 경로의 `File.Exists`/`Directory.Exists` 선검사를 제거해 선택창 시작 지연을 차단했습니다.
- Chrome Local Network Access용 loopback 요청 표기와 `Failed to fetch` 전용 진단 문구를 추가했습니다.

## 상태 / 버전 표시
- 실제 연결 실패 시 남아 있던 Connected·Runtime·Workspace 화면 상태를 즉시 정리합니다.
- 좌측 메뉴와 Simulation 상단에 `Web v4.4.31 · Agent v0.2.9`를 항상 확인할 수 있게 표시합니다.

## 검증
- 배포 v4.4.30의 메뉴 272px, 전 화면 전환, Keyword disable/enable, Tool 추가·선택 제거, Progress Update 보존, Agent 미연결 선택 실패 경로를 실제 브라우저에서 확인했습니다.
- Web v4.4.31 회귀검사 27종, Agent v0.2.9 구조검사 6종을 통과했습니다.

---

# VisionQC v4.4.30

## Keyword / Picker
- Keyword 모드가 꺼져 있을 때 `Keyword Input Root`의 input과 선택 버튼을 공통 잠금 갱신이 다시 활성화하던 문제를 수정했습니다.
- 모드 변경 시 Options와 Position 목록을 함께 갱신해 공통 폴더와 Position별 폴더의 상호 배타 상태를 즉시 반영합니다.
- 브라우저 탭 ID와 Agent Picker 소유자를 연결해, 동일 탭에 남은 선택 창만 안전하게 취소하고 다시 열 수 있게 했습니다.
- Picker 소유창을 현재 마우스가 있는 모니터 중앙에 표시하고 작업 표시줄 알림을 강화했습니다.

## Runtime 상태 / Navigation
- Agent 종료, 실제 연결 끊김, Agent instance 교체 시 Runtime token·WorkspaceInfo·Inspect cache/status를 함께 제거합니다. 선택한 경로 자체는 재설정을 줄이기 위해 보존합니다.
- 알림 버튼을 Light/Dark 위로 이동하고 Main/Analysis/Classification/Simulation/Settings/Utility 아이콘을 단색 SVG로 통일했습니다.
- Local Agent가 VPDL 4.2 설치 DLL을 탐지하고 로드할 수 있게 했습니다.

## 검증
- Keyword 비활성 문맥, Picker 복구, Agent disconnect cleanup, sidebar icon 회귀검사를 추가했습니다.

---

# VisionQC v4.4.29

## 설정 상태 보존 / Tool 조작
- `ensureSimulationForm()`의 동일 target/source `Object.assign`으로 Tool 및 파라미터가 매번 기본값으로 초기화되던 근본 원인을 제거했습니다.
- Tool 추가·선택 제거 전에 현재 DOM 값을 한 번 동기화하고, 전체 Tool 제거 시 기본 목록으로 되돌리던 숨은 fallback을 제거했습니다.
- Progress Update, JPEG, GPU, HeatMap, Fallback 등 다른 옵션은 Tool 목록 변경 후에도 그대로 유지됩니다.

## Picker / 요청 교착
- Web과 Agent 모두 파일·폴더 선택을 single-flight로 처리합니다.
- 투명 8×8 소유창 대신 실제 STA 메시지 루프와 보이는 소유창을 사용해 Shell Dialog가 브라우저 뒤에 숨는 경우를 방지했습니다.
- 이미 선택창이 열려 있으면 다음 요청을 lock 뒤에 대기시키지 않고 즉시 안내합니다.

## Runtime preload / Simulation
- VPDL 4.0 Workspaces 문자열 indexer 의존을 제거하고 `Workspaces.Add()`가 반환한 실제 객체를 직접 보관합니다.
- preload token/signature/Agent instance를 Web과 대조해 저장된 READ OK 화면과 실제 Runtime 메모리 상태가 다르면 Start 전에 차단합니다.
- Simulation Start 중복 클릭과 Runtime/License 자동 확인 요청 중복을 합쳤습니다.
- 정상 완료·사용자 중지 후 동일 Runtime 객체를 다시 preload 상태로 복구합니다.

## 검증
- Web 정적/상태 회귀검사 24종 통과
- Agent v0.2.7 정적/구조 회귀검사 7종 통과
- Chromium FHD workflow에 Tool 추가/선택 제거/파라미터 보존 실제 클릭 테스트 추가

---

# VisionQC v4.4.28

## 실제 Runtime 사전 로드
- `Runtime File Load`가 `/api/runtime/preload` 한 번으로 선택 Workspace를 실제 VPDL Control에 로드합니다.
- Agent는 로드된 Control을 유지하고 Simulation Start에 그대로 인계해 Workspace 재로딩을 제거합니다.
- Green / Blue / Integrated 모드가 같은 재사용 경로를 사용합니다.

## Picker / Progress / Tool
- 파일·폴더 선택 창에 Agent 소유 TopMost STA 부모 창을 적용했습니다.
- `선택 제거`가 클릭 직전 체크박스 DOM 상태를 동기화합니다.
- Progress Update가 Batch 표시와 요청 JSON에 즉시 반영됩니다.
- Batch 경계 진행 로그를 PROGRESS 한 줄로 통합하고 중복 INFO를 제거했습니다.

## 알림 / 도움말
- 좌측 알림 메뉴, unread badge, 오류·경고 로그 패널을 추가했습니다.
- 실패 toast를 확대하고 표시 시간을 늘렸습니다.
- Simulation 파라미터 hover 툴팁을 추가하고 `구조 안내` 버튼을 제거했습니다.
- Runtime File Load 버튼을 Workspace Runtime Structure 패널로 이동했습니다.

## 검증
- Web 정적/구조 회귀검사 19종 통과
- Agent v0.2.6 정적/구조 회귀검사 5종 통과
- JavaScript syntax 및 CSS brace 검사 통과

---

# VisionQC v4.4.27

## Runtime 선로딩 / 상태 정합성
- Workspace 선택 시 자동 Inspect를 제거하고 Output 패널에 `Runtime File Load`를 추가했습니다.
- 현재 모드의 활성 Position에 필요한 Green/Blue Runtime을 버튼 한 번으로 순차 선로딩합니다.
- Position 객체를 정규화할 때 새 객체로 교체하던 동작을 in-place 갱신으로 변경했습니다.
- Workspace 응답 후 현재 Position을 다시 찾아 저장하므로 Log 성공과 화면 READ ERROR/WAIT가 엇갈리지 않습니다.
- Simulation Start는 선택 경로와 일치하는 READ OK 선로딩 결과를 요구하며 시작 직전 Runtime/License를 다시 확인합니다.

## Agent v0.2.5
- 5분 유휴 종료 `IdleLoop`를 제거해 `/api/agent/exit` 전까지 Agent가 유지됩니다.
- Web이 2초마다 상태를 자동 감시하므로 별도 연결 확인 버튼이 없습니다.
- Agent 시작 직후 Runtime/License를 자동 검사하고 status에 instanceId와 검사 결과를 제공합니다.
- `Agent 제거`를 `Agent 종료`로 바꾸고 종료 시 protocol unregister를 수행하지 않습니다.

## UI / Fallback
- 사용자가 제공한 TOPTEC 로고 원본으로 교체했습니다.
- Runtime Structure는 Position당 하나의 카드로 표시하고 Green/Blue를 카드 내부에 묶었습니다.
- Structure 패널을 Output 바로 아래로 이동했습니다.
- Fallback 파일 선택/Preview가 await 후 고정 slotKey로 현재 행을 다시 찾아 갱신합니다.
- Sample Image의 label/button 중첩을 제거하고 다른 Options와 동일한 label/font/control 크기로 통일했습니다.

## 검증
- Web v4.4.27 정적/구조 회귀검사 15종 통과
- Agent v0.2.5 정적/구조 회귀검사 9종 통과
- JavaScript syntax, CSS brace, 패키지 ZIP 무결성 검사

---

# VisionQC v4.4.26

## Workspace 요청/상태 정합성
- Web의 30초 취소 후 Agent만 성공하던 원인을 제거했습니다. Workspace 요청은 Web queue에서 한 건씩 Agent로 전달하며 180초 제한시간은 실제 실행이 시작된 뒤 적용됩니다.
- 동일 Workspace/GPU 요청은 진행 중 Promise를 공유하고, 성공 결과는 10분 동안 Web에서 재사용합니다. Agent의 파일 크기/수정 시간 cache도 그대로 유지됩니다.
- 파일 선택, queue 대기, VPDL 읽기, 성공/실패 상태를 Position·Green/Blue별 Map에서 관리해 메뉴를 이동해도 버튼·요약·Runtime Structure 카드가 같은 상태를 표시합니다.
- 경로별 generation 검증으로 이전 요청의 늦은 응답이 새 경로를 덮어쓰지 않습니다.
- 180초 응답 제한시간을 넘겨도 즉시 READ ERROR로 확정하지 않고 Agent 완료 확인을 한 번 자동 재시도합니다.
- 브라우저의 raw `signal is aborted without reason` 대신 명시적인 제한시간 오류를 사용합니다.
- 이전 버전에 저장된 raw abort 실패는 설정 복원 시 제거하고, Agent 연결 확인 후 저장된 경로를 자동으로 다시 검사합니다.

## FHD UI / Log
- Simulation Options를 FHD grid의 두 번째 열에 포함되는 sticky panel로 변경해 상단 action과 Workspace 카드 위를 덮던 문제를 제거했습니다.
- Fallback 숫자/Sample Image 입력과 선택 버튼을 동일한 34px 높이 및 dark theme로 정렬했습니다.
- Preview 너비를 줄이고 image/figure 가로 overflow를 차단했습니다.
- Progress Log 시간을 `HH:mm:ss.SSS [LEVEL]`로 통일하고 시간 열 너비를 확장했습니다.

## 검증
- Web 정적/회귀검사 15종 통과
- extension/base bundle syntax 검사 통과
- Local Agent v0.2.4 회귀검사 유지

---

# VisionQC v4.4.25

## Workspace Runtime Structure
- Web에서 같은 Workspace/GPU 요청을 하나의 in-flight 요청으로 합칩니다.
- Local Agent v0.2.4는 Runtime Check와 Workspace Inspect에 하나의 VPDL Control을 재사용하고 요청을 직렬화합니다.
- 파일 경로·크기·수정 시간·GPU 설정이 같으면 구조 결과를 재사용합니다.
- Simulation 시작 전 Inspect Control을 해제하고 실행 중 새 Control 생성을 차단합니다.

## Selection / Simulation Options scroll
- 전역 click 위임이 `body[data-vq-page]`를 메뉴 버튼으로 오인해 매 클릭마다 전체 화면을 재생성하던 직접 원인을 제거했습니다.
- Simulation 텍스트와 입력의 native selection을 명시적으로 허용했습니다.
- Agent 연결/Runtime 확인에서 전체 Simulation DOM 재렌더를 제거했습니다.
- Workspace 구조 응답은 해당 Position summary/select만 부분 갱신합니다.
- Options scrollTop을 state에 계속 기록하고 외부 pointer/click에서 예상치 못한 변경만 복원합니다.
- FHD에서는 Options를 viewport 고정 scroller로 분리하고 1439px 이하에서는 일반 1열 panel로 전환합니다.

## Fallback / Preview
- 980px 최소폭의 10열 표를 Position별 반응형 카드로 교체했습니다.
- Shift/ROI는 3열 compact grid, Sample Image는 panel 폭 안의 input+button으로 배치했습니다.
- Preview modal의 가로 overflow를 차단하고 1439px 이하에서는 이미지 1열로 전환합니다.

## 검증
- Web source/layout/state 회귀검사 확장
- Agent v0.2.4 Control ownership/cache/version/CMD 회귀검사 확장
- Chromium 1920×1080 실제 mouse drag/mouseup, Main click, Options/Fallback/Preview overflow 자동검사 추가

---

# VisionQC v4.4.24

## FHD 메뉴 / 레이아웃
- 기준 화면을 FHD 1920×1080, 브라우저 배율 100%로 명시했습니다.
- 메뉴가 72px icon rail에서 272px drawer로 열릴 때 Classification 및 VisionQC 본문도 함께 이동합니다.
- 작은 화면은 1100px 이하에서 overlay, Simulation은 1439px 이하에서 1열 fallback을 사용합니다.

## Simulation Options scroll
- sticky panel 자체를 scroll container로 사용하던 구조를 제거했습니다.
- 고정 header와 `.vq43-sim-options-scroll` 전용 내부 scroller를 분리했습니다.
- 외부 클릭에서 DOM 변경 없이 scrollTop이 0으로 돌아가는 Chrome 동작을 구조적으로 차단했습니다.
- Agent 연결 확인, 제거, Runtime 확인의 전체 렌더도 scroll-preserving 경로로 통일했습니다.

## Tool Settings
- Tool table의 620px 강제 최소폭과 max-content 폭을 제거했습니다.
- Select / ToolName / Threshold / Judgement 4열을 panel 폭에 맞춘 fixed table layout으로 변경했습니다.
- Runtime 상태 badge 문구를 축약해 ToolName 입력 폭을 확보했습니다.

## Local Agent v0.2.3 build hotfix
- Workspace Inspect fallback이 GPU 장치 번호를 `OptimizedGPUMemory(ulong)`의 메모리 byte 수로 잘못 전달하던 호출을 제거했습니다.
- GPU 장치 선택은 `InitializeComputeDevices(mode, gpuList)`가 담당하고 최적화 메모리는 VPDL Runtime 기본값을 유지합니다.
- Windows CMD가 첫 줄을 `癤?echo off`로 오인하지 않도록 배치 스크립트 9개의 UTF-8 BOM을 제거했습니다.

## 검증
- extension / React bundle `node --check`
- FHD drawer, Options scroller, Tool fixed table, responsive fallback, asset version 회귀검사 추가
- Local Agent source/CMD/project 정적 회귀검사 6종 추가

---

# VisionQC v4.4.23

## 안정화 / 정합성
- Simulation Options의 구조 변경(Judgement 추가/삭제/순서변경, Tool 추가/삭제 등)에서도 window/page/options scroll 위치와 focus를 함께 보존합니다.
- Keyword Mode처럼 화면 구조가 바뀌는 설정도 Simulation 전체를 재렌더하지 않고 Position 영역만 부분 갱신합니다.
- Workspace/Folder 선택은 필요한 입력값과 Workspace 구조 영역만 갱신하여 Options scroll을 건드리지 않습니다.
- 누적 CSS 충돌을 차단하기 위해 `visionqc-v4423-clean.css`를 마지막에 로드하는 단일 authoritative layout layer를 추가했습니다.
- Simulation Options 내부 input/select/table/judgement가 패널 밖으로 넘지 않도록 폭/overflow/grid 규칙을 재정의했습니다.

## Live Batch / Log
- Local Agent v0.2.3과 함께 Progress Update=N이면 N/2N/3N... 경계에서 Simulation Status와 Live 분석 Batch가 함께 갱신됩니다.
- Batch=1은 이미지 1장마다 갱신됩니다. 마지막 잔여 Batch는 완료/중지 시 즉시 반영합니다.
- Local VPDL 아래 상세 Progress Log, Auto Scroll, Elapsed, ETA, Images/sec, Batch 표시를 추가했습니다.

## 메뉴 / 브랜드
- HSAGP 참고 화면처럼 항상 보이는 좌측 icon rail + 상단 메뉴 버튼 클릭 시 확장되는 drawer 구조로 변경했습니다.
- 하단에 Light/Dark, Language, Login 아이콘 자리만 준비했습니다(기능은 미구현).
- 분류 화면의 기존 `@TOPTEC` 텍스트를 제거하고 사용자가 제공한 TOPTEC 로고 이미지를 적용했습니다.

## VPDL Workspace / Tool
- Workspace 구조는 Simulation 상단의 `Workspace Runtime Structure` 영역에 Stream / Tool Type / Tag / Class / Feature를 표시합니다.
- 구조 읽기 실패 시 실제 오류를 해당 카드와 Progress Log에 표시합니다.
- Stream은 Runtime에서 읽은 목록으로 dropdown을 구성합니다.
- Tool Settings의 Position별 체크박스를 제거했습니다. Tool은 실행 대상으로 체크한 모든 Position에 동일하게 적용됩니다.
- ToolName은 text input으로 유지하며 Runtime에서 발견된 Green Tool이면 초록색, 없으면 빨간색, 구조 미확인이면 중립색으로 표시합니다.
# v4.7.0 / Local Agent v1.2.0

- SQLite 검사 이력 Viewer와 날짜별 NG율, Cell/Position/Tool/결과 필터, 페이지 조회를 추가했습니다.
- 대용량 CSV를 Agent가 줄 단위로 SQLite에 직접 적재하도록 추가했습니다.
- 파일명 Position 기반 Green Workspace 자동 선택 AI 검사와 Tool별 Green Heatmap Overlay 전환을 추가했습니다.
- History Service/CSV Importer/SQLite Store 문서와 회귀 테스트를 추가했습니다.
- 설정 아이콘을 SVG 톱니바퀴로 교체했습니다.
