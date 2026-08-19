# DL_Simulation v1.13 → VisionQC v4.4.23 이식 점검

기준: 사용자가 제공한 `DL_Simulation_v1.13_cell_position_summary_source`.

## Green
- Workspace / Image Root / Stream
- Cell ID CSV Filter
- Keyword Mode / Keyword Input Root / Position Keyword
- GPU / GPU Devices
- JPEG Quality / Keep Subfolders / HeatMap Save
- HeatMap Alpha / Alpha Cut / Force Jet
- Progress Update(PrintEvery)
- ToolName / Threshold / Judgement
- Judgement Priority

원본 Tool UI의 Position별 Tool 적용 체크박스는 사용자 요청으로 제거했습니다. Tool은 해당 Simulation 모드에서 체크된 모든 Position에 동일하게 적용합니다.
원본 ROI는 GUI에서 직접 입력하는 컬럼이 아니라 ToolName에 따른 기본 ROI를 내부 사용하므로 엔진 동작을 유지합니다.

## Blue
- Runtime Workspace / Image Root / Stream / Blue Tool
- GPU / GPU Devices
- Keep Subfolders / Save as JPEG / Skip Existing / JPEG Quality
- Progress Update(PrintEvery)
- Crop Width / Height
- Expected X Min / Max / Max Y Diff
- Position별 Fallback Shift X/Y
- Preview ROI X/Y/W/H
- Sample Image / Fallback Preview

## Integrated
- Green Workspace는 Green 단독 화면과 공유
- Blue Workspace는 Blue 단독 화면과 공유
- Integrated Image Root는 Blue Crop Image Root와 공유
- Cell ID CSV / Keyword / Keep Crop / HeatMap Save
- Green/Blue 상세 옵션은 같은 설정 객체를 사용

## Position
- 공통 Custom Position 목록을 Settings / Main / Analysis / Simulation / Result Input / 실제 NG 경로에서 공유
- Green / Blue / Integrated별 실행 여부 checkbox는 유지
- Tool별 Position 적용 checkbox만 제거
