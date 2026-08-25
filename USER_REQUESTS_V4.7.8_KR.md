# v4.7.8 사용자 요청 기록

## 요청 원문 요약

- Grab 이미지에서 원본 이미지 Viewer가 비어 보이는 원인을 확인한다.
- 분석의 Score 그래프 점을 눌러 여는 이미지 Viewer에서도 Heatmap을 볼 수 있게 한다.

## 반영 기준

- 이미지 확장자 문제가 아니라 FullPath의 실제 파일 존재 여부를 Agent에서 확인한다.
- CSV·실제 NG·SQLite에 이미 저장된 Overlay는 기존 Viewer 탭을 그대로 사용한다.
- Overlay가 없는 분석 Viewer는 현재 사전 로드된 Green Runtime으로 해당 이미지 1장을 다시 검사해 Tool별 Heatmap을 만든다.
- 원본 경로가 사라진 경우에는 빈 이미지 대신 명확한 오류를 표시한다.
- 변경 뒤 정적·브라우저 회귀 테스트, 오프라인 설치 파일 재빌드, 에이전트 재설치, 백업, GitHub 배포를 수행한다.
