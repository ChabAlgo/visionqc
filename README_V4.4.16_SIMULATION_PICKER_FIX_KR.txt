VisionQC v4.4.16 - Simulation Picker Fix

수정 내용
- Workspace 선택 버튼 클릭 복구
- Image Folder 선택 버튼 클릭 복구
- Output Folder 선택 버튼 클릭 복구

원인
- 선택 버튼에도 data-sim-field 속성이 있었고, 페이지 바인딩 과정에서 버튼 onclick이 입력 필드용 stopPropagation 핸들러로 덮어써졌습니다.
- 따라서 버튼의 simulation-browse 액션이 실행되지 않았습니다.

수정
- 입력값 동기화 바인딩 대상을 input/select/textarea로 한정했습니다.
- 선택 버튼은 기존 simulation-browse 핸들러를 유지합니다.

Local Agent
- v0.1.1 그대로 사용
- 다시 빌드하거나 REGISTER_PROTOCOL.cmd를 다시 실행할 필요 없음
