# VisionQC v4.7.0 구조 기준

## 실행 경로

```text
GitHub Pages / 오프라인 Web
  └─ visionqc-extension.js
       ├─ CSV 분석 화면 / Simulation 설정
       ├─ 메인 대시보드/검사 이력 화면 (날짜별 NG율, 페이지 단위 조회)
       └─ AI 검사 화면 (파일명 → Position → Green Workspace)
  └─ HTTP 127.0.0.1:17891
       └─ Local Agent v1.2.0
            ├─ AgentServer: HTTP/SSE 조율, VPDL Runtime 소유
            ├─ Services/HistoryService: 이력 API와 비동기 파일 적재 작업
            ├─ Services/CsvHistoryFileImporter: 줄 단위 CSV 파싱/DB 적재
            ├─ Persistence/SqliteRunStore: SQLite 스키마·쓰기·검색
            ├─ Services/ImagePreviewService: 경로의 이미지만 축소 미리보기
            └─ Engine/GreenOverlayProcessor: Green Tool 검사 및 Overlay 저장
```

## 책임 분리 규칙

- `AgentServer`는 요청 라우팅과 VPDL Runtime 동기화만 맡는다. 새 DB 쿼리나 CSV 파싱 코드를 추가하지 않는다.
- `HistoryService`는 HTTP body를 DTO로 해석해 이력 저장/조회 작업을 연결한다. 동시 대용량 파일 가져오기는 하나만 실행한다.
- `CsvHistoryFileImporter`는 CSV를 한 줄씩 읽고 즉시 SQLite writer에 전달한다. 전체 행 또는 이미지 목록을 메모리에 누적하지 않는다.
- `SqliteRunStore`는 SQLite의 유일한 접근 계층이다. 검색은 `LIMIT/OFFSET`과 SQL 조건으로 처리한다.
- 웹은 이력 화면에 현재 페이지의 최대 100행만 유지한다. 원본/Overlay 이미지는 클릭했을 때 Agent `/api/image/preview`를 통해 읽는다.

## API 추가분

| API | 용도 |
| --- | --- |
| `POST /api/history/search` | 날짜·Cell·Position·Tool·결과 필터, 집계, 페이지 목록 |
| `POST /api/history/import-file/start` | Agent가 대용량 CSV를 비동기로 SQLite에 직접 적재 시작 |
| `POST /api/history/import-file/status` | 적재 진행 행 수와 완료/오류 조회 |
| `POST /api/classification/inspect/auto` | 파일명 Position으로 Green Workspace를 자동 선택·Runtime Load·단일 검사 |

## 유지보수 판단 기준

새 데이터 기능은 먼저 SQLite DTO/Store/Service/API 순서로 추가하고, 웹은 그 API의 페이지 단위 결과만 표시한다. 새 VPDL 기능은 Runtime을 동시에 두 개 점유하지 않도록 `AgentServer`의 `_vpdlSync` 및 사전 로드 Runtime 규칙을 따른다.

기존 `visionqc-extension.js`는 레거시 화면까지 포함한 단일 번들이다. 이번 버전에서는 큰 이력 처리와 CSV 처리 책임을 Agent의 전용 객체로 분리했다. 향후 기존 화면 자체를 나눌 때에는 상태/렌더링/API 어댑터 경계를 먼저 정의한 뒤, 화면 단위로 파일을 분리한다. 단순히 파일을 쪼개면서 전역 상태를 늘리면 유지보수성이 오히려 떨어진다.
