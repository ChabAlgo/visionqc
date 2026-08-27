# Launcher 폴더

## 역할

`VisionQC.LocalAgent.exe`는 VPDL DLL을 직접 로드하지 않는 시작 관리자입니다. 설치된 VPDL을 탐색한 뒤, API 버전이 일치하는 `Workers\{API}\VisionQC.VpdlWorker.exe`만 실행합니다.

## 실행 흐름

1. VPDL 설치본의 `ViDi.NET.Local.dll`과 `bin\vidi_*.dll` 쌍을 확인합니다.
2. 저장한 선택값이 있으면 해당 API Worker를 선택하고, 없으면 사용 가능한 가장 높은 호환 Worker를 선택합니다.
3. Worker 종료 코드 `74`는 사용자가 VPDL 버전을 전환했다는 뜻이며, Launcher가 새 선택값으로 즉시 다시 시작합니다.
4. 예상하지 못한 Worker 종료는 최대 3회 재시작합니다.

## 제한사항

새 VPDL API는 해당 API로 빌드한 Worker가 설치 패키지에 포함되어 있어야 합니다. VPDL DLL을 다른 버전 Worker에 바꿔 넣어 실행하지 않습니다.
