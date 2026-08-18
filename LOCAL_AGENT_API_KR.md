# VisionQC Local Agent API v0.1.0

Base URL: `http://127.0.0.1:17891`

- `GET /api/status` : Agent / VPDL / GPU / Simulation 상태
- `POST /api/runtime/check` : LocalRuntime.Control 생성으로 Runtime 실제 확인
- `POST /api/pick/file` : Windows 파일 선택창
- `POST /api/pick/folder` : Windows 폴더 선택창
- `GET /api/events` : SSE 실시간 진행 이벤트
- `POST /api/simulation/start` : Green / Blue / Integrated 시작
- `POST /api/simulation/stop` : 중지
- `GET /api/simulation/state` : 현재 진행상태
- `POST /api/agent/exit` : Agent 종료

Custom URI: `visionqc-agent://start`

Agent는 127.0.0.1에만 bind되며 외부 IP에서 접속하지 않습니다.
