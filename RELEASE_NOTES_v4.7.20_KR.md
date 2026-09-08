# VisionQC v4.7.20 / Local Agent v1.3.10

## 이번 수정
- Agent 시작 시 자동 GPU/License 점검을 위해 Control을 생성하고 폐기하던 동작을 제거했다.
- /api/runtime/check는 실행 중/사전 로드 상태를 읽기만 한다. 아직 실제 로드를 하지 않았다면 licenseVerified=false, deferred=true로 반환한다. License 확인을 했다고 표시하지 않는다.
- 실제 Runtime File Load에서 선택한 VPDL/GPU로 초기화한다. 기존 사전 로드 재사용, Integrated/Blue, 판정/Threshold, Crop/Overlay 저장, DB, 분류 기능은 유지한다.
- 구조 미리보기의 GPU 사용을 없애는 시도는 고해상도 Green 모델의 CPU 모드 거부를 실제 확인하여 제외했다. 기존의 명시적 Workspace 미리보기 기능은 GPU 지원 방식 그대로 유지한다.
- 시작/상태 확인 변경은 H100의 6cc4a157에 대한 가설 기반 수정이다. 정확한 원인이나 H100 해결이 확인된 것은 아니다. 다른 PC에서 성공한 시험을 H100 검증으로 표시하지 않는다.

## 파일명 규칙
기본 화면은 Cell ID와 날짜·시간 두 항목이다.
- TAB_J1037G87P611903999_20260807074705_CRACK AN(TOP)_BLUTOL.jpg → 2026-08-07 07:47:05
- 자동 찾기: 14자리 YYYYMMDDHHMMSS와 기존 YYYYMMDD_HHMMSS 지원.
- 위치 지정: 위 예시는 구분자 _, 날짜·시간 토큰 3. 분리형은 날짜 토큰 위치를 지정한다.
- 기존에 날짜/시간 위치를 따로 지정했던 설정은 ‘이전 날짜/시간 규칙 유지’로 이관한다. 고급 접힘 영역에서 이전 위치를 확인할 수 있다.
- Cell ID 후보 길이와 앞부분 추출 길이는 변경하지 않는다. 원본 파일명 자체를 바꾸지 않는다.
- 불가능한 날짜/시간은 인정하지 않고, 후보가 여러 개면 임의 선택하지 않는다.
- 새 분석/이력 저장부터 적용한다. 기존 DB의 촬영 시각을 일괄 재작성하지 않는다.

## 보안 오프라인 서버 확인
1. 승인 절차로 새 오프라인 ZIP을 반입하고 압축을 푼다.
2. VisionQC_Agent_Installer_v1.3.10.exe 실행. 기존 모델/이미지/DB는 삭제하지 않는다.
3. 브라우저 주소 http://127.0.0.1:17891 을 열고 Web 4.7.20 / Agent 1.3.10 확인.
4. 같은 VPDL 4.0, GPU 사용, GPU 0, Workspace, 이미지, Tool, Threshold를 유지한다.
5. 이번 원인 분리 시험은 TensorRT 해제와 새 Runtime 진단 옵션을 둘 다 끈 기본 설정으로 시작한다. 같은 네 조합을 다시 반복할 필요는 없다.
6. Runtime File Load 후 Simulation Start. 출력은 기존 결과와 다른 서버 내부 시험 폴더를 사용한다.
7. 이 서버에서 실제 검사 성공 여부가 최종 확인 항목이다. 모델·이미지·로그 반출이나 보안 해제는 필요하지 않다.

로그의 GPU_LIFECYCLE에서 Startup probe skipped, Actual runtime initialization을 구분할 수 있다.

## 검증 결과
- C# 7.3 파서 실행 시험 16개, 코드 회귀 112개, 브라우저 회귀 26개 통과.
- 실제 RTX 4050 / VPDL 4.0 기본 Green 검사: 6건, Tool 24건, 저장 히트맵 8개. 완료 후 사전 로드 Runtime 재사용 가능 확인.
- 실제 VPDL 4.2: 두 Workspace 구조 미리보기 성공 후 TensorRT 해제 + 새 Runtime 옵션으로 6건/24건/8개 완료. 호환 Runtime 재사용 안 함 확인.
- VPDL 없는 Core: 웹 실행, AI 검사 거절, 14자리 촬영 시각 미리보기와 SQLite 저장(2026-08-07T07:47:05) 확인.
- UI의 날짜·시간 위치 저장/새로고침 및 이전 비연속 날짜/시간 위치 유지 확인.
- H100 + Server 2022 + 현장 CA(BOT) 모델은 직접 시험하지 못했다. 해당 오류가 해결됐다고 확정하지 않는다.
