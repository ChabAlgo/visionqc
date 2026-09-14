VisionQC v4.7.35 / Local Agent v1.3.22

1. VisionQC_Agent_Installer_v1.3.22.exe를 실행합니다.
2. 설치 프로그램은 실행 중인 기존 Agent를 종료하고 프로그램 파일을 제거한 뒤 새 파일을 설치합니다.
3. 기존 data, logs, output과 VPDL Worker 선택값은 유지됩니다.
4. Simulation 실행 중 약 2초 간격으로 해당 실행의 새 SQLite 결과만 대시보드에 추가합니다.
5. 실시간 SSE 상세 행은 DB 동기화 시작 후 중복 집계하지 않으며 완료 시 전체 저장 건수를 다시 검증합니다.
6. 통합 results_YYYYMMDD_HHMMSS.csv는 검사 이력의 대용량 CSV 직접 저장에 그대로 사용할 수 있습니다.
7. DB 전체 삭제는 검사 이력 화면에서 실행합니다. 원본 이미지와 CSV 파일은 삭제하지 않습니다.

Cognex VPDL Runtime과 라이선스 및 Workspace는 설치 패키지에 포함되지 않습니다.
