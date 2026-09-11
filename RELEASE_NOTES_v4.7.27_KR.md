# VisionQC v4.7.27 / Local Agent v1.3.17

## Agent 업그레이드 보완

- 설치 프로그램이 실행 중인 기존 VisionQC Launcher와 Worker를 설치 경로로 확인하여 종료합니다.
- 기존 프로그램 파일과 `Web`, `Workers` 등 배포 디렉터리를 제거한 뒤 Agent 1.3.17을 새로 설치합니다.
- `data`, `logs`, `output`, `vpdl-worker.version`은 보존하여 SQLite 이력, 로그, 검사 결과, 선택한 VPDL Worker를 유지합니다.
- 정상 종료하지 않는 구버전 프로세스는 설치 경로 소유 프로세스인지 확인한 후에만 강제 종료합니다.
- Web이 연결된 Agent의 버전 불일치를 감지하면 버튼을 `Agent 업데이트`로 표시하고 현재 설치 파일을 받도록 안내합니다.

Web 브라우저는 Windows 보안 정책상 다운로드한 EXE를 자동 실행할 수 없습니다. `Agent 업데이트`로 받은 설치 파일을 한 번 실행하면 이후 설치·등록·재실행은 자동 처리됩니다.

## 유지된 기능

- 파일명 규칙 JSON 내보내기/가져오기
- VPDL Runtime/Workspace 로드와 Green, Blue, Integrated 검사
- 판정, Position 매칭, CSV/SQLite 이력, Source/Crop/Overlay 저장 규칙

## 배포 파일

- `downloads/VisionQC_Agent_Installer_v1.3.17.exe`
- `downloads/VisionQC_Offline_v4.7.27.zip`
