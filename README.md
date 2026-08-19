# VisionQC GitHub Pages v4.4.24

GitHub Pages 정적 배포용 VisionQC Web입니다. 실제 VPDL Runtime/GPU 작업은 사용자 PC의 VisionQC Local Agent v0.2.3에서 실행합니다.

## v4.4.24 핵심
- FHD 1920×1080 / 브라우저 100% 배율을 기준으로 Simulation 2열 레이아웃 재정렬
- 좌측 메뉴 확장 시 Web 본문을 72px → 272px 위치로 함께 이동해 메뉴 글자 잘림/겹침 제거
- Simulation Options 외곽 sticky와 내부 scroll 영역을 분리해 외부 클릭 시 scrollTop=0 초기화 제거
- Agent 상태/Runtime 갱신 시에도 Options/page/focus 위치를 보존하는 렌더 경로로 통일
- Tool Settings의 620px 강제 최소폭을 제거하고 4열 고정 레이아웃으로 변경해 가로 드래그 제거
- 1439px 이하에서는 Simulation을 1열로 전환하는 반응형 fallback 추가
- v4.4.24 회귀검사 7종 추가
- Local Agent v0.2.3의 CS1503 원인인 잘못된 GPU memory API 호출과 CMD BOM 문제 수정

## 기존 핵심
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
