# 요청 기록 - VPDL 다중 버전 대응

## 요청

VPDL 4.2만을 기준으로 고정하지 말고, PC마다 다른 VPDL 버전이 설치될 수 있는 환경을 지원한다.

## 반영 원칙

- 설치 폴더명만 보고 VPDL을 선택하지 않는다.
- 관리 API DLL과 네이티브 엔진 DLL의 API 쌍이 일치할 때만 정상 설치본으로 인정한다.
- API별 Worker를 독립 프로세스로 빌드·배포한다.
- 배포 PC에 없던 VPDL 버전은 Universal Worker가 대상 PC의 설치 DLL을 직접 선택해 실행한다.
- 설치 프로그램에는 정확 버전 Worker와 Universal Worker를 하나의 묶음으로 포함한다.
- 지원되지 않은 새 API는 잘못된 DLL 로드 대신 명확한 안내를 표시하며, 해당 API Worker를 추가 빌드해 배포한다.

## 2026-09-07 보완 사유

기존 v1.3.5 패키지는 “빌드 PC에서 감지된 Worker만 포함”하여 VPDL 4.2/API 8.2 Worker만 배포되었다. 따라서 VPDL 4.0이 설치된 다른 오프라인 PC에서 요구사항을 충족하지 못했다. v1.3.6부터 빌드 PC 종속성을 제거하는 Universal Worker를 필수 산출물로 추가하고, 패키지 검사에서 누락 시 실패하도록 한다.

## 2026-09-07 VPDL 미설치 실행 보완

v1.3.6까지는 Launcher와 VPDL Worker가 Runtime 탐지를 서버 시작의 선행 조건으로 사용해 VPDL이 없는 PC에서는 오프라인 화면조차 열리지 않았다. v1.3.7부터 VPDL을 전혀 참조하지 않는 Core Worker를 패키지에 항상 포함한다. VPDL이 없으면 Core Worker가 포트 17891의 웹 서버와 비-VPDL API를 실행하고, Runtime Load와 Simulation API만 명시적으로 거절한다.
