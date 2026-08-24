# VisionQC v4.7.2 / Local Agent v1.2.2

## 수정 사항

- 메인 날짜별 NG율은 현재 실행 Simulation 또는 현재 불러온 CSV 분석 결과만 표시합니다.
- 메인과 SQLite 이력 화면 모두 `Cell ID + Position`이 같은 결과를 중복 집계하지 않습니다. SQLite는 원본 Run 행을 보존하고 마지막 기록을 대표값으로 보여 줍니다.
- `NG Image를 NG로 검출한 Score`는 다른 Tool이 NG이고 입력 기준(기본 0.80) 이상인 행을 최소 Score·KPI·분포·Cell별 차트·확대 차트·CSV에서 일관되게 제외합니다.
- Progress Log의 Auto Scroll은 새 로그가 도착했을 때만 동작합니다.
- Keyword 모드에서는 공통 Input Root가 활성 Position 모두에 전달됩니다. Keyword가 비어 있으면 모든 파일을 검사하고, 입력된 Keyword가 있을 때만 해당 Position 파일로 제한합니다.

## 제한 사항

- Red Tool 전용 Workspace 구조 검사는 아직 전용 호환 경로와 실제 Workspace 회귀 테스트가 없어 안정 지원 범위에 포함되지 않습니다.
