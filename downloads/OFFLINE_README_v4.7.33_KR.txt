VisionQC v4.7.33 / Local Agent v1.3.20

1. VisionQC_Agent_Installer_v1.3.20.exe를 실행합니다.
2. 설치 프로그램은 실행 중인 기존 Agent를 종료하고 프로그램 파일을 제거한 뒤 새 파일을 설치합니다.
3. 기존 data, logs, output과 VPDL Worker 선택값은 유지됩니다.
4. 저장된 CSV/XLSX 결과를 다시 불러오면 이전 날짜 선택이 해제되고 메인 대시보드가 새 데이터 전체로 갱신됩니다.
5. 통합 results_YYYYMMDD_HHMMSS.csv는 검사 이력의 대용량 CSV 직접 저장에 그대로 사용할 수 있습니다.
6. DB 전체 삭제는 검사 이력 화면에서 실행합니다. 원본 이미지와 CSV 파일은 삭제하지 않습니다.
7. Position 독립 병렬 실행은 Simulation Options에서 켜고 동시 실행 수를 1~10으로 조절합니다. GPU 자동 배분을 켜면 감지된 GPU에 Position을 순환 배분하며 GPU가 하나여도 공유 실행합니다.
8. 대시보드 미검 Cell과 검사 이력 Cell 이미지 창에서 방향키로 이전·다음 Cell을 탐색할 수 있습니다.

Cognex VPDL Runtime과 라이선스 및 Workspace는 설치 패키지에 포함되지 않습니다.
