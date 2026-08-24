# VisionQC v4.7.3 / Local Agent v1.2.3

## 수정 사항

- Integrated Simulation에서 Runtime File Load를 완료한 뒤 Green Simulation으로 전환해도, GPU 설정과 Green Workspace·활성 Position이 같으면 기존 Runtime을 재사용합니다.
- GPU 장치/사용 여부, Green Workspace 경로, 활성 Position이 달라진 경우에는 안전하게 Runtime File Load를 다시 요구합니다.
- 좌측 레일에 동작하는 다크·화이트 테마 전환 버튼을 추가했습니다. 선택한 테마는 브라우저에 저장됩니다.
- 테마 아이콘을 흰쪽과 어두운쪽이 분명히 나뉜 반반 원형 아이콘으로 교체했습니다.

## 검증

- Node 정적·회귀 테스트와 Playwright 브라우저 테스트를 통과했습니다.
- Agent x64 Release 빌드를 오류 없이 완료했습니다.

상세 요구사항은 [USER_REQUESTS_V4.7.3_KR.md](USER_REQUESTS_V4.7.3_KR.md)를 참고합니다.
