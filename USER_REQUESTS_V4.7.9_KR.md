# v4.7.9 사용자 요청 기록

## 요청 원문 요약

- 화이트 모드의 Runtime File Load 시 로딩/대기 상태가 다시 어둡게 보이는 문제를 고친다.
- Grab 이미지를 통합 시뮬레이션한 뒤 CSV/분석 Viewer에서 이미지가 열리지 않는 문제를 고친다.
- 화이트 모드의 Integrated Simulation, Green Simulation, Blue Crop 탭에서 현재 선택 상태를 확실히 구분한다.
- Google 계정 등 로그인 기반으로 사용자의 설정/이력을 다른 PC에서도 이어 쓰는 구조를 검토한다.

## 확인된 원인

- Runtime 상태 바탕색은 기본 CSS의 `.vq43-workspace-inspect.loading` 규칙이 화이트 모드 규칙보다 우선되어 남았다.
- Grab의 JPG 확장자는 Agent가 지원한다. 실제 실패 원인은 통합 시뮬레이션이 Green 검사 직후 `_VisionQC_BlueCrop_Temp` 아래 Crop 이미지를 삭제하지만, 결과 CSV의 `FullPath`는 삭제된 Crop 경로를 그대로 보관하던 구조다.
- 이미 삭제된 과거 Crop 파일은 소스 파일이나 백업이 없으면 이 변경으로 복구할 수 없다.

## 반영 기준

- 통합 Crop 결과는 기본적으로 Output 아래 `_VisionQC_Integrated_Images`에 저장하고 결과 CSV의 `FullPath`도 그 실제 파일을 가리킨다.
- 기존 로컬 설정의 과거 기본값 `false`는 한 번만 Viewer 보존 기본값 `true`로 이관한다. 이후 사용자가 체크를 해제한 값은 존중한다.
- 화이트 모드 Runtime 상태와 탭은 밝은 하늘색 표면, 어두운 가독성 텍스트, 선택 강조선을 사용한다.
- 정적/브라우저 회귀 테스트, 오프라인 설치 파일 재빌드, Agent 재설치, 백업, GitHub 배포를 수행한다.

## 로그인/동기화 검토 범위

- 이번 릴리스에 로그인 서버나 외부 전송 기능은 추가하지 않는다.
- 추후 Google OAuth + 사용자별 데이터베이스 + 행 단위 접근 제어를 별도 설계/승인 후 진행한다.
- 로컬 절대 경로와 원본 이미지는 기본적으로 클라우드 동기화하지 않는다.
