# VisionQC v4.7.28 / Local Agent v1.3.18

## UI 보정

- Simulation Options 헤더의 음수 마진을 제거하고 헤더와 스크롤 본문에 안쪽 여백을 적용했습니다.
- 파일명 규칙 저장 목록 드롭다운의 배경·글자·테두리·native option 색상을 다크·라이트 테마별로 고정했습니다.

## Runtime/Workspace 재사용

- Green Simulation 기본 경로는 `Runtime File Load`로 생성한 Runtime Control과 메모리에 로드된 Workspace를 그대로 사용합니다.
- 검사 완료 후에도 정상 Runtime을 Agent에 반환하여 다음 Simulation과 AI SUGGEST에서 재사용합니다.
- AI SUGGEST에 호환되는 사전 로드 Runtime이 없으면 자동으로 Workspace를 다시 열지 않고 `Runtime File Load`를 먼저 실행하도록 안내합니다.
- GPU, 진단 옵션, Position, Workspace 경로가 달라진 경우에는 안전하게 기존 Runtime을 재사용하지 않습니다.

검사 판정, Tool threshold, Position 매칭, Workspace 파일, CSV/SQLite 이력, Source/Crop/Overlay 저장 규칙은 변경하지 않았습니다.

## 배포 파일

- `downloads/VisionQC_Agent_Installer_v1.3.18.exe`
- `downloads/VisionQC_Offline_v4.7.28.zip`
