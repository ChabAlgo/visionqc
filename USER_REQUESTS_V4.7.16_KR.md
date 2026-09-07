# VisionQC v4.7.16 사용자 요청 반영

## 요청

- 오프라인 패키지를 VPDL 4.2 전용으로 만들지 말고 다른 PC의 VPDL 4.0을 포함한 전체 설치 버전에 대응한다.
- 요구사항과 호환 구조를 Markdown 문서로 유지한다.

## 반영 기준

- 제품 버전 문자열을 하드코딩하지 않고 `ViDi.NET.Local.dll`의 실제 API 버전과 `bin\vidi_*.dll` 쌍으로 설치본을 판별한다.
- 정확 API Worker가 있으면 우선 사용한다.
- 정확 API Worker가 없으면 Universal Worker에 대상 PC의 Studio/API 경로를 전달한다.
- Universal Worker는 오프라인 설치 파일의 필수 구성요소이며 누락된 패키지는 테스트에서 실패한다.
- Cognex VPDL Runtime, 라이선스, GPU는 대상 PC에 정식 설치되어 있어야 한다.
- 실제 VPDL 4.0 처리 성공 여부는 VPDL 4.0 PC에서 Runtime Load 및 시뮬레이션으로 최종 확인한다.
