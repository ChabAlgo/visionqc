# tests

Node 정적 회귀 테스트다. 실제 Cognex 모델을 실행하지 않고도 버전 일치, API 라우팅, Picker 분리, 다중 폴더, SQLite/이미지 미리보기 서비스, 설치 Payload를 확인한다.

루트에서 `npm.cmd test`를 실행한다. VPDL 실제 Runtime 검증은 별도로 Agent를 실행해 `/api/status`, `/api/image/preview`, `/api/history/import`을 호출한다.
