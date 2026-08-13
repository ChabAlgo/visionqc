VisionQC v4.3.8 변경 사항

1. 미검 이미지 팝업 X 버튼을 직접 click 이벤트로 연결했습니다.
2. 이미지 드래그 pointer capture가 팝업 버튼을 가로채지 않도록 버튼 영역에서는 드래그를 시작하지 않습니다.
3. 분석 화면의 native select를 직접 제어하는 커스텀 드롭다운으로 교체했습니다.
4. 드롭다운은 한 번 클릭하면 열린 상태를 유지하고, 항목 선택 또는 바깥 클릭 시 닫힙니다.
5. Position → Tool → 분석 범위 순서를 유지했습니다.
6. Chromium에서 실제 mousePressed/mouseReleased 방식으로 X 버튼과 드롭다운을 테스트했습니다.
