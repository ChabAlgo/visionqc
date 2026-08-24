# Local Agent v1.2.1 이력/AI Suggest API

모든 API는 `http://127.0.0.1:17891`의 JSON POST 요청이다. Loopback 이외의 주소에는 바인딩하지 않는다.

## 검사 이력

- `/api/history/search`
  - 요청: `fromDate`, `toDate`, `cellId`, `position`, `tool`, `toolResult`, `totalResult`, `sourceName`, `fullPath`, `sourceTypes[]`, `page`, `pageSize`.
  - 응답: `totalCount`, `ngCount`, `uniqueCellCount`, `daily[]`, `items[]`.
  - `items[].fullPath`, `items[].tools[].overlayPath`는 원본/Heatmap Viewer에 사용한다.
- `/api/history/import-file/start`
  - 요청: `filePath`, `mode`, `sourceName`, `webVersion`, `namingProfile`.
  - CSV 파일 적재 작업을 시작하고 `jobId`를 반환한다.
- `/api/history/import-file/status`
  - 요청: `jobId`.
  - `running`, `completed`, `processed`, `error`를 반환한다.

## AI SUGGEST Runtime 검사

- `/api/classification/inspect-upload`
  - 요청: 현재 Simulation 설정(`mode`, `positions[]`, `green` 등), `imageBase64`, `fileName`, `mimeType`.
  - 브라우저 File을 Agent 임시 파일로만 작성하고 현재 Runtime File Load의 사전 로드 Control을 재사용해 Green 단일 검사를 수행한다.
  - Workspace를 자동으로 Load하지 않으며, 설정/사전 로드 Runtime이 일치하지 않으면 오류를 반환한다.
  - 임시 파일은 검사 후 삭제하고 SQLite 이력은 작성하지 않는다. 최대 이미지 크기는 80MB다.
  - `green.heatmapImageSave=true`이면 Green NG Tool의 Overlay 파일 경로가 결과에 포함될 수 있다.
