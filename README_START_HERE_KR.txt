VisionQC DirectExport v4.4.3 STATIC
================================

실행 방법
1. 압축을 완전히 풉니다.
2. run_app.bat을 실행합니다.
3. 자동으로 열리는 주소를 사용합니다.
   http://127.0.0.1:3021/?v=4.4.3
4. 프로그램 사용 중에는 검은 CMD 창을 닫지 마세요.

필요 조건
- Node.js 설치 필요
- Chrome 또는 Edge 권장
- npm install / Vite / 인터넷 연결 필요 없음

v4.3 메뉴
- 메인: Cell별 → Position별 → Tool별 NG율, Position별 미검 목록
- 분석: Tool별 OK/NG Score 분포, 평균·최소·최대·중앙값
- 분류: 기존 이미지 분류 기능 전체
- 설정: Position별 결과 파일과 실제 NG 이미지 폴더 입력

분석 Input
1. Position별 결과 파일
   - CA(TOP), AN(TOP), CA(BOT), AN(BOT)
   - CSV 또는 XLSX
   - 네 Position을 모두 넣을 필요 없이 1개 이상만 입력 가능
   - 필수 열: Cell ID, Total_result
   - Tool 열: {Tool명}_result, {Tool명}_score

2. 실제 NG 이미지 폴더
   실제_NG_이미지\CA(TOP)\...
   실제_NG_이미지\AN(TOP)\...
   실제_NG_이미지\CA(BOT)\...
   실제_NG_이미지\AN(BOT)\...
   각 Position 폴더 아래의 하위 폴더도 함께 검색합니다.

판정 기준
- Cell NG율: 입력된 Position 중 하나라도 Total_result=NG인 고유 Cell 비율
- Position NG율: 해당 Position에서 Total_result=NG인 비율
- Tool NG율: 해당 Tool의 *_result=NG 비율
- 미검: 실제 NG 이미지가 있고 같은 Cell ID+Position의 Total_result=OK인 경우
- 과검, Tool별 미검, Lot 분석은 포함하지 않음
- Cell ID: 파일명/값에서 J, P, B 중 하나로 시작하는 첫 16자리 영숫자 추출

기존 분류 기능
- 작업 자동 저장 및 새로고침 복원
- ←/↑ 이전 이미지, →/↓ 다음 이미지
- 빠른 휠 확대 시 최초 포인터 중심 유지
- 기존 Class Settings, Organize, Export 기능 유지

주의
- 포트 3021을 고정 사용합니다. 이미 사용 중이면 기존 VisionQC CMD 창을 닫고 다시 실행하세요.
- 브라우저 권한이 해제되면 설정 메뉴에서 결과 파일 또는 NG 폴더를 다시 선택해야 합니다.


v4.4.3 추가 변경
- 메뉴 버튼: 브라우저 기본 checkbox/label + 직접 pointer 이벤트 이중 처리
- 상단 기능 버튼 오른쪽 정렬
- Label Reset 버튼 및 확인창 추가
