VisionQC Offline 4.7.24 / Agent 1.3.14

1. 승인된 반입 절차로 ZIP을 가져가 압축을 풀고 VisionQC_Agent_Installer_v1.3.14.exe를 실행하세요.
2. 웹 4.7.24 / Agent 1.3.14를 확인하세요. 기존 설정과 검사 이력은 유지합니다.
3. Green의 "원본 방식 · 독립 Green 검사"를 켜고 기존 Workspace·이미지·GPU 번호 그대로 사용하세요.
4. Runtime File Load → Simulation Start. 로그에 "Green DLL 검색: 원본 방식 (C)"가 나오는지 확인하세요.
5. 처리 건수가 증가하고 검사 완료 후 CSV·히트맵·이력에 결과가 남는지 확인하세요.

이번 버전은 현장 비교에서 성공한 C 방식의 DLL 검색을 일반 독립 Green 검사에도 적용합니다.
비교진단을 다시 실행하거나 DLL을 직접 지울 필요는 없습니다. 드라이버·학습 프로그램 설정도 변경하지 않습니다.
실제 H100 보안 서버에서의 최종 해결은 이 버전으로 확인해야 합니다.

오프라인 실행: 바탕화면 "VisionQC 오프라인 실행".
Agent 실행 후 브라우저 주소: http://127.0.0.1:17891/
VPDL이 없어도 웹·CSV·이력 기능은 실행됩니다. VPDL 검사는 별도 Runtime/라이선스가 필요합니다.

실패가 남으면 "VisionQC Green 비교 진단"에서 비교 시작을 누르세요.
바로가기가 없다면 Win+R에 아래 명령을 입력하세요.
"%LOCALAPPDATA%\VisionQC\LocalAgent\VisionQC.LocalAgent.exe" --green-compare
결과 화면의 A/B/C/D와 NVIDIA DLL 경로·버전을 확인하세요.
이미지·Workspace·DB·상세 로그는 보안 서버 내부에 보관하세요.

자세한 변경·검증 범위: RELEASE_NOTES_v4.7.24_KR.md
