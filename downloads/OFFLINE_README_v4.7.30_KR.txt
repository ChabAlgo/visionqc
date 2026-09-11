VisionQC v4.7.30 / Local Agent v1.3.19

1. VisionQC_Agent_Installer_v1.3.19.exe를 실행합니다.
2. 설치 프로그램은 실행 중인 기존 Agent를 종료하고 프로그램 파일을 제거한 뒤 새 버전을 설치합니다.
3. 기존 data, logs, output과 VPDL Worker 선택값은 유지됩니다.
4. Simulation Output의 통합 results_YYYYMMDD_HHMMSS.csv는 검사 이력의 대용량 CSV 직접 저장에 그대로 사용할 수 있습니다.
5. 통합 CSV에는 Position, 검사 모드, Workspace 이름·키, Tool 결과·Score, 원본/처리 이미지 경로가 포함됩니다.
6. DB 전체 삭제는 검사 이력 화면에서 실행합니다. 검사·Tool 이력만 삭제하며 원본 이미지와 CSV 파일은 삭제하지 않습니다.
7. Simulation 또는 CSV 저장이 실행 중이면 DB 삭제가 거부됩니다.

Cognex VPDL Runtime과 라이선스 및 Workspace는 설치 패키지에 포함되지 않습니다.

날짜별 NG율 그래프는 최고 NG율보다 3%p 높은 범위까지 자동 확대하여 낮은 변동도 구분합니다.
