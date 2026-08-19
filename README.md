# VisionQC GitHub Pages v4.4.21

GitHub Pages 정적 배포용 VisionQC Web입니다.

## v4.4.21 핵심
- Simulation 옵션 변경 즉시 반영 + 파라미터 편집 시 스크롤 위치 유지
- 기존 Position checkbox 유지 + 공통 Custom Position 추가/이름변경/삭제
- Custom Position을 Main / Analysis / Settings / Simulation / Tool Settings / Result Input / 실제 NG 경로에 동기화
- Position별 실제 NG 폴더 개별 선택/교체/삭제
- Agent 제거 버튼 및 Agent 버전 mismatch 안내
- 메뉴 버튼과 Page title 겹침 보정
- Progress Update 단위 Live Analysis 유지

## Local Agent
Simulation의 Custom Position 실행에는 `VisionQC Local Agent v0.2.1`을 사용하세요.

기존 Agent가 0.1.2 등으로 계속 실행되면 새 Agent 폴더에서 `BUILD_RELEASE_x64.cmd` 후 `REGISTER_PROTOCOL.cmd`를 다시 실행하세요. 새 REGISTER 스크립트는 127.0.0.1:17891의 기존 Agent를 먼저 종료하고, 현재 폴더의 EXE 경로로 `visionqc-agent://` 등록을 덮어쓴 뒤 Registry 값을 화면에 표시합니다.

검증/업데이트 순서는 `UPDATE_AND_TEST_V4.4.21_KR.txt`를 참고하세요.
