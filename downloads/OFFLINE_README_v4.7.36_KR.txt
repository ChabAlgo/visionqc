VisionQC v4.7.36 / Local Agent v1.3.23

1. VisionQC_Agent_Installer_v1.3.23.exe를 실행합니다.
2. 설치 프로그램은 실행 중인 기존 Agent를 종료하고 프로그램 파일을 제거한 뒤 새 파일을 설치합니다.
3. 기존 data, logs, output과 VPDL Worker 선택값은 유지됩니다.
4. Simulation 실행 중에는 새 SQLite 결과만 Cell·Position·Tool 통계에 증분 반영합니다.
5. 동일 Cell·Position 중복행도 전체 재계산과 같은 방식으로 병합합니다.
6. 완료 시 Agent 처리·SQLite 저장·조회·화면 변환 건수가 모두 일치해야 전체 결과로 교체합니다.
7. 통합 results_YYYYMMDD_HHMMSS.csv는 검사 이력의 대용량 CSV 직접 저장에 그대로 사용할 수 있습니다.
8. DB 전체 삭제는 검사 이력 화면에서 실행합니다. 원본 이미지와 CSV 파일은 삭제하지 않습니다.

Cognex VPDL Runtime과 라이선스 및 Workspace는 설치 패키지에 포함되지 않습니다.
