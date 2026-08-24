# VisionQC v4.7.1 / Local Agent v1.2.1

## 핵심 변경

- 메인 날짜별 검사 NG율을 이력 검색 필터에서 분리하고, 시뮬레이션·CSV 결과만 전체/NG로 집계합니다.
- 기존 Gemini 기반 AI SUGGEST를 제거하고 현재 Runtime File Load로 준비된 Green Tool의 1회 검사로 연결했습니다.
- 별도 AI 검사 메뉴를 제거했습니다.
- Tool별 Score 점 클릭 Viewer에 원본/크롭 및 Tool별 Green Heatmap 전환 버튼을 추가했습니다.
- 실제 NG 폴더 선택은 결과 CSV가 있으면 필요한 Position·Cell ID만 지연 색인해 대용량 원본 폴더의 파일 읽기와 메모리 사용을 줄였습니다.
- 실제 NG 검출 최소 Score에 ‘다른 Tool NG 제외 기준’(기본 0.80)을 추가했습니다.

## 이력 보존 정책

SQLite에 새로 저장되는 대상은 시뮬레이션과 사용자가 저장한 CSV 결과뿐입니다. 원본 이미지 바이트는 저장하지 않고 FullPath/Heatmap OverlayPath만 기록합니다. 이전 단발 검사 이력은 삭제하지 않지만 화면의 집계·조회에서는 제외합니다.

상세 기준은 [USER_REQUESTS_V4.7.1_KR.md](USER_REQUESTS_V4.7.1_KR.md)를 참고합니다.
