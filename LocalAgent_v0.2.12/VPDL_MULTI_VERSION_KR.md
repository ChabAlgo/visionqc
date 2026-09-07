# VPDL 전체 버전 호환 Worker 구조

## 목적

VisionQC는 VPDL 미설치 Core Worker, 정확 버전 Worker, Universal Worker의 3단계 구조를 사용한다. 설치된 VPDL이 있으면 관리 DLL(`ViDi.NET.Local.dll`)과 네이티브 엔진 DLL(`bin\vidi_*.dll`)을 한 쌍으로 선택하며, VPDL이 없어도 웹·CSV·SQLite·Picker·이미지 기능은 실행한다.

## 선택 규칙

1. `COGNEX_VPDL_ROOT` 또는 기본 루트 아래를 재귀 탐색한다.
2. `ViDi.NET.Local.dll`의 API 버전과 같은 이름의 `bin\vidi_*.dll`이 모두 있을 때만 정상 설치본으로 등록한다.
3. 정상 VPDL 설치본이 없으면 VPDL을 참조하지 않는 `Workers\Core` Worker를 사용한다.
4. 정상 설치본이 있으면 `Workers\{API}`의 정확 버전 Worker를 우선 사용한다.
5. 정확 버전 Worker가 없으면 `Workers\Universal` Worker를 사용하고, 대상 PC에서 발견한 Studio 경로와 API 버전을 환경변수로 명시한다.
6. VPDL Worker는 전달받은 API 버전의 설치본만 선택하고 그 설치본의 관리 DLL과 네이티브 DLL만 로드한다.
7. 선택값이 없으면 실행 가능한 가장 높은 제품 버전을 선택한다.
8. 사용자가 API 버전을 전환하면 현재 Worker를 종료하고 선택된 Worker를 새 프로세스로 시작한다.
9. Worker가 네이티브 예외로 종료되면 Launcher는 최대 3회 재시작한다. 반복 종료 시에는 Launcher가 남아 오류를 표시한다.

## 제한사항

- 하나의 Worker 프로세스에는 하나의 VPDL API 버전만 로드한다.
- Universal Worker는 VPDL 4.0처럼 배포 PC에 없던 설치본도 탐지·선택할 수 있다. 다만 Cognex가 API의 타입 또는 메서드를 비호환 변경한 버전은 실제 해당 Runtime/라이선스/GPU에서 검증해야 한다.
- 비호환 API는 해당 버전으로 빌드한 `Workers\{API}` Worker를 추가하면 Universal보다 우선 적용된다.
- Workspace 파일 자체의 VPDL 원본 버전을 신뢰성 있게 읽는 공개 메타데이터가 없는 경우, 자동 선택은 설치된 최신 호환 Worker를 사용한다. 서로 다른 버전 Workspace를 사용할 때는 화면에서 API 버전을 선택해 전환한다.

## 빌드

`BUILD_VPDL_WORKERS.ps1`은 VPDL 유무와 관계없이 `Workers\Core`를 항상 생성한다. 빌드 PC에 정상 설치된 VPDL이 있으면 `Workers\{API 버전}`과 `Workers\Universal`도 추가한다. VPDL이 없는 빌드 PC에서도 분석용 Core 패키지는 만들 수 있다. VPDL Runtime과 라이선스 DLL은 Cognex 설치본을 사용하며 VisionQC 설치 파일에 포함하지 않는다.

## 설치 패키지

빌드 스크립트는 Core Worker를 항상 `vpdl-workers.zip`에 포함한다. `vpdl-workers.json`의 `strategy`는 `core-or-exact-or-universal`이며 Core Worker가 실제 ZIP 안에 없으면 릴리스 검증을 통과할 수 없다.

## 지원 의미

VPDL이 없을 때는 분석, 검사 이력, 이미지 조회·분류, 파일 선택 기능을 사용할 수 있다. Runtime Load, Workspace 구조 검사, AI 검사, Green/Blue/Integrated Simulation만 비활성화된다. VPDL 기능을 사용할 대상 PC에는 정식 Cognex Runtime과 라이선스가 필요하며 VisionQC는 Cognex DLL을 재배포하지 않는다.

## 새 버전 검증 절차

1. 대상 PC에서 `ViDi.NET.Local.dll`과 대응 `bin\vidi_*.dll`이 탐지되는지 확인한다.
2. Agent 상태의 제품 버전/API 버전이 실제 설치본과 같은지 확인한다.
3. Runtime File Load, Green/Blue/Integrated 각 1건 이상을 실제 처리한다.
4. Universal Worker에서 API 비호환 오류가 나면 해당 API 설치 PC에서 정확 Worker를 빌드해 패키지에 추가한다.

시작 단계 예외는 `%LOCALAPPDATA%\VisionQC\LocalAgent\logs\agent-startup.log`에 선택된 VPDL 제품/API와 Worker 방식(Exact/Universal)을 함께 기록한다. `/api/status`와 `/api/vpdl/versions`에서도 현재 Worker 방식을 확인할 수 있다.
