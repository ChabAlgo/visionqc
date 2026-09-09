# 원본 Green 엔진 비교용 빌드

이 폴더는 사용자가 제공한 DL_Simulation_v1.13_cell_position_summary_source.zip의
GreenOverlayProcessor.cs와 Models.cs를 수정하지 않고 포함한다.
BOM·줄바꿈까지 원본 바이트를 보존하며 자동 시험에서 SHA-256을 검사한다.

| 파일 | SHA-256 |
| --- | --- |
| 원본 ZIP | 2F9BEA65D75C4E6C446DA3FF44A4B42AF38224F9115F5521695206F18A97D1A5 |
| Original/GreenOverlayProcessor.cs | F362C0919F9EB85A33B2BE24F392E5AE2B22A13EEEE89B6C317970D2D2CC18EE |
| Original/Models.cs | 7D2577BB40B64D1AE4B5D897EFC31AB5BAB32C10F71486B3CC9AE25B5DD9767E |

Program.cs는 새로 만든 진단용 진입점이다. 원본 엔진 소스가 동일하다는 의미이지,
현장에서 실행 중인 원본 GUI EXE 전체·설치 폴더·환경·모든 종속 DLL이 동일하다는 의미는 아니다.
설치된 SDK를 참조하여 API별로 빌드하며 Cognex DLL·라이선스는 재배포하지 않는다.

GreenCompare는 동일 이미지 1장, 선택된 Position 하나, 동일 Tool 설정으로 다음 네 조합을 순차 실행한다.

- A: 원본 엔진 + 기본 DLL 검색
- B: 원본 엔진 + 선택 SDK DLL 검색 고정
- C: 현재 엔진 + 기본 DLL 검색
- D: 현재 엔진 + 선택 SDK DLL 검색 고정

원본 엔진은 고정 Position 네 개만 지원하므로 비교용 요청에 한해 해당 Position을
CA_TOP으로 매핑하고 Tool 소속도 동일하게 매핑한다. DisplayName, Workspace, Stream,
Tool 설정, GPU 번호는 유지한다. 이미지 선택을 이미 끝냈으므로 Cell ID/Keyword 필터만 우회한다.
현재 엔진의 OriginalExecution은 실제 검사와 동일하게 Workspace 저장값을 사용한다.
이 실험은 전체 다중 Position·다중 이미지 실행의 완전한 동등성 검사가 아니다.

부모 프로세스가 실제 로드된 모듈을 주기적으로 관찰하여 경로·버전·해시를 기록한다.
관찰 전에 종료된 모듈은 누락될 수 있으며 접근 실패는 별도 기록한다.
외부 이미지 반출, DB 삽입, 드라이버 변경, 다른 학습 프로세스 종료는 하지 않는다.
결과 폴더에는 내부 경로와 이미지 사본이 있으므로 서버 보안 정책에 따라 로컬 보관한다.
