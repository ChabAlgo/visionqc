# Persistence

`SqliteRunStore.cs`는 검사 이력 전용 저장소다.

- DB 위치: `%LOCALAPPDATA%\VisionQC\LocalAgent\data\visionqc-history.sqlite`
- 테이블: `runs`, `images`, `tool_results`, `schema_info`.
- `images`에는 FullPath, Cell ID, Position, 캡처 시각, 결과를 저장한다.
- `tool_results`에는 Tool 판정·Score·Green Heatmap Overlay 경로를 저장한다.
- WAL 모드와 200행 배치 커밋을 사용한다.

제한: 원본 이미지, Heatmap 원시 픽셀, VPDL Workspace, 브라우저의 파일 목록은 저장하지 않는다. 스키마 변경 시 `CREATE TABLE IF NOT EXISTS`만으로는 부족할 수 있으므로 명시적 버전 마이그레이션을 추가하고 기존 DB 백업/호환성 테스트를 수행한다.
