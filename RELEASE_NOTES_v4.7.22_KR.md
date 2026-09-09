# VisionQC 4.7.22 / Agent 1.3.12

## 이번에 바꾼 범위

Green Simulation 기본 실행 경로를 독립 프로세스로 분리했다. 제공된 DL_Simulation v1.13의 원본 소스와 비교하여 Runtime 생성·로드·검사·정리 수명주기를 맞췄다. VisionQC 전체를 구형 프로그램으로 되돌린 것이 아니다.

1. Runtime File Load는 선택한 Workspace 구성을 확인한다.
2. Simulation Start를 누르면 Agent의 사전 로드 객체를 정리하고 새 VisionQC.GreenRunner.exe를 시작한다.
3. 검사 프로세스의 한 백그라운드 스레드가 원본의 2인자 Control 생성자, Workspace 로드, 이미지별 Sample.Process, Runtime 정리를 실행한다.
4. 결과·진행률은 로컬 사용자 전용 파이프로 Agent에 전달한다. 웹 실시간 분석, SQLite 이력, CSV·히트맵 저장은 유지한다.
5. 끝나면 검사 프로세스도 종료한다. 다음 검사에는 Runtime File Load를 다시 눌러야 한다.

독립 모드에서는 기존 TensorRT 해제·메모리 선할당 변경·새 Runtime 진단 옵션을 적용하지 않는다. Workspace의 저장된 설정을 사용하며 원본 Workspace 파일을 저장하거나 변환하지 않는다. 상세 진단을 켜도 원본 SDK 생성자를 유지하며, 새 검사 프로세스에서는 SDK debug 생성자 옵션을 강제하지 않는다.

다중 버전 설치를 지원해야 하므로 **SDK DLL 검색은 선택한 설치 버전으로 고정**한다. 이것은 단일 버전 원본 실행 파일과의 의도적인 배포 차이이다. GPU 번호·드라이버·TCC/WDDM·다른 학습 프로세스는 변경하지 않는다.

## 유지한 기능

- Integrated/Blue 실행 경로와 사전 로드 재사용, 단일 이미지 검사 기능.
- Tool 선택·Threshold·판정 우선순위, Cell ID 필터, 여러 입력 폴더.
- 날짜시간 통합 파일명 규칙과 이전 사용자 규칙.
- 원본 FullPath, 처리 ProcessedPath, Tool OverlayPath의 분리.
- Tool/Position CSV, Cell·Position 집계, SQLite 이력과 실시간 분석·진행률.
- 분류 초점 유지, 스코어 Viewer, 다크/화이트 테마, 기존 다운로드 확인창.
- VPDL 미설치 Core 실행과 오프라인 로컬 웹.

## 확인한 결과와 한계

- 자동 코드 검사 120개, 브라우저 29개, C# 진단/파일명/정책/프로세스 경계 62개 통과.
- 이 PC의 VPDL 4.0(API 8.0), 4.2(API 8.2)에서 각각 6개 이미지·24개 Tool 결과·히트맵 8개와 SQLite 저장 확인.
- 제공 ZIP의 GreenOverlayProcessor.cs와 Models.cs를 수정하지 않고 별도 빌드하여 실행. 원본 엔진 → 새 독립 실행 → Agent 연동의 모든 검사/Tool 판정 일치. 48개 Score 비교에서 최대 절대 차이 0.000088, 시험 허용치 0.0001 이내. 비트 단위 동일 결과나 모든 모델의 동일성을 보장하지 않는다.
- 손상 이미지로 Image.Load 실패를 재현하여 원래 SDK 예외와 HResult가 로컬 로그 및 Agent 오류에 보존되는 것을 확인했다. 실패를 OK/NG 결과로 위조하지 않는다.
- 정상·SDK 실패·비정상 종료·중복 메시지·협조 중지·응답 없는 자식·진행률 수신 오류의 프로세스 경계 시험 통과. 중지 후 15초가 지나도 응답하지 않을 때 **이번 검사로 생성한 자식 프로세스만** 종료한다.
- 실제 보안 서버의 Windows Server 2022 + H100 NVL 2장 + 해당 Workspace/이미지는 반출 불가하므로 여기서 재현하지 못했다. **6cc4a157 현장 해결 완료가 아니라, 원본 방식에 맞춘 검증된 배포 후보**이다.

## 보안 서버에서 확인하는 방법

