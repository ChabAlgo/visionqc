# VisionQC 사용자 요청 이력 · v4.7.1

기준일: 2026-08-24. 이 문서는 v4.7.0 이후의 요구사항·구현 기준·제한을 새 대화와 유지보수에 전달한다.

## 반영 요구사항

1. SQLite 일별 NG율은 **시뮬레이션 결과**와 사용자가 저장한 **CSV 분석/CSV 직접 가져오기**만 대상으로 한다. 단발 AI SUGGEST 검사는 DB 이력으로 남기지 않는다.
2. 메인 화면 날짜별 NG율은 이력 화면의 검색 조건과 분리한다. 이력 화면에서 NG만 검색해도 메인 차트는 전체 검사 건수를 분모로 유지한다.
3. 이미지 분류 화면의 기존 AI SUGGEST는 Gemini API를 호출하지 않는다. 이미 Runtime File Load로 로드해 둔 Green Tool을 1회 실행한다. 브라우저 File은 Local Agent 임시 파일로만 전달하고 검사 뒤 삭제한다.
4. 별도 `AI 검사` 메뉴/자동 Workspace 선택 페이지는 제거한다. AI SUGGEST는 현재 시뮬레이션 설정·사전 로드 Runtime이 일치할 때만 실행한다.
5. Tool별 Score 분석에서 점을 클릭한 이미지 Viewer에 Tool별 `Heatmap` 버튼을 표시한다. 시뮬레이션 결과의 OverlayPath를 우선 사용하고, CSV FullPath가 있으면 SQLite에서 같은 경로를 보완 조회한다.
6. 실제 NG 이미지가 매칭된 선택 Tool의 NG Score 중 조건 적용 최소값을 표시한다. 같은 행에서 다른 Tool도 NG이고 그 Score가 사용자 설정값(초기 0.80) 이상이면 최소값 후보에서 제외한다.
7. 실제 최종 NG 폴더는 결과 CSV가 먼저 있으면 결과에 존재하는 `Position + Cell ID`만 색인하고, 찾은 대상이 모두 충족되면 탐색을 끝낸다. 선택 시 원본 바이트를 읽지 않고 File Handle만 보관하며, 이미지 Viewer를 열 때만 파일을 읽는다.

## 사용·제한 사항

- 빠른 실제 NG 색인은 결과 CSV를 먼저 불러온 뒤 실제 NG 폴더를 지정할 때 적용된다. 결과 CSV 없이 지정하면 Cell ID 매칭 대상이 없으므로 전체 루트를 탐색해야 한다.
- 빠른 색인 모드에서는 결과 CSV에 매칭되는 실제 NG 이미지를 첫 번째 파일 기준으로 보관한다. 동일 Cell ID의 여러 NG 파일을 모두 비교해야 하면 기존처럼 해당 Position 전용 폴더를 지정하거나 결과 파일을 분리해 확인한다.
- AI SUGGEST는 Green 또는 Integrated 모드에서 Runtime File Load가 완료된 상태가 필요하다. Blue 전용 Runtime에는 실행하지 않는다.
- AI SUGGEST 임시 업로드는 최대 80MB이며, 원본 이미지 바이트와 단발 검사 결과는 SQLite에 저장하지 않는다.
- CSV만 분석한 경우에도 FullPath가 있고 해당 경로가 현재 PC에서 유효하면 원본 이미지를 열 수 있다. Green Heatmap Overlay는 해당 시뮬레이션 결과가 SQLite에 저장돼 있어야 보완 조회된다.

## 검증 기준

- 메인 차트 요청에는 `sourceTypes=[simulation,csv-import,csv-file-stream]`, `totalResult=''`가 전달된다.
- History 화면도 위 세 SourceType만 조회한다.
- Agent `/api/classification/inspect-upload`은 사전 로드 Runtime을 재사용하며 `/api/classification/inspect/auto`과 SQLite 단발 저장 경로는 없다.
- Tool Score 이미지 Viewer에서 원본/크롭과 각 Green Tool Heatmap을 명시적 버튼으로 전환한다.
