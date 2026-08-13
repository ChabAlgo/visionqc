VisionQC DirectExport v4.3 변경 사항
====================================

1. 상단 UI
- 좌측 메뉴 버튼 확대 및 '메뉴' 명칭 표시
- 버전 아래 줄에 자동 저장 상태가 유지되도록 헤더 레이아웃 수정
- Progress 영역 고정
- Tool 통계와 우측 작업 버튼을 수평 스크롤 가능하게 수정

2. 슬라이드 메뉴
- 메인 / 분석 / 분류 / 설정 페이지 추가
- 페이지를 전환해도 기존 분류 화면과 작업 상태를 유지

3. 메인 대시보드
- Cell별 NG율
- Position별 NG율
- Tool별 NG율
- Position별 실제 NG 검출/미검 현황
- 미검 Cell ID 목록
- Cell ID 클릭 시 실제 NG 이미지 팝업
- 동일 Cell의 이미지가 여러 장이면 이전/다음 탐색
- 미검 Cell의 Tool별 OK Score 표시

4. 세부 분석
- Tool, Position, 분석 범위 필터
- OK/NG Score 히스토그램
- Cell별 Score 정렬 그래프
- 평균, 최소, 최대, 중앙값 표시
- 실제 NG를 NG로 검출한 데이터와 미검 Cell의 OK Score를 별도 확인

5. 설정
- CA(TOP), AN(TOP), CA(BOT), AN(BOT) 결과 파일 개별 입력
- CSV/XLSX 지원, 일부 Position만 입력 가능
- *_result 및 *_score 열 자동 인식
- 실제 NG 이미지 루트 폴더 선택 및 하위 폴더 재귀 검색
- 파일/폴더 핸들을 IndexedDB에 저장해 새로고침 후 복원 시도
- 중복, Position 불일치, Cell ID 추출 실패 등 경고 표시

6. 제외 항목
- 과검 분석
- Tool별 미검 판정
- Lot 분석
- VPDL Runtime
- Live Camera
