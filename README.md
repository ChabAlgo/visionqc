# VisionQC GitHub Pages v4.4.28

GitHub Pages 정적 배포용 VisionQC Web입니다. 실제 VPDL Runtime/GPU 작업은 사용자 PC의 VisionQC Local Agent v0.2.6에서 실행합니다.

## v4.4.28 핵심
- Runtime File Load를 `Workspace Runtime Structure` 제목 영역으로 이동
- 구조 Inspect가 아닌 실제 VPDL Control/Workspace 사전 로드 API를 사용하고 Simulation Start에서 동일 객체 재사용
- Windows 파일·폴더 선택 창에 Agent 소유 TopMost 부모 창을 적용하고 Web 제한시간을 10분으로 조정
- Tool `선택 제거`가 현재 체크박스 상태를 즉시 읽어 실제 배열에서 삭제
- Progress Update 변경 시 Batch 표시와 Agent 요청값을 즉시 동기화
- 중복 INFO 진행 로그를 제거하고 Batch 경계마다 PROGRESS 한 줄만 기록
- 좌측 알림 메뉴, 읽지 않은 개수 badge, 오류·경고 기록 패널, 확대된 실패 toast 추가
- Simulation 체크박스·텍스트박스·선택 항목에 hover 툴팁 추가
- 상단 `구조 안내` 버튼 제거

## v4.4.27 핵심
- 첨부된 TOPTEC 원본 로고를 Web 자산으로 교체
- Workspace 경로 선택 시 자동 읽기를 제거하고 Output 아래 `Runtime File Load`로 활성 Position의 Runtime을 선로딩
- 비동기 응답이 교체된 Position 객체에 유실되던 문제를 수정해 Progress Log와 Runtime Structure 화면 상태를 일치
- Workspace Runtime Structure를 Position별 카드로 합치고 같은 카드 안에 Green/Blue 구조를 함께 표시
- Simulation Start는 현재 경로와 일치하는 선로딩 성공 결과가 있어야 실행되며, 시작 직전에 Runtime/License를 재확인
- Agent 상태를 2초 간격으로 자동 감시하고 `연결 확인` 버튼 제거
- `Agent 제거`를 `Agent 종료`로 변경하고 프로토콜 등록은 유지
- Agent의 5분 유휴 자동 종료를 제거해 종료 버튼을 누를 때까지 상시 실행
- Agent 시작 직후 Runtime/License를 자동 확인하고 상태 API에 결과를 표시
- Fallback 이미지 선택/Preview가 비동기 후 현재 Position 행을 다시 찾아 값을 반영하도록 수정
- Fallback의 잘못된 label/button 중첩을 제거하고 12px label·14px text·32px control로 다른 옵션과 통일

## v4.4.26 핵심
- Web에서 여러 Workspace 구조 읽기를 Agent로 동시에 보내지 않고 한 건씩 순차 처리
- 각 항목의 `파일 선택 중 → 읽기 대기 중 → 구조 읽는 중 → READ OK/ERROR` 상태를 단일 상태로 유지
- Workspace 요청 제한시간을 실제 실행 시작 후 180초로 적용하고, 시간 초과 시 Agent 완료 확인을 1회 자동 재시도
- 다른 메뉴를 다녀와도 진행 상태가 유지되고, 성공 결과는 10분 Web cache + Agent 파일 상태 cache로 재사용
- 이전 경로의 늦은 응답이 새 경로 결과를 덮어쓰지 않도록 Position별 generation/path 검증 추가
- 이전 버전이 저장한 raw abort 실패를 폐기하고 Agent 연결 시 저장 경로 자동 재검사
- Progress Log 시간을 모두 `HH:mm:ss.SSS [LEVEL]` 형식으로 통일
- Fallback 입력을 34px dark input으로 통일하고 Sample Image 버튼 높이/정렬 수정
- FHD Options를 정상 grid column의 sticky panel로 되돌려 상단 버튼/Workspace 카드 위를 덮지 않도록 수정

## v4.4.25 핵심
- `body[data-vq-page]`를 navigation으로 잘못 인식하던 전역 click selector를 실제 메뉴 버튼으로 제한
- Options scrollTop을 상태로 추적하고 Options 외부 pointer/click에서 값이 바뀌면 즉시 복원
- Agent 연결/Runtime 확인은 Simulation 전체 DOM을 다시 만들지 않고 Agent 카드만 갱신
- Workspace 구조 응답은 해당 Position의 summary/select만 갱신해 텍스트 선택과 Options DOM을 보존
- 동일 Workspace 요청을 Web에서 합치고 Agent에서 단일 Control·cache로 처리
- Fallback / Preview의 980px 표를 반응형 카드로 교체해 가로 scrollbar 제거
- Simulation 일반 텍스트와 입력의 native text selection을 명시적으로 허용
- Chromium 1920×1080에서 실제 mouse drag/mouseup, Main click, Fallback/Preview overflow를 반복 검사하는 Playwright workflow 추가

## v4.4.24 핵심
- FHD 1920×1080 / 브라우저 100% 배율을 기준으로 Simulation 2열 레이아웃 재정렬
- 좌측 메뉴 확장 시 Web 본문을 72px → 272px 위치로 함께 이동해 메뉴 글자 잘림/겹침 제거
- Simulation Options 외곽 sticky와 내부 scroll 영역을 분리해 외부 클릭 시 scrollTop=0 초기화 제거
- Agent 상태/Runtime 갱신 시에도 Options/page/focus 위치를 보존하는 렌더 경로로 통일
- Tool Settings의 620px 강제 최소폭을 제거하고 4열 고정 레이아웃으로 변경해 가로 드래그 제거
- 1439px 이하에서는 Simulation을 1열로 전환하는 반응형 fallback 추가
- v4.4.24 회귀검사 7종 추가
- Local Agent v0.2.3의 CS1503 원인인 잘못된 GPU memory API 호출과 CMD BOM 문제 수정

## 기존 핵심
- HSAGP 참고형 좌측 icon rail + 메뉴 버튼 확장 drawer
- 분류 화면 TOPTEC 이미지 로고 적용, React 상단바는 분류에서만 표시
- Simulation Options 누적 CSS/재렌더 충돌 정리 및 별도 consistency stylesheet 적용
- Judgement/Tool 구조 변경 시 Options/페이지 scroll 위치와 focus 보존
- Progress Update exact batch: 1이면 매 이미지, 5면 5/10/15... + 마지막 잔여 반영
- Live Simulation 결과를 Main/Analysis 기존 분석 모델에 직접 반영
- 상세 Progress Log + Auto Scroll + Elapsed/ETA/img/s/Batch 표시
- Workspace Runtime Structure: Stream, Tool Type, Tag/Class/Feature 표시
- Tool Settings의 Position별 체크 제거, ToolName text input + Runtime 존재 유무 색상 표시
- Custom Position은 Settings/Main/Analysis/Simulation/Result/실제 NG 경로에 공통 적용
- Simulation 실행 중 옵션/Workspace/Position 변경 잠금

## 배포
Repository root에 이 ZIP의 내용물을 그대로 놓고 GitHub Pages를 `main / (root)`로 배포합니다.

## 회귀검사
- `npm ci`
- `npm test` — source/layout/state 정적 회귀검사
- `npx playwright install --with-deps chromium`
- `npm run test:browser` — Chromium FHD 실제 interaction 검사

권장 Local Agent: **v0.2.6**
