# VisionQC v4.7.2 반영 요청

작성일: 2026-08-24

## 확정된 동작

1. 메인 화면의 날짜별 NG율은 SQLite 누적 이력이 아니라 **현재 실행한 시뮬레이션 결과 또는 현재 불러온 CSV 분석 결과**만 표시한다.
2. 메인 집계의 중복 기준은 항상 `Cell ID + Position`이다. 같은 조합의 중복 행/재실행은 한 번만 센다.
3. `NG Image를 NG로 검출한 Score` 범위는 다른 Tool이 NG이고 사용자가 입력한 제외 기준(기본 0.80) 이상인 경우, 최소값뿐 아니라 KPI·분포·Cell별 그래프·확대 그래프·CSV에서 모두 제외한다.
4. Progress Log의 Auto Scroll은 Agent 상태 폴링 주기가 아니라 **새 로그 행이 발생한 순간**에만 맨 아래로 이동한다.
5. Keyword 모드에서는 공통 Input Root를 활성화된 **모든 Position의 Workspace·Tool**로 검사한다. Position별 Keyword가 비어 있으면 전체 파일을 검사하고, Keyword가 있을 때만 해당 Position의 파일로 제한한다. 중복 제거는 `Position + 파일 경로` 기준이다.
6. `Image/`에서 검토한 캡처는 별도 승인 후 `Image/OLD/`로 이동한다.

## 분리 원칙

- SQLite는 시뮬레이션/명시 저장 CSV의 감사 이력을 보존한다.
- 이력 화면 조회는 `Cell ID + Position`이 같은 경우 마지막 기록을 대표값으로 사용해 중복 집계를 막는다.
- 메인 대시보드는 현재 분석 세션만 보여 주므로 과거 Run이 현재 시뮬레이션 수치에 섞이지 않는다.
