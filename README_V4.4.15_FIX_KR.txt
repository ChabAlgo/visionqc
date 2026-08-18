VisionQC v4.4.15 수정 사항

1) Input Cell ID > Select Folder
- 숨겨진 webkitdirectory input을 버튼 위에 덮는 방식 제거
- GitHub Pages/Chrome에서는 showDirectoryPicker()를 직접 호출
- 폴더 선택 즉시 모든 파일을 File 객체로 만들지 않음
- Load Matched Images를 누를 때 폴더를 순회하며 파일명에 입력 Cell ID 전체 문자열이 포함된 이미지만 실제 File 객체로 로드
- Cell ID는 J/P/B 또는 16자리 규칙을 사용하지 않음

2) Simulation UI
- 텍스트박스 높이 34px로 축소
- 체크박스 16x16으로 축소
- Stream / Blue Tool 입력박스의 색상/테두리를 다른 입력박스와 통일
- 텍스트 자체는 v4.4.14보다 작게 되돌리지 않음
