# VisionQC 4.7.24 / Agent 1.3.14

## 현장 근거와 수정

H100 NVL 두 장 / Windows Server 2022 서버의 비교진단에서 A(원본 엔진·원본 DLL 검색)와
C(현재 엔진·원본 DLL 검색)는 PASS, B/D(고정 DLL 검색)는 Sample.Process의 6cc4a157로 실패했다.
동일 입력의 이 비교에서 DLL 검색 정책이 성공/실패를 가르는 조건이다.
이 PC의 모듈 기록에서도 원본 검색은 Windows System32의 nvcuda.dll(32.0.15.9282),
고정 검색은 Cognex 4.0 bin의 nvcuda.dll(30.0.14.9676)을 선택했다.
서버의 정확한 원인 DLL까지 이 로컬 기록만으로 확정할 수는 없다.

- 일반 독립 Green 검사의 기본 native DLL 검색을 성공한 C 방식으로 변경한다.
- 부모 Worker의 DLL 검색 디렉터리를 초기화한다. Windows 시스템 폴더보다 Cognex bin을 앞세우는 SetDllDirectory 고정은 적용하지 않는다.
- 선택 SDK의 native/Studio/Service 폴더는 프로세스 PATH에만 앞붙인다. PATH 자체는 Windows 시스템 디렉터리 이후에 검색된다. 4.0/4.2 동시 설치에서 해당 버전 의존 DLL을 찾기 위한 보완이며, 비교진단의 명시적 original/pinned 조건은 바꾸지 않는다.
- 선택한 VPDL 설치본 탐색, API별 Worker 및 managed Cognex DLL 해석은 유지한다.
- 실패했던 pinned 검색은 명시적인 A/B/C/D 비교진단에만 허용한다. 일반 검사 연결에서는 거절한다.
- 진행 로그에 `Green DLL 검색: 원본 방식 (C)` 및 VPDL API를 표시한다.
- Agent 로그에 실제 nvcuda.dll 경로·버전을 포함하고, 비교 결과요약에도 각 실행의 NVIDIA DLL 정보를 표시한다.
- PASS 아래에 first-chance 예외가 최종 실패처럼 나오던 표시를 수정한다. 실제 실패와 실행 중 포착된 예외를 분리 보존한다.

## 유지 범위

Tool/Threshold/판정·ROI·히트맵·CSV·SQLite·필터·진행률·분류·뷰어는 변경하지 않는다.
Workspace와 입력 이미지를 변경하지 않으며, CPU 우회·오류를 OK/NG로 위장하는 처리를 넣지 않는다.
Blue·Integrated·독립 실행을 끈 기존 Green의 로딩 정책은 이번 수정 대상이 아니다.
원본 비교 엔진 두 소스는 원본 ZIP과 바이트 동일성을 유지한다.
Windows/Cognex DLL 파일, 드라이버, GPU 번호, TCC/WDDM, 다른 학습 프로그램 및 시스템 환경변수는 변경하지 않는다.

## 서버에서 확인

1. 승인된 반입 절차로 새 ZIP을 옮기고 포함된 Agent 1.3.14 설치 EXE를 실행한다.
2. 웹 4.7.24 / Agent 1.3.14 표시를 확인한다. 이전 웹 탭은 새로고침한다.
3. Green의 **원본 방식 · 독립 Green 검사**를 켠 상태로 기존 Workspace·이미지·GPU 설정을 유지한다.
4. Runtime File Load → Simulation Start. 진행 로그의 **Green DLL 검색: 원본 방식 (C)**를 확인한다.
5. 처리 건수 증가, 최종 완료 및 기존 CSV/히트맵/검사 이력 결과를 확인한다.

문제가 남으면 상세 진단 로그를 켜고 한 번 실행한 뒤 비교진단 결과 화면을 확인한다.
새 결과요약에는 A/B/C/D 결과와 nvcuda.dll의 실제 경로·버전이 함께 나온다.
이미지·Workspace·DB·전체 모듈 기록을 외부로 반출할 필요가 없다.
바로가기가 없다면 Win+R에서 아래 명령으로 실행할 수 있다.

```
"%LOCALAPPDATA%\VisionQC\LocalAgent\VisionQC.LocalAgent.exe" --green-compare
```

## 검증 범위

- 코드 회귀 123개, 브라우저 회귀 29개 통과. 브라우저 시험은 실제 검사와 분리하여 최종 재실행했다.
- C# 파일명 16 / Runtime 정책 11 / SDK 진단 19 / 프로세스 경계 26 / 비교진단 17 = 89개 통과.
- 최종 실행기로 VPDL 4.0, 4.2 및 Universal의 4.0 검사 각각 이미지 6장·Tool 결과 24건·히트맵 8개와 SQLite 저장 확인.
- 4.0/4.2 각 로그에서 올바른 vidi_80.dll / vidi_82.dll과 Windows System32의 nvcuda.dll 로드 확인.
- 단순 경로 고정 제거에서 발견한 4.2 의존 DLL 로드 오류(127)는 시스템 폴더 뒤의 선택 SDK PATH 탐색으로 보완하고 최종 4.2 검사를 재통과했다.
- 사용자 원본 ZIP 엔진과 최종 독립 실행·Agent 연동의 Score 48개 비교 통과. 최대 절대 차이 0.000082 (허용치 0.0001), 비교한 판정·Cell ID 일치.
- VPDL 미설치 Core의 오프라인 웹·날짜시간·검사 이력 저장 및 시뮬레이션 거절 동작 확인.
- 설치 EXE 버전 1.3.14.0, 포함된 실행기 8종 및 웹 자산 해시가 최종 빌드와 일치함을 확인. Worker 묶음에 Cognex SDK·검사 데이터 없음.

H100/Server 2022 보안 서버 전체 검사는 직접 수행할 수 없으며 현장 재확인이 필요하다.
이 PC의 성공만으로 서버 해결을 확정하지 않는다.

## 배포

웹·설치 EXE·오프라인 ZIP 버전을 일치시킨다. 기존 배포 파일은 복구용으로 보존한다.
ZIP은 설치 EXE, 사용 안내, 릴리스 노트, VERSION.txt만 포함한다.
Cognex SDK/DLL·모델·검사 이미지·개인 DB·로그는 배포에 포함하지 않는다.
