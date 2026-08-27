# v4.7.10 원본·Crop·Heatmap Viewer 기준

## 데이터 경로

통합 시뮬레이션은 Blue Crop 파일을 Green Runtime 입력으로 사용한다. 그러나 사용자가 이미지 Viewer에서 먼저 확인해야 하는 파일은 원본 Grab 이미지다.

- FullPath: 사용자가 선택한 원본 이미지 경로
- ProcessedPath: Green Runtime에 입력된 Blue Crop 결과 경로
- OverlayPath: 특정 Green Tool의 Heatmap Overlay 파일 경로

LiveAnalysisRecord, 결과 CSV, Web 분석 행, SQLite images 테이블은 이 세 경로의 역할을 분리한다.

## Viewer

1. 기본 화면은 원본 탭을 연다.
2. ProcessedPath가 있고 원본과 다른 경우에만 Crop 탭을 연다.
3. Tool별 Heatmap Overlay가 저장돼 있으면 별도 Heatmap 탭을 연다.
4. 저장된 Overlay가 없으면 현재 사전 로드된 Green Runtime으로 1장을 다시 검사해 생성할 수 있다. 이때 Crop이 있으면 Crop을 우선 사용한다.
5. Simulation 실행 중에는 VPDL Runtime이 예약돼 있으므로 Heatmap 생성은 허용하지 않는다.

## SQLite migration

- 새 DB의 images 테이블에는 processed_path TEXT가 포함된다.
- 기존 DB는 Agent 시작 시 EnsureColumn으로 같은 열을 추가하고 index를 생성한다.
- 이미지 바이트는 DB에 넣지 않으며 경로만 보관한다.
- 과거 실행은 원본 경로가 없을 수 있으므로, Viewer에서 Crop만 보이거나 파일이 이미 삭제돼 열리지 않을 수 있다.

## 유지보수 확인 위치

- 통합 연결: Engine/BlueCropCore.cs
- Green 결과/CSV: Engine/GreenOverlayProcessor.cs
- SQLite: Persistence/SqliteRunStore.cs
- Runtime 호환 단일 검사: AgentServer.cs
- Web Viewer/CSV 파서: visionqc-extension.js