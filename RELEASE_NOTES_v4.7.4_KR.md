# VisionQC v4.7.4 / Local Agent v1.2.3

## 반영

- 축소된 Cell별 Score 차트에서도 점을 클릭하면 CSV FullPath 우선, 없으면 실제 NG 이미지 또는 Heatmap Overlay를 연다.
- 실제 NG를 NG로 검출한 Score의 다른 Tool NG 제외는 같은 Cell ID + Position의 모든 원본 CSV 행을 합쳐 판정한다. 예: Welding 0.5071의 동일 Cell에 Crack NG 0.7495가 있으면 기준 0.70에서 Welding은 제외된다.
- 화이트 모드의 카드/행/차트/모달 표면을 밝은 하늘색 레이어로 정리했다.
- 설정은 파일명 규칙과 Position 구성을 첫 행의 좌/우 2열로 배치하고, 나머지 입력 카드도 2열로 활용한다.
