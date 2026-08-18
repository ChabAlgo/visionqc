# VisionQC Changelog

## v4.4.15

- Input Cell ID의 Select Folder를 명시적 버튼 + Chrome Directory Picker 방식으로 수정
- 폴더 선택 시 전체 FileList를 즉시 만들지 않고 DirectoryHandle만 보관
- Load Matched Images 실행 시 파일명을 순회하고, 입력 Cell ID 전체 문자열이 파일명에 포함된 이미지에 대해서만 File 객체 로드
- J/P/B 시작 또는 16자리 추출 규칙을 Input Cell ID 매칭에 사용하지 않음
- Simulation 텍스트 크기는 유지하면서 텍스트박스 높이/패딩 축소
- Simulation 체크박스 16x16으로 축소
- Stream / Blue Tool 입력박스의 배경/테두리 색상을 Workspace/Image Folder 입력박스와 통일

## v4.4.14

- 좌측 메뉴에 `시뮬레이션` 페이지 추가
- Integrated Simulation / Green Simulation / Blue Crop UI 추가
- Local Agent 상태 표시 및 `127.0.0.1:17891/api/status` 연결 확인 골격 추가
- Workspace / Image Folder / Stream / GPU / Output / Progress / Log UI 추가
- 실제 VPDL Runtime 실행은 아직 연결하지 않음 (다음 단계에서 Local Agent 구현)
- 기존 분류/메인/분석/설정 기능은 React 번들 변경 없이 유지

## v4.4.12

- 이미지 전환 시 확대율 유지
- 이미지 전환 시 드래그/화면 위치 유지
- 메인 Position별 Tool NG 구성의 실제 NG 최소 Score 기준 유지
- 로컬 이미지/CSV 처리 구조
- GitHub Pages 배포용 상대경로 구성

> 이 파일에는 안정 배포 버전만 기록하는 것을 권장합니다.
