# VisionQC v4.7.19 / Local Agent v1.3.9

## 목적과 확인된 범위

보안 오프라인 H100 서버의 Green 검사 오류 6cc4a157을 자료 반출 없이 구분하기 위한 호환/진단판이다. H100에서 해결됐다는 의미는 아니다.

제공된 로그는 VPDL 4.0/API 8.0 로드 및 Workspace 연결 성공 후 CA(BOT)의 Crack Tool을 실행하는 Sample.Process에서 오류가 발생했음을 보여 준다. DLL 버전 오선택이나 히트맵 저장 단계 오류로 단정할 근거는 없다. Worker의 후속 비정상 종료 원인도 확정되지 않았다.

## 추가한 기능

- Green 단독 검사: TensorRT 해제 (호환 검사)와 새 Runtime으로 검사 (진단)를 독립적으로 제공한다. 기본값은 둘 다 OFF.
- TensorRT 해제는 현재 API의 TensorRTMode 또는 이전 API의 ProcessWithTrt 기능을 확인하고 실제 해제값을 다시 읽어 검증한다. 해제할 수 없으면 적용한 것처럼 진행하지 않고 오류를 알린다.
- 설정은 메모리에만 적용한다. Workspace 저장, 재학습, 자동 재최적화는 하지 않는다. GPU 번호, 드라이버, 시스템 PATH, 판정 Threshold도 자동 변경하지 않는다.
- 새 Runtime 옵션은 Green 검사 스레드에서 Control/Workspace를 새로 연다. SDK 프로세스 자체를 다시 시작하는 옵션은 아니다.
- 호환/진단 옵션을 사용한 Runtime은 다음 검사에 재사용하지 않는다. 검사 후 Runtime File Load를 다시 실행한다. 통합/Blue 검사 및 분류의 단일 이미지 검사에는 이 옵션을 적용하지 않는다.
- 추론 직전 이미지 크기, 선택 GPU 번호, Tool의 TensorRT/Heatmap/Batch 설정을 기록한다. 오류 시 CUDA/cuDNN/TensorRT 등 실제 로드 모듈 및 GPU 사용 메모리/드라이버 모드를 추가 기록한다.
- 실패한 이미지를 정상 판정하거나 자동 건너뛰지 않는다. 옵션 자동 전환·자동 재검사도 하지 않는다.

## 보안 서버 안에서 비교하는 순서

업데이트 반입은 회사 승인 절차를 따른다. 모델·이미지·전체 로그 반출 및 원격 접속은 필요하지 않다. 테스트 출력은 기존 결과와 다른 서버 내부 폴더를 지정한다.

1. 같은 Workspace/입력/Tool/GPU/Threshold를 유지하고 기본 설정으로 결과를 확인한다.
2. TensorRT 해제만 켜고 다시 검사한다. 성공하면 TensorRT 실행 경로의 관련성을 의심할 수 있지만 GPU 자체 고장/호환성으로 단정하지 않는다.
3. 2번에서 실패하면 TensorRT 옵션을 원래대로 끄고 새 Runtime으로 검사만 켠다.
4. 필요하면 둘 다 켜서 비교한다. 각 검사 사이에는 Runtime File Load를 다시 실행한다.
5. 계속 실패하면 서버 내부에서 아래 진단 파일을 확인한다. 외부에 알릴 때는 보안 정책에서 허용하는 성공/실패, 오류 코드, 설정값만 전달한다.

TensorRT 해제로 속도와 Score가 달라질 수 있다. 호환 검사 성공만으로 기존 Threshold가 생산에 적합하다고 보증하지 않는다.

## 진단 파일 (서버 내부 전용)

Windows 탐색기에 %LOCALAPPDATA%\VisionQC\LocalAgent\logs 입력.

- last-green-inference.txt: 마지막 추론 직전 설정/이미지 크기. 직접적인 이미지 파일 경로를 넣지 않는 요약.
- last-green-failure.txt: 관리 코드가 포착한 마지막 추론 오류. 원본 예외 Stack에 경로가 포함될 수 있다.
- last-operation.txt: 실제 처리 중이던 이미지 경로/Tool. 민감 정보 포함 가능.
- agent-worker-*.log, cognex 폴더: 상세 SDK 및 라이브러리 진단. 민감 정보 포함 가능.

파일은 타임스탬프를 기준으로 현재 실패와 대조한다. 네이티브 강제 종료는 catch를 건너뛰므로 last-green-failure가 이전 기록일 수 있다. 이때 last-green-inference와 Worker/SDK 로그를 확인한다. 이미지·모델 복사나 진단 자료 자동 업로드는 하지 않는다.

## 원인 후보의 근거

TensorRT는 GPU 모델별 최적화를 사용한다. 다만 해당 서버 모델이 TensorRT를 사용하는지는 아직 확인되지 않았다. 런타임 재사용 영향 및 GPU 메모리/네이티브 의존성 문제도 후보이며 확정 원인이 아니다.

- Cognex 4.0 API Changelog: https://docs.cognex.com/deep-learning_400/web/EN/deep-learning/Content/Topics/developer/api-changelog.htm
- 현지 설치된 Cognex 4.0 Example.Runtime.GreenStandard.Console 예제의 TensorRT 설정 및 Sample.Process 호출을 대조했다.

## 검증

- RTX 4050 / 드라이버 592.82에서 VPDL 4.0 기본 실행, TensorRT 해제, 새 Runtime 옵션 각각 성공. VPDL 4.2에서 두 옵션 동시 실행 성공. 각 실행은 TOP Workspace 2개 x 이미지 복사본 3개 = 6건, Tool 결과 24건, 저장 히트맵 8개.
- 기본 실행의 Runtime은 재사용 가능하며, 세 호환/진단 실행은 재사용되지 않는 것을 상태 API에서 확인했다.
- 비교한 OK/NG 판정 차이는 0건. Score 최대 차이는 0.000778이었다. 시험 모델의 TensorRT 원래 값은 None이었다. 따라서 실제 Basic/Int8 최적화 모델 해제는 모의 API 단위검증만 했으며 H100 모델에서 확인이 필요하다.
- Workspace 두 파일의 시험 전후 SHA-256 일치: 원본 변경 없음.
- TensorRT 설정 검증 C# 테스트 11개 통과 (현재/이전 API, 명시적 인터페이스, 읽기 전용, 쓰기 무시, 기능 미노출/없는 객체).
- 코드 회귀 109개, 브라우저 회귀 24개 통과. 이번 실행의 화면 테스트 통과가 과거 기록된 간헐 분류 좌표 문제를 영구 해결했다는 의미는 아니다.
- VPDL 없는 실제 Core Worker에서 웹/상태 응답 및 AI 실행 거절 확인.
- 모의 Worker 3회 시작/복구 중 오프라인 창 열기 요청은 최초 1회뿐임을 확인.
- H100 + 해당 CA(BOT) 모델에서의 6cc4a157은 직접 재현/해결 확인하지 못했다. 진단 옵션이 현장에서도 성공하는지 별도 확인이 필요하다.
- 오류 후 SDK 이미지 속성을 다시 조회하지 않도록 값을 미리 보관하는 최종 보완 후, 배포용 4.0 Worker에서 두 옵션 동시 실행을 추가 확인했다: 6건/Tool 24건/히트맵 8개, 완료 후 Runtime 재사용 안 함.
