VisionQC 오프라인 v4.7.21 / Agent 1.3.11

1. 압축 해제 후 VisionQC_Agent_Installer_v1.3.11.exe 실행.
2. http://127.0.0.1:17891/ 접속, Web 4.7.21 / Agent 1.3.11 확인.
3. Green Simulation에서 상세 진단 로그 켜기.
4. GPU 메모리 선할당 해제를 켠 뒤 Runtime File Load → Simulation Start.
   다른 GPU/모델/판정 설정은 유지하고 한 옵션만 비교하세요.
5. 실패 시 %LOCALAPPDATA%\VisionQC\LocalAgent\logs 의
   last-sdk-failure.txt / last-green-failure.txt /
   last-gpu-failure.txt / cognex-sdk-log-locations.txt 확인.

H100 + Server 2022 현장 해결은 아직 확정되지 않았습니다.
자세한 원인 판단 근거와 안전한 확인 방법은 동봉한 릴리스 노트를 보세요.
자료 반출, 보안 해제, GPU 드라이버 변경은 하지 않습니다.

VPDL 없이도 웹/CSV/이미지 분류/이력 사용이 가능합니다.
실제 AI 검사는 설치된 VPDL와 라이선스가 필요합니다.
진단 완료 후 상세 로그를 끄고 Agent 재시작/Runtime File Load 해주세요.
기존 데이터·Workspace를 덮어쓰거나 삭제하지 않습니다.
