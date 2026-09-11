VisionQC 오프라인 패키지 v4.7.29 / Agent v1.3.19

1. VisionQC_Agent_Installer_v1.3.19.exe를 실행합니다.
2. 설치 프로그램은 LocalAppData\VisionQC\LocalAgent에 Agent와 웹 UI를 설치하고,
   visionqc-agent:// 프로토콜을 등록한 뒤 오프라인 화면을 자동으로 엽니다.
   기존 Agent가 있으면 실행 중인 Launcher/Worker를 종료하고 기존 프로그램 파일을 제거한 뒤 새 버전을 설치합니다.
   data, logs, output, VPDL Worker 선택값은 보존합니다.
3. 이후에는 바탕화면의 "VisionQC 오프라인 실행" 바로가기를 사용하면 됩니다.
4. 모든 검사 경로는 시스템 DLL 우선 검색을 사용합니다. 실험용 비교 옵션은 제거했습니다.
   Runtime File Load → Simulation Start로 검사합니다. AI SUGGEST도 로컬 Runtime을 사용합니다.
5. 파일명 날짜 형식과 규칙 저장/불러오기 및 JSON 내보내기/가져오기는 분석 Input 설정에서 선택합니다.
   메인 날짜별 NG율 클릭으로 전체 대시보드를 필터하고 전체보기로 복원합니다.
6. Simulation 통합 results CSV에는 Position, Workspace, Tool 결과가 포함되며 검사 이력의 대용량 CSV 직접 저장에 사용할 수 있습니다.
7. DB 전체 삭제는 SQLite 검사·Tool 이력만 비우며 원본 이미지와 CSV 파일은 삭제하지 않습니다.

인터넷 연결, GitHub Pages, 외부 CDN은 필요하지 않습니다.
Cognex VPDL Runtime과 해당 라이선스는 별도 제품이므로 시뮬레이션을 위해 기존 설치가 필요합니다.
