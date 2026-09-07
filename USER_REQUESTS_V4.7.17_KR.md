# VisionQC v4.7.17 사용자 요청 반영

## 요청

- VPDL이 설치되어 있지 않은 오프라인 PC에서도 VisionQC 서버와 화면이 실행되어야 한다.
- VPDL이 필요한 기능만 제한하고 나머지 분석 기능은 사용할 수 있어야 한다.
- 실행 주소는 항상 `http://127.0.0.1:17891/`로 유지한다.

## 반영

- VPDL 관리 DLL을 전혀 참조하지 않는 `VisionQC.CoreWorker.exe`를 추가했다.
- Launcher는 VPDL 설치본이 없으면 Core Worker를 자동 선택한다.
- Core Worker는 오프라인 UI, Picker, 이미지 미리보기, CSV/SQLite 검사 이력 API를 제공한다.
- Runtime Check/Load, Workspace 검사, AI 검사, Simulation API는 `VPDL 미설치` 오류를 반환한다.
- 웹 화면은 Agent를 정상 연결 상태로 유지하면서 VPDL Runtime을 `미설치`로 표시하고 Runtime Load/Simulation Start만 비활성화한다.
