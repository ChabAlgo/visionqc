# tests

Node 정적 회귀 테스트다. 실제 Cognex 모델을 실행하지 않고도 버전 일치, API 라우팅, Picker 분리, 다중 폴더, SQLite/이미지 미리보기 서비스, 설치 Payload를 확인한다.

루트에서 `npm.cmd test`를 실행한다. `/api/status`, `/api/image/preview`, `/api/history/import`만으로 VPDL 실제 추론 성공을 판정하지 않는다. 실제 Runtime 검증은 `/api/runtime/preload`, `/api/simulation/start` 이후 처리 건수, Tool 결과와 히트맵 저장까지 확인한다.

## Agent v1.3.8 실행 회귀 테스트

- `launcher-lifecycle.ps1 -LauncherPath <빌드된 VisionQC.LocalAgent.exe>`: VPDL 없는 임시 폴더와 모의 Worker로 버전 전환/비정상 종료를 재현한다. 3회 실행 중 최초 1회에만 --offline이 전달되고 종료 코드/SDK 출력이 기록되는지 확인한다. 실제 Agent를 종료한 상태에서 실행한다.
- `node LocalAgent_v0.2.12/tests/core-smoke.mjs`: 실제 Core Worker, 빈 VPDL 경로, 별도 SQLite/웹 폴더로 실행해 상태 응답, 오프라인 화면, Simulation 거절을 확인한다. 실행 중인 Agent가 있으면 종료하지 않고 테스트를 거절한다.
- 테스트용 임시 폴더는 결과 확인을 위해 남기며 운영 DB에는 쓰지 않는다.
