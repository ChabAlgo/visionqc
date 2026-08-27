# VPDL 다중 버전 Worker 구조

## 목적

VisionQC는 VPDL의 관리 DLL(`ViDi.NET.Local.dll`)과 네이티브 엔진 DLL(`bin\vidi_*.dll`)을 서로 다른 버전으로 섞어 로드하지 않는다. VPDL 버전마다 독립 Worker 프로세스를 사용하고, Launcher가 설치된 정상 Runtime에 맞는 Worker만 실행한다.

## 선택 규칙

1. 설치 폴더를 탐색한다.
2. `ViDi.NET.Local.dll`의 API 버전과 같은 이름의 `bin\vidi_*.dll`이 모두 있을 때만 정상 설치본으로 등록한다.
3. 선택값이 없으면 설치된 Worker가 있는 가장 높은 제품 버전을 선택한다.
4. 사용자가 API 버전을 전환하면 현재 Worker를 종료하고 선택된 Worker를 새 프로세스로 시작한다.
5. Worker가 네이티브 예외로 종료되면 Launcher는 최대 3회 재시작한다. 반복 종료 시에는 Launcher가 남아 오류를 표시한다.

## 제한사항

- 하나의 Worker 프로세스에는 하나의 VPDL API 버전만 로드할 수 있다.
- 새 VPDL API가 기존 API와 호환되지 않는 경우에는 그 API로 Worker를 빌드한 뒤 검증해야 한다. 무조건 DLL을 바꿔 끼우는 방식은 지원하지 않는다.
- Workspace 파일 자체의 VPDL 원본 버전을 신뢰성 있게 읽는 공개 메타데이터가 없는 경우, 자동 선택은 설치된 최신 호환 Worker를 사용한다. 서로 다른 버전 Workspace를 사용할 때는 화면에서 API 버전을 선택해 전환한다.

## 빌드

`BUILD_VPDL_WORKERS.ps1`은 PC에 정상 설치된 모든 VPDL 버전을 탐색해 각각 `Launcher\bin\x64\Release\Workers\{API 버전}`에 Worker로 생성한다. VPDL Runtime과 라이선스 DLL은 Cognex 설치본을 사용하며, VisionQC 설치 파일에 포함하지 않는다.

## 설치 패키지

빌드 스크립트는 생성된 Worker 폴더 전체를 `vpdl-workers.zip`으로 묶고, 단일 설치 프로그램은 Launcher·Worker 목록·Worker 묶음을 포함한다. 설치 시 Worker 묶음은 `Workers\{API}`로 풀린다. 빌드 PC에 여러 정상 VPDL API가 있으면 모두 포함된다.
