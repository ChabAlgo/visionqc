VisionQC Web v4.4.13 - VPDL Simulation UI Prototype
====================================================

이번 변경
1. 메뉴에 시뮬레이션 추가
2. Integrated Simulation / Green Simulation / Blue Crop 화면 추가
3. Local Agent 상태 영역 추가
4. Workspace, Image Folder, Stream, Output, GPU, 진행률, 로그 GUI 구성
5. Local Agent 연결 확인 주소 초안: http://127.0.0.1:17891/api/status

중요
- 이번 버전은 Web GUI와 Local Agent 연결 골격까지만 포함합니다.
- 실제 Cognex VPDL Runtime / GPU 시뮬레이션 실행은 아직 연결하지 않았습니다.
- 기존 DL_Simulation_v1.13의 실행 엔진을 다음 단계에서 Local Agent로 분리하여 연결할 예정입니다.
- 기존 이미지 분류 React 번들은 수정하지 않았습니다.
