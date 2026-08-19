# VisionQC Local Agent API - v0.2.1

Base: `http://127.0.0.1:17891`

- `GET /api/status` : Agent/VPDL/GPU/Simulation 상태
- `GET /api/events` : SSE status/progress/analysis/completed/stopped/error
- `POST /api/runtime/check` : Runtime/License 실제 생성 확인
- `POST /api/pick/file` : Windows Shell file picker
- `POST /api/pick/folder` : Windows Shell folder picker
- `POST /api/simulation/start` : Green/Blue/Integrated 시작
- `POST /api/simulation/stop` : 중지
- `POST /api/agent/exit` : Agent 종료
- `POST /api/agent/unregister` : visionqc-agent:// 등록 제거 후 Agent 종료

## Live analysis
Green/Integrated는 이미지별 상세 결과를 Agent 메모리에 저장합니다. `Progress Update=N`이면 N건을 SSE `analysis` batch로 Web에 전달하고, 완료/중지/오류 시 남은 건도 flush합니다. 중간 CSV 재열람은 하지 않습니다.

## Custom Position
Web v4.4.21은 Tool마다 `positionKeys` 배열을 보냅니다. Agent v0.2.1은 기존 CA_TOP/AN_TOP/CA_BOT/AN_BOT boolean과의 하위 호환을 유지하면서 `positionKeys`가 있으면 그것을 우선 사용합니다.
