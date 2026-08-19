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
