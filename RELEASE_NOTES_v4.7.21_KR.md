# VisionQC v4.7.21 / Local Agent v1.3.11

## 상태: H100 오류 해결은 아직 확정되지 않음

이번 배포는 Green GPU 메모리 정책을 비교할 수 있는 선택 옵션과 상세 진단을 추가한다.
로컬 RTX 4050에서 통과한 것은 현장 Server 2022 / H100 두 대 / CA(BOT) 모델의 해결 증거가 아니다.
현장 모델·이미지·DB·로그를 외부로 반출할 필요가 없으며, 이번 설치 파일에도 포함하지 않았다.

## 지금 확인된 오류 위치

기존 현장 로그는 Workspace 읽기와 Tool 확인 이후, 첫 Green Crack의 `Sample.Process`에서 Cognex 예외가 발생했음을 보여준다.
검사가 끝나서 Workspace의 최종 출력이 ERROR로 나온 것으로 확인된 것이 아니다.
정상적인 결과/Score를 읽기 전에 SDK 추론 호출이 실패했다.
모델 손상, GPU 메모리, 드라이버 모드, 실행 수명 관리 중 어느 것이 근본 원인인지는 기존 예외만으로 확정할 수 없다.

## 이번 변경

- Green 단독 검사에 **GPU 메모리 선할당 해제 (Green 호환)** 추가. 기본 꺼짐.
  켜면 Control 생성 후, Workspace를 로드하기 전에 `OptimizedGPUMemory(0)`를 명시한다.
  모델을 저장하거나 드라이버/TCC/WDDM/GPU 번호를 변경하지 않는다.
- **상세 진단 로그 (서버 내부 저장)** 추가. 이번 진단 릴리스의 기본값은 켜짐.
  SDK가 지원하는 debug 초기화와 자체 단계 기록을 함께 사용한다.
- Control 생성 → Workspace 로드/재사용 → Stream → Image 로드 → Sample 생성/이미지 전달 → Process → 결과/Heatmap 읽기를 각각 BEGIN/OK/FAIL로 기록한다.
- PID, 실행 스레드, Runtime 식별자/생성 스레드, 선택 GPU, GPU UUID/메모리/드라이버 모드, 로드된 Cognex/CUDA DLL 경로, 원래 예외와 내부 예외/HResult/Data를 기록한다.
- GPU 정보 조회는 읽기 전용이다. 조회 실패/N/A는 그대로 표시하며, GPU 사용 프로세스 목록만으로 특정 연산이 어느 GPU에서 실행됐다고 단정하지 않는다.
- native 강제 종료 시에도 마지막 BEGIN 기록이 남는다. 자동으로 같은 이미지를 재시도하지 않는다.
- 진단/메모리 설정을 바꾸면 Runtime File Load를 다시 요구해 이전 설정의 Runtime 재사용을 방지한다.
- 원래 이미지/크롭/히트맵 저장, Threshold, Tool 판정, CSV/SQLite, 파일명 규칙은 유지한다.
- Integrated/Blue에는 새 옵션을 적용하지 않는다. Integrated Runtime을 Green으로 재사용하려면 GPU/Workspace뿐 아니라 진단/메모리 설정도 같아야 한다. Integrated에서 지원하지 않는 옵션을 Green에서 켜면 다시 로드해야 한다.

## 보안 서버에서 확인하는 순서

