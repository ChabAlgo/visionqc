# VisionQC GitHub Pages v4.4.23

GitHub Pages 정적 배포용 VisionQC Web입니다. 실제 VPDL Runtime/GPU 작업은 사용자 PC의 VisionQC Local Agent v0.2.3에서 실행합니다.

## v4.4.23 핵심
- HSAGP 참고형 좌측 icon rail + 메뉴 버튼 확장 drawer
- 분류 화면 TOPTEC 이미지 로고 적용, React 상단바는 분류에서만 표시
- Simulation Options 누적 CSS/재렌더 충돌 정리 및 별도 consistency stylesheet 적용
- Judgement/Tool 구조 변경 시 Options/페이지 scroll 위치와 focus 보존
- Progress Update exact batch: 1이면 매 이미지, 5면 5/10/15... + 마지막 잔여 반영
- Live Simulation 결과를 Main/Analysis 기존 분석 모델에 직접 반영
- 상세 Progress Log + Auto Scroll + Elapsed/ETA/img/s/Batch 표시
- Workspace Runtime Structure: Stream, Tool Type, Tag/Class/Feature 표시
- Tool Settings의 Position별 체크 제거, ToolName text input + Runtime 존재 유무 색상 표시
- Custom Position은 Settings/Main/Analysis/Simulation/Result/실제 NG 경로에 공통 적용
- Simulation 실행 중 옵션/Workspace/Position 변경 잠금

## 배포
Repository root에 이 ZIP의 내용물을 그대로 놓고 GitHub Pages를 `main / (root)`로 배포합니다.

권장 Local Agent: **v0.2.3**
