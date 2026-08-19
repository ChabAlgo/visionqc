# VisionQC Local Agent API - v0.2.3

Base: `http://127.0.0.1:17891`

- `GET /api/status` : Agent/VPDL/GPU/Simulation 상태
- `GET /api/events` : SSE `progress`, `analysis`, `log`, `completed`, `stopped`, `error`
- `POST /api/runtime/check` : Runtime/License 실제 생성 확인
- `POST /api/workspace/inspect` : Runtime Workspace Stream/Tool/Tag/Class/Feature 검사
- `POST /api/pick/file` : Windows Shell file picker
- `POST /api/pick/folder` : Windows Shell folder picker
- `POST /api/simulation/start` : Green/Blue/Integrated 시작
- `POST /api/simulation/stop` : 중지
- `POST /api/blue/fallback/preview` : Blue fallback preview
- `POST /api/agent/exit` : Agent 종료

POST body는 GitHub Pages -> localhost CORS preflight를 피하기 위해 `text/plain;charset=UTF-8` Content-Type에 JSON 문자열을 사용합니다.