1. 기존 시뮬레이션을 종료하고 새 ZIP을 압축 해제한 뒤 포함된 Agent 1.3.11 설치 EXE를 실행한다.
2. 브라우저 주소 `http://127.0.0.1:17891/`에서 Web 4.7.21 / Agent 1.3.11을 확인한다.
3. Green Simulation에서 기존 VPDL 4.0, Workspace, 이미지 폴더, GPU 설정은 그대로 둔다.
4. **상세 진단 로그**는 켠다. TensorRT 해제/새 Runtime 옵션은 이전에 실패했던 조합을 계속 반복하지 말고, 기존 설정을 기록해 고정한다.
5. 이번에 추가된 **GPU 메모리 선할당 해제**만 켠 뒤 **Runtime File Load → Simulation Start** 순서로 검사한다.
6. 실패하면 서버 내부에서 아래 로그를 확인한다. 반복해서 전량 검사할 필요는 없다.
7. 진단이 끝나면 상세 로그를 끄고 Agent를 재시작한 뒤 다시 Runtime File Load 한다. 상세 로그는 실행 시간/디스크 사용량을 늘릴 수 있다.

GPU 메모리 옵션을 끈 상태가 비교 기준이다. 켠 상태에서만 성공한다면 메모리 정책 영향의 증거가 되지만, 이것만으로 SDK 내부 원인을 전부 증명한 것은 아니다.

## 로그 위치와 읽는 방법

탐색기 주소창에 `%LOCALAPPDATA%\VisionQC\LocalAgent\logs`를 입력한다.

| 파일 | 확인 내용 |
| --- | --- |
| last-sdk-failure.txt | 마지막으로 예외를 반환한 SDK 단계, 원래 예외/HResult |
| last-sdk-stage.txt | 마지막 BEGIN/OK/FAIL. 강제 종료 시 BEGIN에서 끝날 수 있음 |
| last-green-failure.txt | Green 추론 오류의 Position/Tool/이미지 크기/Runtime 정보 |
| last-gpu-failure.txt | 추론 실패 직후 GPU/프로세스 메모리와 드라이버 모드 |
| cognex-sdk-log-locations.txt | 해당 PC의 실제 Cognex 상세 로그 폴더/파일 경로 |
| agent-worker-숫자.log | 누적 단계 기록. 자체 로그는 8 MB 기준으로 이전 파일 1개 보존 |
| agent-launcher-숫자.log | Worker 종료 코드와 SDK 콘솔 출력 |

- `Stage=Image.Load` 실패는 이미지 읽기 단계다.
- `Workspace.Load` 실패는 Workspace 로드 단계다.
- `Sample.Process` 실패는 SDK 추론 호출 단계다.
- Process의 OK 이후 `Result.ReadMarking` / `Result.ReadHeatmap` 실패라면 결과/히트맵 읽기 단계다.
- `SDK_OK`와 실제 `TOOL_RESULT`를 구분해 결과가 생성됐는지 확인한다.
- 이전 실패 파일은 보존될 수 있다. 반드시 이번 검사 시각과 PID가 일치하는 기록을 확인한다.

Cognex 4.0의 실제 SDK 로그는 이 PC에서
`%APPDATA%\Cognex Corporation\Cognex VisionPro Deep Learning 4.0\logs`
에 생성되는 것을 확인했다. 작업 폴더의 `logs\cognex`라고 가정하면 안 된다.
`.debug.log`는 사람이 읽을 수 없는 인코딩/바이너리일 수 있다. 파일을 수정하지 않는다.
SDK 자체 로그의 크기는 Cognex가 관리하며 VisionQC의 8 MB 제한 대상이 아니다.
상세 로그로도 SDK 내부 원인이 해석되지 않으면, 보안 절차에 맞춘 Cognex 지원 확인이 필요할 수 있다. 원인 특정이 반드시 가능하다고 보장하지 않는다.

## 공식 자료와 해석

