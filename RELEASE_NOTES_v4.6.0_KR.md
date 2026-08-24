# VisionQC v4.6.0 / Local Agent v1.1.0

## 주요 변경

- CSV/XLSX의 `FullPath` 기반 이미지 조회를 추가했습니다. Score 차트의 점을 클릭하면 Local Agent가 해당 원본을 한 장씩 축소해 표시합니다.
- SQLite 검사 이력을 추가했습니다. Simulation/단일 Green 검사는 자동 기록하고, CSV 분석은 설정 메뉴에서 사용자가 명시적으로 저장합니다.
- SQLite에는 원본 이미지가 아니라 경로, Cell ID, 캡처 시각, 판정, Tool Score, Overlay 경로만 보관합니다.
- Heatmap 저장을 Green 단독 NG 원본 위 Overlay로 명확히 제한했습니다. Integrated 모드에서는 제공하지 않습니다.
- 파일명 규칙을 Simulation/SQLite에도 전달하며, Cell ID·날짜·시간 파싱을 한 규칙으로 통일했습니다.
- 설치 EXE가 Cognex VPDL 설치 루트의 네이티브 `bin` 폴더를 DLL 검색 경로에 명시적으로 추가하도록 보완했습니다.
- Local Agent의 이미지 미리보기·SQLite 저장소를 별도 폴더로 분리하고 Agent 하위 폴더별 유지보수 Markdown을 추가했습니다.
- 설정 아이콘을 조절 패널 형태로 변경했습니다.

## 설치/배포 파일

- `downloads/VisionQC_Agent_Installer_v1.1.0.exe`
- `downloads/VisionQC_Offline_v4.6.0.zip`

Agent 설치 EXE는 SQLite 관리 DLL과 x64 네이티브 DLL을 포함합니다. Cognex VPDL Runtime 및 라이선스는 포함하지 않으므로 대상 PC에 별도 설치되어 있어야 실제 Simulation을 실행할 수 있습니다.

## 검증

- `npm.cmd test`: 회귀 테스트 통과
- x64 Release Agent/Installer 빌드 통과
- 실행한 Agent v1.1.0에서 `/api/status`, `/api/image/preview`, `/api/history/import` 스모크 테스트 통과

## 릴리스 절차

1. `main`에 커밋/푸시합니다.
2. 태그 `visionqc-v4.6.0-agent-v1.1.0`을 생성하고 푸시합니다.
3. GitHub Release 제목을 `VisionQC v4.6.0 / Local Agent v1.1.0`으로 만들고 위 EXE와 ZIP을 첨부합니다.
4. `RELEASE_MANIFEST.json`의 SHA-256과 업로드 파일을 다시 대조합니다.
