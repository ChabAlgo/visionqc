# DL_Simulation v1.13 → VisionQC v4.4.21 이식 점검

기준 소스: `DL_Simulation_v1.13_cell_position_summary_source`

## 1. Green Simulation
- Position별 사용 여부: 이식. 기존 checkbox를 유지하며 Blue 모드 실행 여부를 제어합니다.
- Custom Position 목록은 Green/Integrated/Settings/Analysis와 공통입니다. 기존 checkbox를 유지하며 Green/Blue/Integrated 모드별 실행 여부를 독립적으로 체크합니다.
- Custom Position: Web 공통 Position 목록에 자유롭게 추가/이름변경/삭제하며 Main/Analysis/Settings/Simulation/Tool 설정과 동기화합니다.
- Position Workspace / Image Input / Stream: 이식.
- Cell ID CSV Filter: 이식.
- Keyword Mode: 이식.
  - Common Image Input Root
  - Position별 Keyword
  - 대소문자 무시 동작은 기존 Engine 유지.
- 하위 폴더 구조 유지: 이식.
- GPU 사용 / GPU 번호 CSV: 이식.
- JPEG Quality: 이식.
- HeatMap Image Save: 이식.
- HeatMap 흑백이면 Jet 컬러맵 적용: 이식.
- HeatMap Alpha(%): 이식.
- Alpha Cut: 이식.
- 진행 표시 주기: `Progress Update`로 이식.
- Tool Settings: 이식.
  - 현재 공통 Custom Position 전체를 동적 column으로 표시하고 Tool별 적용 위치를 체크
  - ToolName
  - Threshold
  - Judgement
  - Tool 추가 / 선택 제거 / 원본 기본값
- Judgement Setting: 이식.
  - Priority / Name
  - 추가 / 삭제 / 순서 변경
  - ERROR 유지
- 원본 Tool 기본 ROI: 기존 Engine의 ToolName별 기본 ROI lookup을 그대로 유지.

## 2. Blue Crop
- Position별 사용 여부: 이식.
- Position Blue Workspace / Original Image Folder / Stream / Blue Tool: 이식.
- GPU 사용 / GPU 번호 CSV: 이식.
- 하위 폴더 구조 유지: 이식.
- Save as JPEG: 이식.
- Skip Existing: 이식.
- JPEG Quality: 이식.
- 진행 표시 주기: `Progress Update`로 이식.
- Crop Width / Crop Height: 이식.
- Expected X Min / Expected X Max: 이식.
- Max Y Diff: 이식.
- Tool별 Fallback Shift X/Y: 이식.
- Preview X/Y/W/H: 이식.
- Sample Image 선택: 이식.
- Fallback Preview: Agent API + Web Preview Modal로 이식.

## 3. Integrated Simulation
- Integrated Position 선택: 공통 Custom Position 목록을 사용하고, checkbox로 Integrated 모드 실행 여부만 독립 제어합니다.
- Integrated Cell ID CSV: 이식.
- Keyword Mode / Common Input Root / Position Keyword: 이식.
- Blue Crop 이미지 저장: 이식.
- HeatMap 이미지 저장: 이식.
- Blue → Green Streaming: 원본 `IntegratedSimulationProcessor.RunStreaming` 유지.
- Green Workspace: Green standalone과 동일 state 공유.
- Blue Workspace: Blue standalone과 동일 state 공유.
- Integrated 원본 Image Folder: Blue Crop의 `blueImageRoot`와 동일 state 공유.
- Green Image Folder: Green standalone 전용으로 별도 유지.

## 4. Output / Runtime UI 배치
- Output Folder는 요청대로 메인 컬럼에 유지.
- 원본 하단 Runtime / HeatMap / Blue Runtime / Crop / Fallback 옵션은 오른쪽 `Simulation Options`에 통합.
- Green / Blue / Integrated 모드에 필요한 옵션만 해당 모드에서 표시.

## 5. 설정 저장
- 원본 WinForms의 `DL_Simulation_v1.13_defaults.txt` 직접 저장 대신 Web `localStorage`에 현재 설정 자동 유지.
- `기본값 저장`: 현재 전체 Simulation 설정을 사용자 기본값으로 저장.
- `기본값 복원`: 저장한 기본값이 있으면 복원, 없으면 DL_Simulation v1.13 원본 기본값 복원.

## 6. Live Analysis
CSV 파일을 중간에 읽지 않습니다.

원본 Green Engine은 이미지 한 장 처리 직후 이미 다음 값을 메모리에 가지고 있습니다.
- Cell ID
- Position
- total_result
- Tool별 result
- Tool별 score

Agent v0.2.1이 각 이미지의 상세 결과 객체를 내부 버퍼에 저장하고 `Progress Update = N`이면 N개가 쌓였을 때 SSE `analysis` 이벤트 한 번으로 Web에 Batch 전송합니다.

예: `Progress Update = 100`
- VPDL 검사는 1장씩 계속 처리
- Agent는 100장 상세 결과를 메모리에 누적
- 100장마다 Web에 100행을 한 번에 전달
- VisionQC 기존 `rebuildModel()`을 호출하여 Main Cell NG율 / Position NG율 / Tool NG 구성 / 분석 화면을 갱신
- 마지막 100장 미만 잔여 Batch는 Complete / Stop / Error 시 전송

따라서 실시간 분석을 위해 CSV 파일을 열거나 이미지마다 Browser 통신을 할 필요가 없습니다.

## 7. Engine 변경 범위
`GreenOverlayProcessor.cs`, `BlueCropCore.cs`의 VPDL 검사/판정/Crop/Fallback 핵심 로직은 원본 v1.13을 유지합니다.
추가된 부분은 주로:
- per-image Progress 정보
- LiveAnalysisRecord 생성
- Web 전달용 진행 상태
입니다.

## 8. 의도적으로 동일하지 않은 GUI 부분
- 원본 WinForms GUI 자체는 사용하지 않음.
- 파일/폴더 Picker는 Local Agent Native Shell Picker 사용.
- Log / Progress는 VisionQC Simulation Status로 통합.
- Position checkbox는 유지. Position 자체의 추가/삭제/이름변경은 공통 Position 구성으로 제공하고 checkbox는 모드별 실행 여부만 담당.