- [Cognex 지원 문서: GPU 드라이버 모드와 6cc4a157](https://support.cognex.com/en/help-articles/gpu-driver-mode-selection-vidi-vpdl-performance):
  제목과 본문에 해당 오류 코드를 명시하고 TCC가 메모리 문제에 도움이 될 수 있다고 설명한다.
  이를 먼저 확인하지 못하고 공식 연관 자료가 없다고 판단했던 이전 설명은 정정한다.
  해당 서버에서 기존 프로그램이 동작하므로 드라이버만의 문제로 단정할 수 없다.
- [VPDL 4.0 GPU 메모리 최적화](https://docs.cognex.com/deep-learning_400/web/EN/deep-learning/Content/Topics/optimization/gpu-optimized-gpu-memory.htm):
  선할당과 WDDM/TCC의 관계 및 Standard 학습 시 해제 지침을 설명한다.
  **학습 지침만으로 현장 추론 오류의 원인을 확정할 수 없다.**
  설치된 4.0 Green Standard 공식 예제에서도 `OptimizedGPUMemory(0)` 사용을 확인했다.
- 원본 DL_Simulation Green 엔진과 Agent 1.3.10 모두 이 값을 명시하지 않았다.
  따라서 "원본은 0, Agent만 선할당을 켰다"는 차이는 확인되지 않았다.
  이번 옵션은 그 가능성을 분리해 시험하기 위한 것이다.
- [NVIDIA nvidia-smi 문서](https://docs.nvidia.com/deploy/nvidia-smi/index.html).
  이번 배포는 조회만 수행한다. 드라이버 모드 변경/재부팅 명령은 실행하지 않는다.

## 로드와 검사 시작

Runtime File Load는 선택 GPU의 Control을 만들고 Workspace/Stream/Tool을 메모리에 올린다.
실제 이미지별 추론은 Simulation Start 이후 Sample.Process에서 실행된다.
일반 모드는 사전 로드 Control/Workspace를 재사용한다.
기존 "새 Runtime으로 검사"는 같은 Worker 안에서 새 Control을 만드는 옵션이며, 별도 프로세스 실행이 아니다.
이번에는 이 실행 구조를 임의로 바꾸지 않고 생성/사용 스레드와 Runtime ID를 기록한다.
GPU 두 대라고 해서 이미지 하나가 자동으로 두 GPU에 나뉘어 처리되는 것도 아니다.

소스 폴더명 `LocalAgent_v0.2.12`나 예외의 개발 PC 경로는 빌드 시 포함된 소스 위치다.
실행 Agent 버전은 화면/EXE 파일 버전으로 확인한다. 이번 배포는 1.3.11이다.

## 검증

- C# 파일명 규칙 16건 / Green 정책 11건 / 진단 기록 12건 통과.
- 코드 회귀 115건, 최종 브라우저 회귀 27건 통과.
- 기존 분류 초점 테스트는 최초 전체 실행에서 1회 실패했다. 해당 기능 코드는 이번에 수정하지 않았으며, 단독 5회 반복과 최종 전체 실행에서는 통과했다. 간헐성은 완전히 배제하지 않는다.
- RTX 4050 / VPDL 4.0: 기본 및 메모리 선할당 해제 설정에서 각각 이미지 6건, Tool 24건, 히트맵 8개 저장. 24개 판정 모두 일치, Score 최대 차이 0.000072. 수치가 비트 단위로 같다고 주장하지 않는다.
- VPDL 4.2: 두 Workspace 구조 미리보기 후 새 Runtime으로 6건/24건/8개 완료.
- 두 버전 모두 비어 있지 않은 Cognex native .debug.log 생성 확인.
- 임시 손상 이미지로 실제 SDK Image.Load 예외를 발생시켜 실패 단계와 원래 예외/HResult 기록 검증. 실제 6cc4a157을 로컬에서 재현한 것은 아니다.
- VPDL 없는 Core: 웹 실행, 시뮬레이션 거절, 14자리 촬영 시각 미리보기와 SQLite 저장 확인.
- 사용한 두 원본 Workspace의 SHA-256은 검사 전후 동일.
- MKL_THREADING_SEQUENTIAL 경고는 로컬의 정상 완료 검사에서도 나타났다. 그 경고 한 줄만으로 현장 실패 원인이라고 판단할 수 없다.
- Server 2022 / H100 두 대 / 현장 CA(BOT) 모델에서의 재검증은 남아 있다.
