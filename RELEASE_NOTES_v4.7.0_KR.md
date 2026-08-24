# VisionQC v4.7.0 / Local Agent v1.2.0

## 핵심 변경

- SQLite 검사 이력 Viewer와 메인 대시보드 최상단 날짜별 NG율: Cell ID·Position·Tool·결과 필터, 서버 측 페이지네이션, 원본/Heatmap 이미지 열기.
- 대용량 CSV 직접 적재: Agent가 줄 단위로 읽어 SQLite에 저장하므로 브라우저 메모리에 전체 행을 쌓지 않는다.
- AI 검사: 파일명 Position을 기준으로 Green Workspace를 자동 선택·Runtime Load·1회 검사하며, Tool별 Score와 Green Heatmap Overlay 전환을 제공한다.
- 유지보수: History Service/CSV Importer/SQLite Store로 책임을 분리하고, 구조·요청·제한사항 문서를 갱신했다.
- UI: 설정 아이콘을 SVG 톱니바퀴로 교체하고 신규 이력/AI 검사 화면의 글자 크기와 조작 영역을 확대했다.

## 검증

- Node 정적/회귀 테스트 58개 통과.
- Agent x64 Release 빌드 성공(경고 0, 오류 0).
- 격리 SQLite DB에서 CSV 직접 적재 API를 실행: 2행 저장, NG 1건, Tool 2개, 2025-02-19/20 일별 집계 확인.
- 설치 EXE와 오프라인 ZIP은 본 릴리스 빌드 후 SHA-256을 `RELEASE_MANIFEST.json`에 기록한다.

## 배포 파일

- `downloads/VisionQC_Agent_Installer_v1.2.0.exe`
- `downloads/VisionQC_Offline_v4.7.0.zip`

## 운영 확인 한 가지

실제 운영 Green Workspace와 이미지 한 장으로 AI 검사 실행 후, 파일명 Position 자동 선택 및 Tool별 Heatmap Overlay가 생성되는지만 확인한다. 이 항목은 Workspace/라이선스가 있어야 가능한 VPDL 실환경 검증이다.
