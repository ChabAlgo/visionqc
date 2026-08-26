# v4.7.9 Integrated Viewer 결과 이미지 보존

## 목적

Integrated Simulation의 Blue Crop → Green 검사 결과를 CSV/분석/SQLite Viewer에서 다시 열 수 있도록, Green에 전달한 Crop 파일을 실행 직후 삭제하지 않고 Output 폴더에 보존합니다.

## 동작

- `integrated.keepCropImages=true`이면 `AgentServer.RunSimulation`이 `OutputRoot\_VisionQC_Integrated_Images`를 Crop Root로 사용합니다.
- Blue Crop 결과는 Position·Source 하위 구조를 보존해 저장됩니다.
- Green 결과의 `FullPath`는 이 실제 Crop 파일 경로이며, 이후 `/api/image/preview`와 `/api/classification/inspect`에서 같은 파일을 사용합니다.
- `integrated.keepCropImages=false`이면 기존 호환 동작처럼 `_VisionQC_BlueCrop_Temp`에 만들고, Green 검사 직후 파일과 폴더를 삭제합니다.

## 제한사항

- 이미 삭제된 과거 `_VisionQC_BlueCrop_Temp`의 파일은 이 변경으로 되살릴 수 없습니다.
- `keepCropImages=true`은 검사 건수만큼 Crop 이미지 저장 공간을 사용합니다. 대량 실행 시 Output 디스크 여유 공간을 확인합니다.
- 원본 Grab 이미지의 JPG/BMP/PNG/TIFF 지원 여부와 별개로, Viewer는 CSV `FullPath`의 실제 파일 존재 여부가 필요합니다.

## 유지보수 확인 위치

- 실행 진입: `AgentServer.cs`의 `mode == "integrated"` 분기
- Streaming 삭제 조건: `Engine/BlueCropCore.cs`의 `IntegratedSimulationProcessor.RunStreaming`
- Web 설정 이관: `visionqc-extension.js`의 `ensureSimulationForm`
- 회귀 테스트: `tests/v479-integrated-viewer.test.mjs`
