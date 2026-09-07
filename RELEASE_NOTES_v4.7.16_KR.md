# VisionQC v4.7.16 / Local Agent v1.3.6

## 핵심 변경

- 오프라인 설치 파일에 `Workers\Universal\VisionQC.VpdlWorker.exe`를 필수 포함한다.
- 대상 PC에서 발견된 VPDL 제품/API/Studio 경로를 Launcher가 Worker에 명시적으로 전달한다.
- `Workers\{API}`의 정확 버전 Worker가 있으면 우선 사용하고, 없으면 Universal Worker를 사용한다.
- VPDL 관리 DLL 위치가 버전별로 달라도 기본 설치 루트 아래에서 제한 깊이로 재귀 탐색한다.
- 빌드 전에 기존 Workers 산출물을 제거하고, ZIP 내부에 Universal Worker가 없으면 빌드를 실패시킨다.
- Launcher에도 Agent 버전 메타데이터를 기록한다.

## 수정 배경

v1.3.5는 빌드 PC에 설치된 VPDL 4.2/API 8.2 Worker만 패키지에 포함했다. 그 결과 VPDL 4.0이 설치된 다른 오프라인 PC에서 실행할 Worker를 선택하지 못했다.

## 호환 및 검증 경계

- VPDL Runtime과 라이선스는 Cognex 정식 설치본을 사용하며 패키지에 포함하지 않는다.
- 현재 개발 PC의 VPDL 4.2/API 8.2 빌드와 Universal Worker 패키징을 검증한다.
- VPDL 4.0 PC에서는 새 설치 파일로 설치 후 Agent 상태의 제품/API 표시, Runtime File Load, 실제 시뮬레이션을 최종 확인해야 한다.
- Cognex API가 비호환 변경된 버전은 해당 API의 정확 Worker를 추가 빌드하면 Universal Worker보다 우선 적용된다.
