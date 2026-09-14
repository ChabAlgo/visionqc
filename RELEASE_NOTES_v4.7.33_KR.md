# VisionQC v4.7.33 / Agent v1.3.20

## 변경 내용

- 두 개 이상의 Position을 사용할 때 Position마다 독립 Runtime과 Worker를 준비해 동시에 Simulation합니다.
- NVIDIA GPU 목록을 자동 감지해 Position 순서대로 균등 순환 배분합니다. GPU가 하나면 여러 Position Worker가 같은 GPU를 공유하며, 동시 실행 수는 1~10개로 조절할 수 있습니다.
- `Runtime File Load`에서 Position별 Workspace를 한 번만 로드하고 Simulation 완료 후에도 같은 Runtime을 재사용합니다. AI SUGGEST도 해당 Position Runtime을 빌려 사용한 뒤 반환합니다.
- 병렬 결과 CSV는 Position별 임시 결과를 충돌 없이 생성한 뒤 기존 통합 형식의 CSV 하나로 합칩니다.
- 대시보드의 미검 Cell ID 이미지와 검사 이력의 Cell 이미지 탐색에서 좌우·상하 방향키로 다음/이전 Cell을 순서대로 볼 수 있습니다. 첫 Cell과 마지막 Cell에서는 더 진행하지 않습니다.

## 호환성과 안전 처리

- 기존 직렬 실행 경로는 그대로 유지하며, Position이 하나이거나 원본 프로세스·Fresh Runtime·TensorRT 비활성화 호환 옵션을 사용하면 기존 방식으로 실행합니다.
- 통합 병렬 검사 중에는 한 Worker가 다른 Worker의 Crop 임시 이미지를 지우지 않으며, 모든 Position 완료 또는 중지 후 Coordinator가 한 번만 정리합니다.
- SQLite 검사 이력, Tool 판정, Position 판정, 이미지 열거, 파일명 규칙과 출력 CSV 열 구성은 기존 처리기를 그대로 사용합니다.

## 검증 범위

- Agent Launcher, Core Worker, VPDL 4.0(API 8.0)·4.2(API 8.2) Worker, Universal Worker, Offline Installer 전체 x64 Release 빌드를 통과했습니다.
- 미검 및 검사 이력 이미지 탐색은 Chromium에서 3개 Cell의 처음·중간·마지막 이동과 양 끝 정지를 자동 검증했습니다.
- 실제 VPDL 4.0과 GPU 0에서 3개 Position·이미지 9건을 병렬 검사해 통합 CSV, SQLite 9건, Tool 결과 36건과 Runtime 재사용을 확인했습니다. AI SUGGEST 2회도 재로드 없이 처리했으며 SQLite 건수는 늘지 않았습니다.
- 정적 회귀 137건, Chromium 회귀 44건과 설치 파일·오프라인 ZIP 감사를 통과했습니다.
- 실제 장시간 다중 Position VPDL 부하는 설치 PC의 GPU 메모리, Workspace 크기와 VPDL 라이선스 조건에 영향을 받으므로 동시 실행 수를 화면에서 낮출 수 있습니다.
