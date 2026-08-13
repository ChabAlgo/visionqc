VisionQC v4.3.7

- 메뉴를 pointerdown이 아닌 단일 click 이벤트로만 토글하도록 수정
- 전역 pointerdown 캡처 제거: 메뉴를 누르고 뗄 때 닫히는 현상 해결
- 메인/분석/설정 페이지의 모든 버튼과 select에 직접 이벤트 연결
- 비분류 화면에서 기존 React main을 display:none 처리하여 투명 레이어 클릭 간섭 제거
- 확장 페이지를 최상위 인터랙션 레이어로 고정
- 상단 기능 버튼을 요청한 2줄 구조로 배치
  1행: Input Cell ID / Load Folder / Load Files
  2행: Label Reset / ZIP / Organize Folder / Export Folder
- 상단 통계 영역 가용 폭 확대
