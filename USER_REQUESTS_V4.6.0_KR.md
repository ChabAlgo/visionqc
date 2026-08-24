# VisionQC 사용자 요청 이력 · v4.6.0

이 문서는 새 대화나 유지보수 작업을 시작할 때 먼저 읽는 요구사항 기준 문서입니다. 완료 항목은 코드·테스트·릴리스에 반영된 범위이며, 보류 항목은 사용자 승인 전까지 구현하지 않습니다.

## 이번 릴리스에서 완료

1. CSV/XLSX의 `FullPath`(또는 `ImagePath`, `FilePath`, `SourceImagePath`) 열을 보존한다.
   - Score 차트의 점을 클릭하면 실제 NG 폴더를 지정하지 않아도 CSV의 로컬 원본 이미지를 Local Agent를 통해 연다.
   - 브라우저가 디스크를 직접 읽지 않는다. Agent가 한 번에 한 장만 최대 2560px JPEG 미리보기로 반환한다.
   - 지원 확장자: BMP, GIF, JPG/JPEG, PNG, TIF/TIFF. 원본은 변경·복사하지 않는다.
2. 검사 이력을 SQLite에 영구 저장한다.
   - Simulation의 Green/Integrated 상세 결과와 Green 단일 검사 결과는 자동 기록한다.
   - CSV 분석 결과는 설정 화면의 **현재 CSV 이력 저장** 버튼을 눌렀을 때만 저장한다.
   - 저장 위치: `%LOCALAPPDATA%\VisionQC\LocalAgent\data\visionqc-history.sqlite`.
   - 저장 대상: 실행 메타데이터, Cell ID, Position, 검사/캡처 시각, FullPath, 결과, Tool별 Score·판정, Heatmap Overlay 경로, 파일명 규칙 스냅샷.
   - 저장하지 않는 대상: 원본 이미지 바이너리, 브라우저의 대량 이미지 목록, 모델/Workspace 파일.
3. 파일명 규칙을 Agent에도 전달한다.
   - Simulation의 Cell ID 추출과 SQLite 캡처 날짜/시간 파싱은 설정 메뉴의 활성 규칙을 사용한다.
4. Heatmap 저장은 **Green 단독 검사**에서만 제공한다.
   - NG일 때 원본 위에 VPDL Heatmap을 합성한 JPEG만 도구별 결과 폴더에 저장한다.
   - 원본 파일은 수정하지 않으며 Integrated 모드에는 저장 옵션을 노출하지 않는다.
5. 설정 아이콘을 톱니바퀴가 아닌 조절 패널 형태 SVG로 교체했다.
6. Agent 기능을 `Services`, `Domain`, `Persistence`로 추가 분리하고, Agent의 각 소스 폴더에 목적·제한·변경 규칙 문서를 추가했다.
7. Web v4.6.0 / Local Agent v1.1.0 설치 파일과 오프라인 패키지를 빌드한다.

## 이전에 확정되어 유지되는 요구사항

- Chrome과 Edge 모두에서 Local Agent 기반 파일/폴더 선택을 안정적으로 사용한다.
- Image Folder 및 Keyword Input Root는 다중 폴더 선택을 지원한다.
- Keyword 모드에서는 Position별 Image Folder를 비활성화한다.
- 최근 선택한 경로를 다음 선택창의 시작 위치로 사용한다.
- Workspace 선택 직후 UI 상태를 갱신하고, Runtime File Load는 명시적인 버튼으로만 실행한다.
- Web GUI는 GitHub Pages와 오프라인 Local Agent 페이지 모두에서 동작한다. 외부 CDN 의존성은 없다.
- 설치 EXE 하나로 Agent 설치, 프로토콜 등록, Agent 실행, 오프라인 UI 열기를 처리한다.

## 보류/후속 기능

- SQLite 이력 조회 화면(날짜별 NG율 그래프, Cell/기간/Tool 검색, Dashboard 연결).
- Green Heatmap을 이미지 Viewer에서 켜고 끄는 상호작용 Overlay.
- AI Suggest가 파일명 Position을 기준으로 적절한 Runtime Workspace를 자동 선택하여 단일 검사를 수행하는 고도화.
- 전체 UI 글자 크기 확대 및 세부 레이아웃 조정(사용자가 “지금 바로는 말고”라고 보류).

## 변경 작업 수칙

1. 새 사용자 요청은 이 문서 또는 다음 버전의 `USER_REQUESTS_*.md`에 먼저 추가한다.
2. 요구사항을 완료로 바꾸기 전에는 코드, 자동 테스트, 가능한 실제 API 검증을 수행한다.
3. SQLite 스키마를 변경할 때는 기존 DB를 파괴하지 않는 마이그레이션과 문서 갱신을 함께 수행한다.
4. 배포 전에는 `RELEASE_NOTES`, `RELEASE_MANIFEST`, 버전 문자열, 설치 EXE/오프라인 ZIP 해시를 같이 갱신한다.
