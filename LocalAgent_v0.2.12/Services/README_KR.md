# Services

HTTP와 VPDL Engine 사이의 재사용 가능한 로컬 기능을 둔다.

- `PickerService`: STA 파일/폴더 선택창 수명, 다중 폴더, 최근 경로, 취소를 관리한다.
- `NamingProfileParser`: 파일명에서 Cell ID, 날짜, 시간을 추출한다. Simulation과 SQLite가 같은 규칙을 쓴다.
- `PositionResolver`: 파일명에 포함된 Position 문자열로 단일 검사 Workspace를 고른다. 정확히 하나여야 한다.
- `ImagePreviewService`: 허용된 로컬 이미지 한 장만 읽어 축소 JPEG data URL로 반환한다. 512MB 초과, 지원하지 않는 확장자는 거부한다.

이 폴더는 SQLite 스키마를 직접 수정하지 않고 `Persistence`의 공개 메서드만 사용한다. 이미지 전체 목록/원본 이미지 바이트를 장기 캐시하지 않는다.
