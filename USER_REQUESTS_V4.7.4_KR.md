# VisionQC v4.7.4 반영 요청

## 요청 사항

1. Cell별 Score는 확대 없이도 점 클릭으로 이미지 확인.
2. 실제 NG 검출 최소 Score의 다른 Tool NG 제외 기준은 동일 Cell ID + Position 전체에서 적용.
3. 화이트 모드의 어두운 표면 제거, 하늘색 계열 레이어 구분, 설정 화면 2열 재배치.

## 구현 원칙

- CSV FullPath가 있으면 우선 표시하고, 없으면 실제 NG 이미지와 Heatmap Overlay를 사용한다.
- 기준 이상의 다른 Tool NG가 하나라도 있으면 실제 NG 검출 Score의 차트·KPI·CSV·최소값 후보에서 제외한다.
