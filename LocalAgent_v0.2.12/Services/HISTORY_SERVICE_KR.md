# History Service 유지보수 기준

## 파일별 목적

- `HistoryService.cs`: 이력 HTTP 요청을 해석하고, 브라우저의 소규모 chunk import와 대용량 CSV 파일 import 작업의 수명주기를 관리한다.
- `CsvHistoryFileImporter.cs`: CSV를 `StreamReader`로 한 줄씩 읽어 `SqliteRunStore`에 기록한다. 전체 파일을 List로 만들지 않는다.
- `ImagePreviewService.cs`: FullPath 또는 OverlayPath의 이미지를 요청 시점에만 축소 JPEG data URL로 반환한다.

## 제한과 수정 규칙

- 직접 파일 import는 현재 CSV만 지원한다. XLSX 스트리밍을 추가할 때에도 행 전체를 브라우저 또는 Agent 메모리에 모으지 않는다.
- CSV 파서는 일반 따옴표/쉼표는 처리하지만 하나의 필드가 여러 물리 줄인 경우는 지원하지 않는다. 해당 포맷이 필요하면 CSV parser 교체와 회귀 fixture를 함께 추가한다.
- 동시에 실행할 수 있는 대용량 파일 import는 하나다. 두 번째 요청은 기존 작업을 덮어쓰지 말고 busy 응답을 유지한다.
- 이미지 미리보기는 원본 파일을 DB에 복사하지 않는다. 경로 접근 실패는 UI에 오류로 전달하고 DB 레코드를 삭제하지 않는다.
