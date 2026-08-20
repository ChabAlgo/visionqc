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
