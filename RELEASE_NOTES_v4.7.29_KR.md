# VisionQC v4.7.29 / Agent v1.3.19

## 변경 사항

- Simulation이 만드는 통합 `results_*.csv` 한 파일에 `CaptureTimestamp`, `ProcessedPath`, `Position`, `WorkspaceType`, `WorkspaceName`, `WorkspaceKey`를 기록합니다.
- 활성 Position 전체의 Tool을 통합 CSV 열로 만들고 각 행에 Tool 결과와 Score를 기록합니다. 해당 Position에 없는 Tool은 빈 값으로 유지합니다.
- 검사 이력의 `대용량 CSV 직접 저장`에서 위 열을 읽어 Position·검사 모드·Workspace 필터와 원본/Crop Viewer 경로를 보존합니다.
- 이전 CSV에 새 열이 없어도 기존처럼 파일명과 기본 Position을 이용해 가져올 수 있습니다.
- 검사 이력 화면에 `DB 전체 삭제` 버튼을 추가했습니다. 화면 확인 후 Agent 확인 토큰이 일치해야 삭제됩니다.
- Simulation 또는 CSV 저장 중에는 삭제를 거부합니다. SQLite의 검사·Tool 이력만 비우며 원본 이미지와 CSV 파일은 삭제하지 않습니다.

## 권장 사용 순서

1. Integrated 또는 Green Simulation을 실행합니다.
2. Output 최상위의 `results_YYYYMMDD_HHMMSS.csv`를 선택합니다.
3. `검사 이력 · 날짜별 NG율`에서 `대용량 CSV 직접 저장`을 누릅니다.
4. Position, 검사 모드, Workspace, Tool 조건으로 조회합니다.

## 검증

- 기존 CSV 열 인식과 신규 Workspace/Tool 열 인식을 함께 회귀 검증합니다.
- 삭제 확인 취소, 확인 후 삭제, 삭제 후 빈 DB 재조회 흐름을 브라우저에서 검증합니다.
- 설치 파일과 오프라인 패키지는 `RELEASE_MANIFEST.json`의 SHA-256으로 확인합니다.
