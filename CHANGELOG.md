# VisionQC v4.4.21

## Simulation option editing
- Simulation Options의 checkbox/select/number/text 값을 변경 즉시 state + localStorage에 반영합니다.
- 일반 파라미터 편집 시 Simulation 전체 화면을 다시 그리지 않으므로 스크롤 위치가 위로 튀지 않습니다.
- Keyword Mode처럼 레이아웃 자체가 변하는 옵션만 재렌더하며, 재렌더 전/후 scrollTop을 복원합니다.
- Simulation Start 직전에 현재 보이는 모든 입력값을 한 번 더 flush하여 포커스가 남아 있는 값도 누락하지 않습니다.

## Global custom Position
- 기존 Position checkbox는 유지하며, checkbox는 현재 Green/Blue/Integrated 모드에서 그 Position을 실행할지 여부만 제어합니다.
- Position 자체는 이름을 자유롭게 추가/변경/삭제할 수 있습니다.
- 공통 Position 목록은 Simulation / Main / Analysis / Settings / Tool 적용 Position / 결과 CSV 입력 / 실제 NG 이미지 경로에 동기화됩니다.
- Settings에서 Position별 실제 최종 NG 이미지 폴더를 독립적으로 선택/교체/제거할 수 있습니다. 기존 전체 NG Root 선택도 유지합니다.
- Position 이름 변경 시 현재 로드된 결과/NG 데이터, Threshold key, 저장된 FileSystem handle key도 함께 이동합니다.

## Local Agent
- 권장 Agent를 v0.2.1로 변경했습니다.
- Tool Settings의 Position 적용 대상을 동적 positionKeys로 Agent에 전달합니다.
- Agent의 cell_position_summary CSV도 실행한 Position 목록을 동적 column으로 기록합니다.
- Web 상단에 Agent 제거 버튼을 추가했습니다. v0.2.1에서는 프로토콜 등록 제거 + Agent 종료를 수행하며 Agent 폴더 자체는 삭제하지 않습니다.
- Web이 실행 중 Agent의 버전을 확인해 v0.2.1과 다르면 등록 갱신 안내를 표시합니다.

## UI
- 메뉴 버튼 크기/내부 여백을 늘리고 Page title 영역에 좌측 여백을 추가하여 고정 메뉴 버튼과 제목이 겹치지 않게 조정했습니다.

## Live analysis
- 기존 v4.4.20 방식 유지: Progress Update = N이면 Agent가 상세 검사 결과 N건을 메모리에 모아 SSE analysis 이벤트 한 번으로 전송합니다. CSV를 중간에 다시 읽지 않습니다.
