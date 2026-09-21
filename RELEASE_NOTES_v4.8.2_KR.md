# VisionQC 4.8.2 / Local Agent 1.4.2

## 화면 및 CSV 선택 저장

- Simulation Output의 CSV당 최대 행 수 입력을 다크/라이트 테마에서 읽을 수 있도록 배경·글자·테두리 색을 지정했습니다.
- Position별 Tool NG 구성의 Threshold 옆에 `CSV / Download` 버튼을 추가했습니다. 공간이 좁으면 다음 줄로 배치합니다.
- Tool CSV는 현재 선택 날짜(전체보기는 모든 날짜), 해당 Position, 해당 Tool의 **Threshold 적용 후 NG Cell**만 저장합니다. 날짜 + Cell ID + Position 기준 한 행이며, 화면의 NG 수량과 일치합니다. 원본 중복 행은 Source_Row_Count로 표시합니다.
- 날짜별 검사 NG율에 Position 체크박스와 CSV Download를 추가했습니다. 체크한 Position들의 합산 그래프와 KPI를 표시하며, CSV는 선택 날짜의 OK/NG 전체 Cell을 저장합니다. 전체보기는 모든 날짜를 저장합니다. 모든 Position을 해제하면 빈 결과이며, 전체 데이터로 되돌아가지 않습니다.
- 메인 그래프의 Position 선택은 해당 그래프·KPI·날짜 CSV에 적용됩니다. 다른 Position 카드와 개별 Tool 버튼은 각 카드의 Position을 유지합니다.
- 검사 이력에서도 여러 Position을 선택할 수 있습니다. 날짜 점을 누르면 해당 날짜로 검색하며, CSV는 날짜·Position·Workspace·Tool·Cell ID 등 현재 검색 조건에 맞는 전체 결과를 저장합니다. 화면 페이지 크기에 제한되지 않습니다.

## 데이터와 대용량 처리

- Agent 분석에서는 전체 데이터를 브라우저로 반환하지 않고 SQLite를 순차 조회하여 CSV를 저장합니다.
- CSV당 행 수와 날짜별 분할은 Simulation Output 설정을 그대로 사용합니다. 저장할 폴더를 선택하면 일반 CSV 파일을 생성합니다.
- Tool/날짜 CSV는 집계 결과입니다. 같은 날짜·Cell·Position에 여러 관측이 있을 수 있으므로 Time은 비워 두며, Tool 결과·대표 Score·Threshold·원본 행 수를 포함합니다. 원본 관측 전체는 기존 `전체 결과 CSV 저장`을 사용합니다.
- 검사 이력 CSV는 검색 화면과 같은 중복 제거 기준(날짜·Cell ID·Position·Workspace와 최신 기록)을 사용하고 Date, Time, 원본 경로, Workspace 및 Tool별 결과·Score를 포함합니다.
- 새 Position 필터와 CSV 기능은 Agent 1.4.2가 필요합니다. 구버전 Agent가 필터를 무시한 채 전체 데이터를 저장하지 않도록 새 기능의 실행을 제한합니다.

## 검증

- 중복 관측이 있는 2개 Position × 2개 날짜 테스트: Tool NG 17개 → CSV 17행, Threshold 변경 후 10개 → 10행.
- 전체/선택 날짜, 다중/빈 Position, OK 포함 날짜 CSV, 7행 단위 파일 분할 확인.
- 검사 이력은 10건 페이지 조회 상태에서도 조건에 맞는 20건 전체 CSV 저장 확인.
- 브라우저 회귀 테스트와 SQLite 정합성 테스트, 에이전트 HTTP 경로를 통한 저장·조회 검증.
- 이번 변경은 추론 엔진·ROI·GPU 배분·이미지 디코딩을 변경하지 않습니다. 이 릴리스에서 수백만 건의 실제 생산 데이터 검사는 새로 수행하지 않았습니다.
