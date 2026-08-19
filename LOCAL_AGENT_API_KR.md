# VisionQC Local Agent API - v0.2.0

Base: `http://127.0.0.1:17891`

- `GET /api/status`
- `POST /api/runtime/check`
- `POST /api/pick/folder`
- `POST /api/pick/file` (`fileType`: workspace/csv/image)
- `POST /api/blue/fallback/preview`
- `POST /api/simulation/start`
- `POST /api/simulation/stop`
- `GET /api/simulation/state`
- `GET /api/events` SSE

SSE events: `status`, `progress`, `analysis`, `completed`, `stopped`, `error`.

`analysis` 이벤트는 Green/Integrated의 per-image Tool result/score를 `Progress Update` N개씩 묶어 전달합니다.
