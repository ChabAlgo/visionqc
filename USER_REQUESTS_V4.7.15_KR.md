# VisionQC v4.7.15 사용자 요청 반영

## 요청 사항

- 메인 `Position별 Tool NG 구성`의 Threshold 입력칸과 숫자가 카드 밖으로 나오지 않도록 정리하고, 기본 증감 화살표가 위아래 모두 보이게 한다.
- 검사 이력의 검사 모드가 Green이면 Green Workspace만, Integrated이면 Integrated Workspace만 표시하며 DB에 실제 존재하는 항목만 노출한다.
- 좌측 메뉴에서 시뮬레이션을 분류 바로 위로 이동한다.
- 메인 상단에도 Agent 다운로드와 오프라인 패키지 버튼을 제공한다.
- Agent/오프라인 패키지는 즉시 받지 않고 다운로드 확인창에서 승인한 경우에만 시작한다.
- Runtime 구조의 Green/Blue/Red Tool 이름을 각각 초록/파랑/빨강으로 구분한다.
- Simulation Start/Stop을 Simulation Status 패널 우상단으로 이동한다.
- Simulation Options 내부 스크롤이 끝에 도달한 뒤 계속 휠을 움직이면 바깥 Simulation 페이지가 이어서 스크롤되게 한다.

## 검증 기준

- 1920px 화면의 실제 Tool 카드 좌우 경계 안에 Threshold 입력칸이 포함된다.
- 입력칸은 브라우저 기본 number spinner를 유지한다.
- 검사 모드 변경 즉시 Workspace 선택지가 해당 모드만 남는다.
- 취소 시 다운로드가 시작되지 않고, 확인 시에만 한 번 시작된다.
- Simulation Status 내부에 Start/Stop이 존재하며 기존 별도 실행 바는 제거된다.
- 옵션 내부 최하단에서 휠을 계속 내리면 바깥 화면의 scrollTop이 증가한다.
