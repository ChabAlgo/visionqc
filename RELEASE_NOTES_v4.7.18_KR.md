# VisionQC v4.7.18 / Local Agent v1.3.8

## 수정

- Worker 복구 또는 VPDL 버전 전환 때 오프라인 브라우저를 다시 열지 않는다. 최초 실행 때만 연다.
- 여러 VPDL이 설치되어도 선택한 버전의 네이티브 DLL 검색 경로를 프로세스 내에서 우선한다. Windows 전역 PATH는 변경하지 않는다.
- GPU/드라이버, 실제 로드한 관리·네이티브 DLL, 검사 요청, 마지막 이미지/Tool/히트맵 처리 단계, SDK 출력, Worker 종료 코드를 로컬에 기록한다.
- 4.0/API 8.0, 4.2/API 8.2 정확 버전 Worker와 Universal, VPDL 독립 Core Worker를 패키지에 포함한다.

## 진단 파일

`%LOCALAPPDATA%\VisionQC\LocalAgent\logs`

오류 직후 이 폴더를 복사하면 된다. 이미지 자체는 수집하지 않으며 외부로 자동 전송하지 않는다. 로그와 `last-simulation-request.json`에는 로컬 이미지·워크스페이스 경로가 포함될 수 있으므로 공유 전 확인한다.

## 확인 범위와 남은 사항

- 현재 PC(RTX 4050 Laptop, 드라이버 592.82)의 VPDL 4.0에서 기존 Green 워크스페이스 2개와 이미지 복사본 3개를 사용한 비교 검사: 기존 Universal/정확 버전 Worker 각각 6건 처리, Tool 결과 24건, 히트맵 8개 저장 성공.
- 두 실행의 OK/NG는 모두 일치했다. Score는 최대 0.00008 차이로 완전 동일하지 않았다.
- 다른 PC의 H100 + VPDL 4.0에서 보고된 `Cognex Internal Error (6cc4a157)`는 현재 PC에서 재현하지 못했다. 이 릴리스를 해당 오류의 해결 완료로 간주하지 않는다. H100 PC에서 재검사 후 진단 로그가 필요하다.

## 수정판 검증 결과

- 정확 버전 Worker: 4.0 및 4.2 각각 기존 Green 워크스페이스 2개 × 이미지 복사본 3개 = 6건 검사, Tool 결과 24건, 히트맵 8개 저장 성공.
- 재시작 테스트: 모의 Worker 3회 실행, 최초 1회에만 오프라인 창 열기 요청. 버전 전환/비정상 종료 코드와 SDK 출력 기록 확인.
- VPDL 없는 조건: 실제 Core Worker의 오프라인 페이지/상태 응답 성공, Simulation 요청은 설치 안내와 함께 거절.
- 정적 회귀 106개 통과. 화면 회귀는 23개 중 22개 통과했으나 분류 확대 좌표 테스트가 실패했고 단독 3회 중 2회에서도 실패했다. 화면 테스트 전체 통과로 보고하지 않는다. 이번 화면 코드 변경은 버전/다운로드 주소만이다.
- 비교용으로 이전 배포 b2f1676의 화면 코드를 사용한 3회 실행은 통과했다. 간헐 실패의 원인을 이번 Agent 변경 또는 단순 테스트 타이밍으로 단정하지 않으며 별도 미해결 검증 항목으로 남긴다.
- 초기 단기 응답 제한(1.5초)을 둔 Native 자동 테스트는 준비 단계에서 시간 초과했다. 화면 테스트와 분리하고 응답 대기를 15초로 조정한 재실행에서 위 실제 검사 결과를 확인했다. 이 준비 단계 실패를 Cognex 내부 오류 재현으로 취급하지 않는다.

## H100 관련 확인

H100이라는 모델명만으로 미지원이라고 판단하지 않는다. Cognex 4.0 문서는 CUDA Compute Capability 6.1 이상을 명시하고 NVIDIA는 H100을 9.0으로 분류한다. 다만 Cognex의 테스트 모델 목록에 H100은 없으며 드라이버/실제 처리 환경까지 검증했다는 뜻은 아니다.

- [Cognex 4.0 PC 요구사항](https://docs.cognex.com/deep-learning_400/web/EN/deep-learning/Content/Topics/getting-started/pc-requirements.htm)
- [Cognex 4.0 테스트 GPU](https://docs.cognex.com/deep-learning_400/web/EN/deep-learning/Content/Topics/getting-started/gpu-recommendations.htm)
- [NVIDIA H100 Compute Capability](https://developer.nvidia.com/blog/nvidia-hopper-architecture-in-depth/)

드라이버를 임의로 변경하거나 Standard Green을 지원하지 않는 CPU 모드로 자동 전환하지 않는다. TensorRT 최적화 사용 여부도 로그 및 동일 PC에서의 Cognex Studio 실행과 대조한 뒤 판단한다.
