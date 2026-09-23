# VisionQC 4.8.6 / Agent 1.4.6

- 메인 대시보드의 날짜별 검사 NG율 그래프와 집계를 날짜 + Cell ID 기준으로 변경합니다.
- 같은 날짜의 같은 Cell은 여러 Position에서 검사해도 한 건입니다. 선택한 Position 중 하나라도 Threshold 적용 후 NG이면 NG Cell 한 건으로 계산합니다.
- 동일 Cell ID라도 날짜가 다르면 별도 검사 건입니다. 전체보기는 날짜별 검사 Cell 건수를 합산합니다.
- 날짜 선택, Position 선택, Threshold 변경에 같은 규칙을 적용합니다. PDF의 날짜별 그래프도 Cell 기준으로 맞춥니다.
- 기본 전체 Position 경로는 기존 Agent Cell 증분 카운터를 재사용합니다. CSV 상세행과 Position별 검사 결과, Tool별 NG CSV 형식은 유지합니다.
- 이 집계에는 Agent 1.4.6이 필요합니다. 이전 Agent 연결 시 Position 집계를 Cell로 잘못 표시하지 않고 업데이트 안내를 표시합니다.
