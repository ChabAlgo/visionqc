# v4.7.10 사용자 요청 기록

## 요청 원문 요약

- Save as JPEG, Skip Existing 옵션의 의미를 마우스 도움말로 확인할 수 있게 한다.
- 통합 시뮬레이션 결과를 클릭했을 때 Crop 대신 사용자가 선택한 원본 Grab 이미지를 기본으로 표시한다.
- 분석 화면의 이미지 Viewer에서 Green Heatmap을 표시할 수 있게 한다. 이후 추가 요청으로 재검사·생성 UI는 제거하고, 검사 시 저장된 Overlay만 표시한다.- Simulation 실행 중 분석 화면의 Tool 드롭다운이 자동으로 닫히지 않게 한다.

## 반영 기준

- Save as JPEG: Blue Crop 결과를 JPEG(.jpg)로 저장하고 JPEG Quality 값을 적용한다.
- Skip Existing: 같은 출력 경로에 Crop이 있으면 Blue Crop 계산·저장을 건너뛰고, 통합 모드에서는 그 Crop을 Green 입력으로 사용한다. 원본 또는 Crop 조건을 바꾼 경우 해제 후 재실행한다.
- 통합 Green 결과는 원본 경로 FullPath와 Green이 실제 검사한 Crop 경로 ProcessedPath를 함께 기록한다.
- Viewer 기본 탭은 원본이며, 보존된 Crop이 있을 때만 Crop 탭을 제공한다. Green 검사 시 저장된 Tool별 Heatmap Overlay가 있을 때만 해당 탭을 제공하며, Viewer에서 재검사·생성은 하지 않는다.- 분석 드롭다운이 열린 동안 실시간 결과 수신은 모델만 갱신하고, 드롭다운을 닫은 뒤 화면을 한 번 갱신한다.

## 이력 및 호환성

- SQLite images.processed_path 열을 추가한다. 기존 DB는 시작 시 열을 추가하는 migration을 수행한다.
- 새 통합 실행, 결과 CSV 분석, SQLite 이력은 원본/Crop 경로를 함께 다룬다.
- 과거 이력에는 원본 경로가 저장되어 있지 않아 기존 Crop 경로만 남아 있을 수 있다. 이미 삭제된 Crop 파일은 복구할 수 없다.
- 결과 Crop 이미지 유지를 해제한 새 실행은 원본 Viewer는 사용할 수 있지만 Crop 탭을 사용할 수 없다. 저장된 Overlay가 없으면 Heatmap 탭도 표시되지 않는다.
## 추가 요청 반영 (v4.7.10 - 원본 Viewer 및 테마 보완)

- `Save as JPEG`, `Skip Existing` 체크박스와 라벨 모두에 동일한 마우스 도움말을 연결한다.
- Viewer는 새 통합 시뮬레이션의 원본 Grab `FullPath`를 기본으로 표시한다. 과거 결과가 `_VisionQC_Integrated_Images` 또는 `_VisionQC_BlueCrop_Temp`의 경로만 가진 경우에는 현재 입력 루트의 `Source_번호_폴더명` 태그를 사용해 원본 경로를 즉시 복원한다. 입력 루트가 달라졌거나 태그가 없으면 잘못된 추측을 하지 않고 기존 경로를 유지한다.
- 분석/이력 Viewer의 `Green Heatmap 생성` UI는 표시하지 않는다. 검사 실행 시 저장된 Tool별 Overlay 경로가 있을 때만 해당 Heatmap 탭을 표시한다.
- 분류 화면 Inspection Asset 파일명은 줄임표 없이 전체를 표시한다.
- 화이트 모드의 보조 텍스트와 분류 화면 텍스트 대비를 어두운 청색 계열로 강화하고, 다크 모드 날짜 입력의 캘린더 아이콘은 밝게 표시한다.
- 원본 경로 복원은 폴더 재탐색을 수행하지 않으므로 대량 Grab 입력 폴더의 성능에 영향을 주지 않는다.

## 추가 요청 반영 (대시보드·Viewer·검사 이력 정리)

- 메인 결과는 FHD 이상 화면에서 날짜별 NG율, Cell 결과, Position 결과, Tool 구성, 미검 목록을 2열 압축 배치하여 한눈에 비교한다.
- 날짜별 NG율은 0~100% 선 그래프로 표시하고 각 점에 퍼센트를 직접 표시한다.
- 시뮬레이션 중 Tool 드롭다운이 열려 있으면 실시간 화면 갱신이 드롭다운을 닫지 않도록 상태를 보존한다.
- 분석 이미지 Viewer의 좌우 이동 버튼은 이미지와 분리된 Overlay 레이어에 놓고, 원본·Crop·Heatmap 전환 및 이미지 이동 시 확대 배율과 이동 좌표를 유지한다.
- 실제 NG 폴더에서 선택된 이미지에만 `이미지 제외` 버튼을 표시한다. 확인 후 원본을 삭제하지 않고 선택한 실제 NG 루트의 `DELET` 폴더로 이동하며, 같은 이름은 숫자 접미사로 충돌을 방지한다. `DELET` 폴더는 이후 실제 NG 색인에서 제외한다.
- 분류 화면 하단의 중복 `ORGANIZE FOLDER` 버튼을 제거하고, 확대·이동 후 방향키로 다음 이미지로 전환해도 정규화된 중심 좌표를 복원한다.
- 검사 이력 Cell ID는 줄바꿈·쉼표·공백으로 최대 10,000개까지 입력한다. Position·Tool·검사 모드·Workspace는 SQLite에 저장된 선택 목록을 사용한다.
- SQLite는 이미지별 `workspace_type`, `workspace_name`, `workspace_key`를 추가하며, 중복 집계 기준을 Cell ID + Position + Workspace로 확장한다.
- 다크 모드 날짜 입력의 달력 아이콘을 밝게 표시하고, 분석 Viewer의 Green Heatmap 재검사·생성 UI는 완전히 제거한다.
