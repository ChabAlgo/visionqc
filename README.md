# VisionQC GitHub Pages v4.4.37

GitHub Pages 정적 배포용 VisionQC Web입니다. 실제 VPDL Runtime/GPU 작업은 사용자 PC의 VisionQC Local Agent v0.2.12에서 실행합니다.

## v4.4.37 핵심

- Chrome 145+의 Loopback Network 권한 상태를 감지합니다. 권한이 차단됐거나 요청 대기 상태이면 Local Agent 카드에 정확한 조치 방법을 표시합니다.
- 이 권한은 Chrome 보안 기능이라 Web이 대신 허용할 수 없습니다. 처음 안내가 뜨면 Chrome의 `127.0.0.1` 연결 권한을 허용하면 이후 파일·폴더 선택과 Simulation 요청이 정상 동작합니다.

## v4.4.36 핵심

- Chrome의 Local Agent 요청 대상(`127.0.0.1`)과 `targetAddressSpace`를 모두 `loopback`으로 일치시켰습니다. `local` 값으로 인한 Chrome CORS 차단을 제거했습니다.
- Workspace를 선택하면 다른 체크박스나 입력 이벤트를 기다리지 않고, 선택한 경로가 즉시 Position 입력칸에 표시됩니다.
- Keyword Mode에서도 Position별 Image Folder 선택 버튼을 비활성화하지 않아 Ctrl/Shift 다중 폴더 선택을 계속 사용할 수 있습니다.
- Local Agent의 `IFileOpenDialog` 기반 Explorer 선택기를 유지하여 왼쪽 탐색창(홈/드라이브/빠른 액세스)이 있는 Windows 표준 선택 화면을 사용합니다.

## v4.4.34 핵심

- Image Folder에서 Ctrl/Shift로 여러 폴더를 동시에 선택하고, 모든 경로를 Simulation 요청으로 전달합니다.
- Explorer 선택 대화상자의 완료 신호를 직접 기다려 파일·폴더 선택 뒤 Web 로딩이 남는 문제를 보완했습니다.
- 선택 취소는 Explorer 응답 지연과 무관하게 즉시 완료 상태로 전환합니다.
- 이전 Agent·누적 테스트 보고서·중간 빌드 파일을 정리하고 v0.2.12 Release만 유지합니다.

## v4.4.33 핵심
- 정상적으로 열린 Windows 선택창을 5분 뒤 강제로 취소하던 Web timeout 제거
- 사용자가 파일/폴더를 선택하거나 Windows 창에서 취소할 때까지 비동기 상태 조회 유지
- 폴더 선택창의 왼쪽 탐색 트리를 숨기던 Shell 옵션 제거
- 일반 Windows 탐색기 형태의 Quick Access/드라이브 탐색 영역 복원
- 이전 선택 작업 자동 정리는 오류 알림이 아닌 INFO 로그로 기록

## v4.4.32 핵심
- Agent 연결 시 감지한 설치 DLL 버전과 실제 Simulation Runtime preload 상태를 분리
- Runtime File Load 전 VPDL Runtime은 `미로드`, 성공 후에만 실제 버전 표시
- Workspace Runtime Structure는 preload 전 compact header만 표시하고 로드 중/성공 후에만 Position 카드 확장
- Simulation 상단의 별도 `선택 창 취소` 버튼 제거
- Windows Shell이 기억한 H:/UNC/최근 위치를 초기화하고 로컬 폴더에서 Picker를 즉시 시작
- 최근 위치·고정 위치 자동 열거를 비활성화해 끊긴 네트워크 경로로 인한 선택창 지연 방지

## v4.4.31 핵심
- 파일·폴더 선택을 장시간 단일 HTTP 연결에서 requestId 기반 `start -> status` 작업으로 변경
- 같은 탭의 잔류 선택창 자동 취소와 Simulation 상단 `선택 창 취소` 버튼 추가
- H: 가상/네트워크/UNC 초기 경로의 느린 존재 확인을 건너뛰어 선택창 시작 지연 제거
- Agent 내부 예외를 HTTP 500 JSON으로 반환해 원인 없는 `Failed to fetch`를 구체적인 오류로 전환
- Chrome Local Network Access loopback 요청 표기와 권한 진단 안내 추가
- 좌측 메뉴와 Simulation 상단에 Web/Agent 버전을 상시 표시

## v4.4.30 핵심
- Keyword 모드가 꺼진 동안 공통 입력 폴더를 입력·선택할 수 없도록 문맥 비활성 상태를 공통 잠금과 분리
- Keyword 모드 변경 즉시 공통 입력 폴더와 Position별 이미지 폴더의 활성 상태를 함께 갱신
- 동일 브라우저 탭에 남은 Windows Picker를 Agent에서 취소하고 새 선택 창으로 한 번 자동 복구
- Agent 종료·연결 끊김·인스턴스 교체 시 Runtime token과 Workspace 구조 표시를 함께 초기화
- 알림을 하단 유틸리티 영역으로 이동하고 전체 사이드바 아이콘을 단색 SVG 테마로 통일
- Local Agent의 VPDL 4.2 설치 경로 탐지 지원

## v4.4.29 핵심
- Simulation 설정 보정 시 현재 객체를 기본값으로 덮던 잘못된 `Object.assign` 순서를 수정
- Tool 추가/선택 제거 시 현재 checkbox·Tool·Judgement·모든 Green/Blue 파라미터를 먼저 단일 동기화
- Tool을 전부 제거해도 기본 15개 Tool로 몰래 복원하지 않으며, 추가 버튼으로 빈 행부터 다시 구성 가능
- 파일/폴더 Picker를 Web과 Agent 모두 single-flight로 변경해 두 번째 요청이 뒤에서 무한 대기하지 않음
- Runtime preload token/signature와 Agent instance를 비교해 화면의 READ OK와 실제 메모리 세션 불일치 차단
- Simulation Start를 single-flight로 변경해 License 확인 중 연속 클릭 요청 차단
- VPDL Workspace 이름 indexer 대신 preload 때 반환된 실제 Workspace 객체를 재사용
- Progress Update 값이 설정 보정 과정에서 100으로 되돌아가던 원인 제거

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

권장 Local Agent: **v0.2.12**
