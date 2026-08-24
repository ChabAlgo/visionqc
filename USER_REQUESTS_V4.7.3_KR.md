# VisionQC v4.7.3 반영 요청

작성일: 2026-08-24

## 확정 요구사항

1. Integrated Simulation에서 Green·Blue Workspace를 Runtime File Load한 뒤, Green Simulation으로 전환해도 같은 GPU 설정과 같은 Green Workspace·Position 집합이면 다시 로드하지 않고 Green 단독 시뮬레이션을 실행한다.
2. GPU 설정, Green Workspace 경로, 활성 Position 중 하나라도 달라지면 기존처럼 Runtime File Load를 다시 요구한다.
3. Web GUI에 다크 모드와 화이트 모드를 제공하고, 마지막 선택을 브라우저에 저장해 다음 실행에도 유지한다.
4. 테마 버튼 아이콘은 한쪽은 어둡고 한쪽은 흰 반반 모양으로 표시한다.

## 검증 기준

- Agent와 Web 양쪽이 동일한 Runtime 호환 기준을 사용한다.
- 테마 전환은 페이지 이동·시뮬레이션 설정을 초기화하지 않는다.
- 화이트 모드에서 레일, 본문, 카드, 입력창, 로그·이력 화면의 기본 대비를 유지한다.
