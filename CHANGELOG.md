# VisionQC v4.4.20

## Simulation / Local Agent integration
- 원본 React 상단 바를 `분류` 메뉴에서만 표시하도록 변경.
- DL_Simulation v1.13의 Green / Blue / Integrated 상세 파라미터를 Simulation Options 패널로 이식.
- Green / Blue / Integrated 각각 독립적인 Position 추가/삭제 지원.
- Green/Blue Workspace는 Integrated와 공유.
- Integrated Image Folder는 Blue Crop Image Folder와 공유; Green Image Folder는 별도 유지.
- Output Folder는 Simulation 메인 영역에 유지.
- Tool Settings / Judgement Setting / Keyword / Cell ID CSV / Runtime / HeatMap / Blue Crop / Fallback Preview 이식.
- Progress Update N개 단위 Live Analysis Batch 반영.
- Agent 상세 결과를 기존 VisionQC 분석 모델에 연결하여 Main Cell/Position/Tool NG 분석 실시간 갱신.
- Simulation 설정 localStorage 저장 + 사용자 기본값 저장/복원.

## Required Local Agent
- VisionQC Local Agent v0.2.0 이상 필요.
