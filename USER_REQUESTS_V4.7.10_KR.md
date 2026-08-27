# v4.7.10 사용자 요청 기록

## 요청 원문 요약

- Save as JPEG, Skip Existing 옵션의 의미를 마우스 도움말로 확인할 수 있게 한다.
- 통합 시뮬레이션 결과를 클릭했을 때 Crop 대신 사용자가 선택한 원본 Grab 이미지를 기본으로 표시한다.
- 분석 화면의 이미지 Viewer에서도 Green Heatmap을 생성·표시할 수 있게 한다.
- Simulation 실행 중 분석 화면의 Tool 드롭다운이 자동으로 닫히지 않게 한다.

## 반영 기준

- Save as JPEG: Blue Crop 결과를 JPEG(.jpg)로 저장하고 JPEG Quality 값을 적용한다.
- Skip Existing: 같은 출력 경로에 Crop이 있으면 Blue Crop 계산·저장을 건너뛰고, 통합 모드에서는 그 Crop을 Green 입력으로 사용한다. 원본 또는 Crop 조건을 바꾼 경우 해제 후 재실행한다.
- 통합 Green 결과는 원본 경로 FullPath와 Green이 실제 검사한 Crop 경로 ProcessedPath를 함께 기록한다.
- Viewer 기본 탭은 원본이며, 보존된 Crop이 있을 때만 Crop 탭을 제공한다. Heatmap 재생성은 Crop을 우선 사용한다.
- Simulation 실행 중에는 Runtime을 공유할 수 없으므로 Heatmap 생성 버튼을 비활성화하고 완료 후 생성하도록 안내한다.
- 분석 드롭다운이 열린 동안 실시간 결과 수신은 모델만 갱신하고, 드롭다운을 닫은 뒤 화면을 한 번 갱신한다.

## 이력 및 호환성

- SQLite images.processed_path 열을 추가한다. 기존 DB는 시작 시 열을 추가하는 migration을 수행한다.
- 새 통합 실행, 결과 CSV 분석, SQLite 이력은 원본/Crop 경로를 함께 다룬다.
- 과거 이력에는 원본 경로가 저장되어 있지 않아 기존 Crop 경로만 남아 있을 수 있다. 이미 삭제된 Crop 파일은 복구할 수 없다.
- 결과 Crop 이미지 유지를 해제한 새 실행은 원본 Viewer는 사용할 수 있지만 Crop 탭 및 나중의 Heatmap 재생성은 사용할 수 없다.