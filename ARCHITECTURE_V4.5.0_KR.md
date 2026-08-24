# VisionQC v4.5.0 구조 기준

이 문서는 다음 기능을 추가할 때 지켜야 할 경계를 정리한 유지보수 기준서입니다.

## 현재 경계

```text
Web UI
  ├─ 화면 상태/렌더링
  └─ Local Agent HTTP 호출

Local Agent (API facade: AgentServer)
  ├─ PickerService          : Windows Explorer 선택, 다중 폴더, 취소, 브라우저 소유권
  ├─ NamingProfileParser    : Cell ID·촬영 날짜·촬영 시간 파일명 파싱
  ├─ PositionResolver       : 파일명 → Position 단일 매칭
  └─ GreenOverlayProcessor  : VPDL Green 실행과 검사 결과 생성
```

`AgentServer`는 HTTP 요청을 서비스에 전달하고, VPDL Runtime의 단일 소유권과 SSE 상태 방송만 담당한다. 새 기능의 규칙·파싱·선택 창 상태를 `AgentServer`에 다시 넣지 않는다.

## 파일명 규칙의 데이터 계약

- Cell ID / 날짜 / 시간은 각각 `auto` 또는 `token` 모드이다.
- `tokenIndex`는 사람이 읽는 1부터 시작한다.
- 날짜는 유효한 `YYYYMMDD`, 시간은 유효한 `HHMMSS`만 인정한다.
- 기본 Cell ID 규칙은 영문을 포함한 18글자 영숫자 토큰을 찾고, 앞 16글자를 사용한다.
- 후보가 둘 이상이면 임의 선택하지 않고 `ambiguous`로 반환한다.
- 미리보기 입력은 Agent에서 최대 200개로 제한한다. 대량 파일을 브라우저나 Agent 메모리에 한 번에 올리지 않는다.

## 단일 Green 검사 계약

`POST /api/classification/inspect`는 한 장의 로컬 이미지와 현재 시뮬레이션 설정을 받아 다음을 수행한다.

1. 파일명에서 활성 Position을 하나만 찾는다.
2. 같은 설정으로 사전 로드된 Green Runtime이 있는지 확인한다.
3. 기존 `GreenOverlayProcessor`의 동일한 판단 규칙으로 한 장만 검사한다.
4. 결과와 Tool별 점수를 반환하고, Runtime은 재사용 가능한 상태로 돌려놓는다.

이 API는 결과 CSV나 Heatmap 파일을 쓰지 않는다. 따라서 대량 시뮬레이션의 저장 경로·성능에 영향을 주지 않는다.

## 다음 구조 분리 순서

1. `RuntimePreloadService`: VPDL Control/Workspace preload·dispose·단일 검사 잠금 이동
2. `SimulationService`: 요청 검증, Green/Blue 설정 변환, 실행 수명주기 이동
3. `RunStore` 인터페이스: SQLite 구현을 추가해 실행/이미지/Tool 결과를 디스크에 저장
4. Web 모듈 분리: Agent client, 파일명 규칙 UI, 분석 집계, 시뮬레이션 화면을 별도 파일로 분리

SQLite 도입 전에도 위 API 계약은 유지한다. 따라서 DB 저장소를 붙여도 Web 요청 형식과 VPDL 엔진 호출부를 다시 바꿀 필요가 없다.

## 성능 원칙

- 폴더 전체 목록화와 정렬은 실행 직전에 필요한 Position/루트에만 적용한다.
- 파일명 미리보기와 화면 표는 항상 상한을 둔다.
- 원본 이미지 바이트와 전체 경로 목록을 브라우저 메모리에 장기 보관하지 않는다.
- VPDL Control은 한 번만 사전 로드하고, 동시 작업은 명시적으로 거절한다.
- 실행 이력과 이미지 인덱스가 필요한 시점에는 브라우저 메모리 대신 SQLite `RunStore`를 사용한다.