1. 새 오프라인 ZIP을 승인된 반입 절차로 가져간 뒤 압축을 푼다.
2. 기존 VisionQC 검사만 끝내고 포함된 VisionQC_Agent_Installer_v1.3.12.exe를 실행한다. 다른 VPDL 학습 프로그램을 강제로 종료할 필요는 없다.
3. 바탕화면의 **VisionQC 오프라인 실행**을 열거나 브라우저 주소창에 **http://127.0.0.1:17891/** 을 입력한다. 웹 4.7.22와 Agent 1.3.12를 확인한다.
4. Green Simulation → Green Runtime / HeatMap에서 **원본 방식 · 독립 Green 검사 (권장)** 체크를 확인한다. 기존 Workspace·입력 폴더·GPU 번호를 유지한다.
5. Runtime File Load → Simulation Start 순서로 실행한다. 진행 로그의 **[ORIGINAL] 독립 Green 프로세스 시작 | PID=...** 를 확인한다.
6. 완료 시 이미지 수와 CSV·SQLite·히트맵 결과를 확인한다. 실패하면 아래 로그를 **서버 안에서** 연다. 이미지/Workspace/DB 반출은 필요하지 않다.

## 실패 시 읽을 로컬 로그

탐색기 주소창에 다음을 붙여 넣는다:

`%LOCALAPPDATA%\VisionQC\LocalAgent\logs`

- `last-sdk-stage.txt`: 마지막 SDK 단계와 BEGIN/OK/FAIL. Sample.Process인지, 이미지 로드인지, Runtime 생성인지 구분한다.
- `last-sdk-failure.txt`: 원래 SDK 예외/HResult와 해당 PID·스레드.
- `last-green-runner-failure.txt`: 독립 검사 전체 예외.
- `agent-green-runner-<PID>.log`: ORIGINAL_PROCESS / ORIGINAL_RUNTIME, 실제 DLL 버전·경로, 실행 EXE 및 DLL 해시, 단계별 처리.
- `agent-worker-<PID>.log`: GREEN_CHILD_START / GREEN_CHILD_EXIT, 부모·검사 PID 및 종료 코드.
- `cognex-sdk-log-locations.txt`: 실제 Cognex 로그 위치. 독립 검사 로그 파일명은 visionqc.greenrunner로 시작할 수 있다.
- `green-runs\<실행 ID>\request.json`, `result.json`: 실행 구성·결과. 요청에는 내부 파일 경로가 포함된다. 실패 시 result.json이 없을 수 있다.

공유 last-*.txt는 최근 실행으로 갱신된다. 오류 발생 시간과 PID가 맞는지 먼저 확인한다. SDK의 .debug.log는 사람이 읽을 수 없는 형식일 수 있다. 위 로그와 요청 파일은 서버 내부에만 저장되며 자동 업로드하지 않는다. 실행별 요청 기록은 자동 삭제하지 않으므로 조직의 보관 정책에 따라 관리한다.

## 기존 방식과 비교할 때

새 방식을 우선 시험한다. 비교가 꼭 필요할 때만 **원본 방식 · 독립 Green 검사** 체크를 해제하면 기존 TensorRT/새 Runtime/메모리 진단 옵션이 다시 나타난다. 이전 옵션 값은 보존되어 있다. 모드와 옵션을 바꾼 뒤 Runtime File Load를 다시 실행한다. 기존 방식의 실패도 이미 확인된 사항이므로 동일 옵션을 무작정 반복하지 않는다.

## 근거의 해석

현장 로그의 실패 호출은 Sample.Process이며, SDK 결과 반환 전 예외이다. Cognex 공식 문서는 6cc4a157을 메모리 문제 맥락에서 언급하지만 단일 메모리 할당 함수나 VRAM 부족만을 확정하는 코드로 정의하지 않는다. 현장 두 GPU는 이미 TCC이고 실패 시점 스냅샷의 여유 메모리도 많았다. 스레드 번호 차이 또는 TensorRT 모델 경고만으로 원인을 확정하지 않았다.

- [Cognex GPU Driver Mode 안내](https://support.cognex.com/en/help-articles/gpu-driver-mode-selection-vidi-vpdl-performance)
- [VPDL 4.0 GPU 메모리 최적화 안내](https://docs.cognex.com/deep-learning_400/web/EN/deep-learning/Content/Topics/optimization/gpu-optimized-gpu-memory.htm)

설치 패키지에는 검사 이미지, Workspace, 개인 DB, 현장 로그, Cognex 라이선스/DLL을 넣지 않는다. 실제 설치 버전은 EXE 파일 버전과 RELEASE_MANIFEST.json을 기준으로 확인한다. 소스 폴더 LocalAgent_v0.2.12라는 이름은 과거부터 유지한 폴더명이다.
