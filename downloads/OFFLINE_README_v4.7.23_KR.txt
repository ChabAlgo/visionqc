VisionQC Offline 4.7.23 / Agent 1.3.13

1. 승인된 반입 절차로 ZIP을 가져가 압축을 풀고 VisionQC_Agent_Installer_v1.3.13.exe를 실행하세요.
2. 바탕화면의 "VisionQC 오프라인 실행"을 열거나 http://127.0.0.1:17891/ 에 접속하세요.
3. Green에서 "원본 방식 · 독립 Green 검사"와 "상세 진단 로그"를 켜고
   기존 설정 그대로 Runtime File Load → Simulation Start를 한 번 실행하세요.
4. 검사가 끝나거나 실패하면 바탕화면 "VisionQC Green 비교 진단"을 실행하세요.
5. "비교 시작" → 확인. 같은 이미지 1장으로 네 번 순차 검사합니다.
   각 조합은 최대 3분이며 진행 창에서 중지할 수 있습니다.
6. 자동으로 열리는 결과요약.txt의 A/B/C/D 결과를 확인하세요.
   네 줄의 성공/실패와 오류 문구만 알려주시면 됩니다.

결과: %LOCALAPPDATA%\VisionQC\LocalAgent\logs\green-comparisons
최초 SDK 오류: logs\green-runs\실행ID\last-sdk-failure.txt
오류 뒤 정리 단계: 같은 폴더의 last-cleanup-stage.txt

진단 요청, DLL 목록, 이미지 사본은 서버 안에만 보관하세요. 자동 업로드하지 않습니다.
원본 파일·Workspace·기존 DB를 수정하지 않고 진단 이미지를 DB에 넣지 않습니다.
다른 학습 프로그램이나 GPU/드라이버 설정도 변경하지 않습니다.
비교 진단 중 새 VisionQC 검사를 동시에 시작하지 마세요.

인터넷 없이 웹·CSV·이력을 실행할 수 있습니다. 실제 검사는 설치된 Cognex VPDL,
라이선스, GPU 및 Workspace가 필요합니다. Cognex DLL/라이선스·검사 이미지·DB는 미포함.
이 버전은 원인 분리 진단 릴리스이며 H100 서버 오류 해결 완료를 뜻하지 않습니다.
자세한 변경/검증 범위는 RELEASE_NOTES_v4.7.23_KR.md를 확인하세요.
