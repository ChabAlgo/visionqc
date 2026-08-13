VisionQC Web v4.4.9 변경사항

1. 메인 Position별 Tool NG 구성의 최소 Score 기준 변경
- NG Image 폴더에 실제 이미지가 존재하고 CSV의 동일 Cell ID + Position과 매칭되는 데이터만 사용
- 해당 Tool의 원본 결과가 NG이며 Score가 숫자인 값 중 최솟값 표시
- 조건에 맞는 값이 없으면 - 표시
- 화면 문구를 원본 NG 최소에서 실제 NG 최소로 변경

2. Input Cell ID의 Select Folder 수정
- JavaScript로 숨은 input.click()을 호출하는 방식 제거
- 실제 file input을 버튼 전체에 직접 배치하여 Chrome 프로필별 클릭 실패 방지

3. 분류 이미지 전환 시 화면 초점 고정
- 다음 이미지로 넘어갈 때 확대율 100%, Fit 상태, 이동 위치 0으로 초기화
- 이전 이미지의 확대/드래그 위치가 다음 이미지에 이어지는 현상 제거
