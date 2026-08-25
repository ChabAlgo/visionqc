# VisionQC v4.7.7 / Local Agent v1.2.3

## 수정 사항

- 이미지 분류 헤더의 PROGRESS·통계 그룹을 좌측 정렬하고, PROGRESS 카드를 밝은 하늘색 계열로 표시합니다.
- 화이트 모드에서 Workspace의 로드 대기·읽는 중·로드 완료 카드와 Tool 카드까지 밝은 표면과 어두운 텍스트로 통일했습니다.
- Simulation Options의 항목명·입력값·설명·버튼 글자를 한 단계 키워 고해상도 화면에서도 읽기 쉽게 했습니다.
- AI SUGGEST 결과는 검사 이력을 유지하되, 화면을 가리던 큰 패널 대신 좌측 하단의 작은 2줄 요약 카드로 표시합니다.
- 이미지 분류 화이트 모드의 회색·흰색 텍스트를 더 어두운 남색 계열로 보정했습니다.

## 확인

- JavaScript 문법 검사, 정적 회귀 테스트, Playwright 브라우저 회귀 테스트를 실행합니다.
- 화이트 모드의 분류 헤더와 Workspace 상태의 실제 DOM 색상을 확인합니다.
- Local Agent v1.2.3을 다시 설치·실행하여 오프라인 UI와 상태 API를 확인합니다.

## 배포물

- `VisionQC_Agent_Installer_v1.2.3.exe`
- `VisionQC_Offline_v4.7.7.zip`
