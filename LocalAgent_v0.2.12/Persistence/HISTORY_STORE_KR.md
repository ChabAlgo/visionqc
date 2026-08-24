# SQLite 이력 저장소 기준

`SqliteRunStore.cs`가 검사 이력 DB의 유일한 접근 지점이다.

- 기본 DB: `%LOCALAPPDATA%\VisionQC\LocalAgent\data\visionqc-history.sqlite`
- 테스트 전용: `VISIONQC_HISTORY_DB_PATH` 환경 변수로 별도 DB 경로를 지정할 수 있다.
- 테이블: `runs`, `images`, `tool_results`, `schema_info`.
- 이미지 바이트는 저장하지 않고 FullPath/OverlayPath만 저장한다.
- 검색은 `BuildSearchWhere`와 SQL `LIMIT/OFFSET`으로 수행한다. 대량 이력을 C# List 또는 웹으로 전량 반환하지 않는다.
- WAL 모드와 batch commit을 사용한다.

스키마를 바꿀 때는 새 DB뿐 아니라 기존 운영 DB의 migration, index, 롤백/백업 정책을 함께 검토하고 `tests/fixtures`와 API 테스트를 추가한다.
