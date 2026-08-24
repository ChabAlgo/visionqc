# Local Agent v1.2.0 이력/AI 검사 API

모든 API는 `http://127.0.0.1:17891`의 JSON POST 요청이다. Loopback 이외의 주소에는 바인딩하지 않는다.

## 검사 이력

- `/api/history/search`
  - 요청: `fromDate`, `toDate`, `cellId`, `position`, `tool`, `toolResult`, `totalResult`, `sourceName`, `page`, `pageSize`.
  - 응답: `totalCount`, `ngCount`, `uniqueCellCount`, `daily[]`, `items[]`.
  - `items[].fullPath`, `items[].tools[].overlayPath`는 원본/Heatmap Viewer에 사용한다.
- `/api/history/import-file/start`
  - 요청: `filePath`, `mode`, `sourceName`, `webVersion`, `namingProfile`.
  - CSV 파일 적재 작업을 시작하고 `jobId`를 반환한다.
- `/api/history/import-file/status`
  - 요청: `jobId`.
  - `running`, `completed`, `processed`, `error`를 반환한다.

## AI 자동 검사

- `/api/classification/inspect/auto`
  - 요청: `imagePath`, `positions[]`, `green`, `namingProfile`.
  - 파일명에서 Position 문자열이 단 하나만 일치할 때 그 Position의 Green Workspace를 자동으로 Runtime Load하고 단일 검사를 수행한다.
  - `green.heatmapImageSave=true`이면 Green NG Tool의 Overlay 파일 경로가 결과에 포함될 수 있다.
  - Simulation Runtime 사용 중이거나 Position이 0개/복수개 일치하면 실패 응답을 반환한다.
