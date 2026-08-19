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
