# VisionQC v4.7.6 사용자 요청 정리

## 요청

1. 화이트 모드에 남아 있는 어두운 박스를 모두 밝은 계열로 바꾸고, 밝은 배경에서 읽기 어려운 텍스트를 어둡게 한다.
2. CSV FullPath·SQLite 이미지 보기 팝업과 확대 Score 차트 팝업도 화이트 모드에 맞게 정리한다.
3. Cell ID `P163GG23M2100004`에서 Crack `0.7495`가 다른 Tool NG 제외 기준 `0.70` 이상인데 Welding `0.5071`이 메인의 실제 NG 최소 Score로 남는 현상을 확인한다.

## 반영 기준

- 팝업은 `#vq43-shell` 바깥에 있으므로 별도 화이트 테마 선택자를 둔다.
- 팝업의 카드·헤더·탭·경로·점수·버튼·차트 보조 정보는 밝은 하늘색 계열, 텍스트는 짙은 남색 계열로 표시한다.
- 실제 사진이 보이는 이미지 캔버스는 대비 확보를 위해 검정 배경을 유지한다.
- 실제 NG 최소 Score는 Cell ID + Position의 모든 관련 CSV 행을 기준으로 다른 Tool NG Score가 제외 기준 이상이면 후보에서 제외한다.
- 메인 화면과 Score 분석 화면은 공통 `actualNgScoreCandidates` 함수로 같은 후보를 계산한다.

## 검증 사례

- Cell ID: `P163GG23M2100004`
- Position: `AN(TOP)`
- Crack: `NG 0.7495`
- Welding: `NG 0.5071`
- 다른 Tool NG 제외 기준: `0.70`
- 기대 결과: Welding `0.5071`은 실제 NG 최소 Score, Cell별 Score 점, 통계, CSV 후보에서 제외된다.
