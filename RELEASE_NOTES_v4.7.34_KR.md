# VisionQC v4.7.34 / Agent v1.3.21

## 수정 내용

Simulation 중 브라우저가 SSE 실시간 분석 배치 일부를 놓치면, Agent의 CSV와 SQLite에는 전체 결과가 있어도 대시보드는 수신한 행만 집계했습니다. 완료 또는 중지 시 Web이 해당 Simulation 실행 ID의 원본 SQLite 행을 페이지 단위로 다시 조회하고, 전체 저장 건수와 일치하는 경우에만 대시보드 입력을 원자적으로 교체하도록 수정했습니다.

일반 검사 이력 조회의 Cell/Position/Workspace 중복 제거 규칙은 변경하지 않았습니다. 이번 완료 동기화 API는 한 실행의 모든 이미지 행을 `image_id` 순서로 반환하므로 시뮬레이션 처리 건수와 대시보드 입력 건수가 일치합니다.

Agent 상태 스냅샷에 `simulationRunId`, 활성 Position Worker 수, 완료 Position Worker 수도 포함해 Web 재연결 후에도 완료 동기화 대상을 식별할 수 있게 했습니다.

## 확인

- 정적 회귀 테스트: 138개 통과
- 브라우저 완료 동기화 회귀: 실시간 2건만 받은 상태에서 저장 결과 12건으로 교체되고 4개 Position이 각각 3건으로 집계됨
- 전체 브라우저 회귀: 45개 통과
- Windows x64 Release 빌드: 오류 0개
- 배포 패키지 리소스 및 SHA-256 감사: 통과

## 적용 조건

Web v4.7.34와 Agent v1.3.21을 함께 사용해야 완료 결과 재동기화가 동작합니다. 기존 Agent는 새 조회 API와 실행 ID 상태를 제공하지 않습니다.
