# VisionQC v4.7.5 사용자 요청 정리

## 요청

1. 화이트 모드에 남은 어두운 박스를 모두 밝은 계열로 전환하고, 밝아서 읽기 어려운 텍스트를 어둡게 표시한다.
2. P163GG23M2100004가 Crack `0.7495`와 Welding `0.5071`을 함께 가질 때, 다른 Tool NG 제외 기준 `0.70`에서 Welding 실제 NG 최소 Score로 남지 않게 한다.

## 반영 기준

- 빈 상태, 메인 대시보드, Score 분석, Simulation, 이미지 분류 화면을 화이트 테마 검증 범위로 둔다.
- 실제 NG 최소 Score는 Cell ID + Position을 묶어 다른 Tool의 NG Score가 기준 이상이면 해당 Tool 후보를 제외한다.
- 메인 화면과 Score 분석 화면은 동일한 최소 Score 계산 함수를 사용한다.
