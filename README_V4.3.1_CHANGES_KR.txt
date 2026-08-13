VisionQC Web v4.3.1 변경 사항

1. 실제 NG 폴더 스캔 보강
- 선택한 폴더 자체가 CA(TOP), AN(TOP), CA(BOT), AN(BOT)인 경우도 인식
- 괄호/공백/밑줄 차이에 강한 Position 인식
- CSV와 실제 NG 이미지 Cell ID 매칭 수, 미매칭 수, 샘플 ID 표시

2. Tool별 NG 집계 변경
- 전체 Position 통합 Tool NG율 제거
- Position별 Total_result=NG Cell을 분모로 사용
- 각 Tool의 NG Cell 수와 비율을 원형 링 그래프로 표시
- 복수 Tool 동시 NG가 가능하므로 비율 합계는 100%를 초과할 수 있음

3. 테스트 주의
- 랜덤 생성 CSV Cell ID는 실제 NG 이미지 파일의 Cell ID와 다르면 매칭되지 않음
