VisionQC v4.7.19 / Local Agent v1.3.9 - Green 호환/진단판

1. 회사의 승인된 반입 절차에 따라 패키지를 보안 서버로 가져갑니다.
2. 기존 검사를 중지한 뒤 VisionQC_Agent_Installer_v1.3.9.exe를 실행합니다.
3. 바탕화면 "VisionQC 오프라인 실행" 또는 브라우저 http://127.0.0.1:17891/ 사용.
4. Green Runtime / HeatMap에 두 가지 옵션이 추가됩니다(기본 OFF).
   - TensorRT 해제 (호환 검사): 먼저 이것만 켜서 같은 조건으로 비교.
   - 새 Runtime으로 검사 (진단): TensorRT 옵션을 끄고 이것만 켜서 비교.
   - 필요하면 둘 다 켜서 비교. 각 검사 사이 Runtime File Load를 다시 실행.
5. 원본 모델은 저장/수정하지 않지만 TensorRT 해제로 속도/Score가 달라질 수 있습니다.
6. 결과 폴더는 서버 내부의 별도 시험 폴더를 사용하세요. 오류를 OK로 바꾸거나 자동 무시하지 않습니다.
7. 진단은 %LOCALAPPDATA%\VisionQC\LocalAgent\logs 안에만 저장합니다.
   last-green-inference.txt, last-green-failure.txt를 발생 시각과 대조하세요.
   상세 로그에는 경로가 포함될 수 있습니다. 모델/이미지/로그 반출은 필요하지 않습니다.
8. H100의 6cc4a157 해결은 아직 미확정입니다. 서버 내 실제 비교가 필요합니다.
9. VPDL 없이도 화면/이력/분류 서버는 실행됩니다. AI 검사에는 VPDL/라이선스/GPU가 필요합니다.
10. 4.0/API 8.0, 4.2/API 8.2 Worker 및 Universal/Core를 포함합니다. 미검증 API나 모든 GPU에 대한 호환 보증은 아닙니다.
