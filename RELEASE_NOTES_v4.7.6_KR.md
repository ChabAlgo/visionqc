# VisionQC v4.7.6 / Local Agent v1.2.3

## 수정 사항

- 화이트 모드의 CSV FullPath·SQLite 실제 NG 이미지 팝업과 확대 Score 차트 팝업을 밝은 하늘색 계열로 정리했습니다.
- 팝업 헤더, Heatmap 탭, 경로, Tool Score 칩, 닫기·원위치 버튼, 확대 차트의 안내·도움말을 모두 어두운 텍스트로 표시합니다.
- 실제 이미지가 표시되는 캔버스만은 이미지 가장자리와 명암을 정확히 보기 위해 검정 배경을 유지합니다.
- 실제 NG 최소 Score 제외는 Cell ID + Position 단위의 다른 Tool NG 최고 Score를 기준으로 적용합니다. 예를 들어 Crack `0.7495`가 제외 기준 `0.70` 이상이면 Welding `0.5071`은 메인과 Score 분석 모두의 후보에서 제외됩니다.

## 확인

- JavaScript 문법 검사와 자동 회귀 테스트를 실행합니다.
- 브라우저 회귀 테스트에서 일반/확대 Score 차트의 이미지 열기 동작을 확인합니다.
- Local Agent v1.2.3을 다시 설치·실행하여 상태 API와 Runtime 상태를 확인합니다.

## 배포물

- `VisionQC_Agent_Installer_v1.2.3.exe`
- `VisionQC_Offline_v4.7.6.zip`
