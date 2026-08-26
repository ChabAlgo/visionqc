# VisionQC v4.7.9 / Local Agent v1.2.3

## 수정 사항

- 화이트 모드에서 Runtime File Load 중 `구조 읽는 중`, 로드 대기/완료 영역이 어두운 배경으로 보이던 문제를 수정했습니다.
- 화이트 모드 Simulation 탭의 현재 선택 모드를 하늘색 배경, 청록 테두리, 하단 강조선으로 표시합니다.
- Integrated Simulation의 Crop 결과는 기본적으로 Output 폴더의 `_VisionQC_Integrated_Images`에 보존됩니다. 결과 CSV의 `FullPath`가 이 보존 경로를 가리켜 이미지 Viewer와 Green Heatmap 재생성을 사용할 수 있습니다.
- 설정의 `결과 Crop 이미지 유지 (Viewer용)`을 해제하면 저장 공간을 줄이는 대신 실행 직후 Crop 파일이 삭제되므로 해당 실행 결과의 이미지 Viewer를 사용할 수 없습니다.
- 기존 v4.7.8 이하의 로컬 Simulation 설정은 첫 실행 시 Viewer용 보존을 켜는 방향으로 한 번만 이관합니다. 이미 삭제된 과거 `_VisionQC_BlueCrop_Temp` 파일은 복원되지 않습니다.

## Grab 이미지 안내

- `.jpg` Grab 이미지는 기존 Agent에서 지원합니다.
- Viewer가 `이미지 파일을 찾을 수 없습니다`라고 표시하면 CSV의 `FullPath`에 적힌 실제 파일이 이미 이동/삭제되었는지 먼저 확인합니다.

## 배포물

- `VisionQC_Agent_Installer_v1.2.3.exe`
- `VisionQC_Offline_v4.7.9.zip`

## 검증 범위

- 정적 회귀 테스트와 Chrome/Playwright UI 회귀 테스트
- Agent/Installer Release 재빌드 및 로컬 loopback Agent 실행 확인
- 실제 Grab/VPDL Runtime의 전체 통합 실행은 고객 PC의 현재 입력 이미지와 Runtime 라이선스 조건에서 재확인 필요
