# VisionQC v4.7.8 / Local Agent v1.2.3

## 수정 사항

- 분석의 Cell별 Score 그래프에서 원본 이미지를 연 뒤, 기존 Heatmap 경로가 없으면 `Green Heatmap 생성` 버튼으로 현재 사전 로드된 Runtime을 사용해 해당 이미지 1장을 다시 검사할 수 있습니다.
- 생성된 Tool별 Overlay는 같은 Viewer의 Heatmap 탭으로 즉시 전환합니다. 단일 검사 결과는 SQLite 이력에 자동 저장하지 않습니다.
- CSV FullPath가 이미 삭제·이동되어 Agent가 찾을 수 없는 경우, 빈 화면 대신 `이미지 파일을 찾을 수 없습니다` 원인을 표시합니다.
- Grab 이미지의 `.jpg` 자체는 기존대로 지원합니다. 이번 확인에서는 화면의 BlueCrop 임시 폴더가 실제로 존재하지 않았습니다.

## 사용 조건

- 이미지 파일이 현재 CSV FullPath 위치에 실제로 존재해야 합니다.
- Green 또는 Integrated 모드에서 해당 Position의 Runtime File Load가 완료되어 있어야 합니다.
- Blue 전용 Runtime에는 Green Heatmap을 생성하지 않습니다.

## 배포물

- `VisionQC_Agent_Installer_v1.2.3.exe`
- `VisionQC_Offline_v4.7.8.zip`
