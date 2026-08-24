# VisionQC 사용자 요청 이력 · v4.7.0

기준일: 2026-08-24. 이 문서는 새 대화에서도 요구사항과 결정 사항을 이어가기 위한 기준 문서다.

## 이번 버전에 반영한 요청

1. 웹 v4.7.0과 Local Agent v1.2.0으로 버전을 올리고, 설치 EXE와 오프라인 ZIP을 제공한다.
2. 검사 이력은 브라우저 메모리가 아니라 Local Agent의 SQLite DB에 영구 저장하고, 메인 대시보드 최상단과 이력 화면에서 날짜별 NG율·Cell ID·Position·Tool·결과 조건으로 조회한다.
3. CSV를 분석만 할 때도 FullPath를 보존해 원본 이미지와 저장된 Green Heatmap Overlay를 열 수 있게 한다.
4. 100만 행처럼 큰 CSV는 브라우저가 전부 읽지 않는다. Agent가 CSV를 줄 단위로 SQLite에 직접 적재하고, 웹은 서버 측 페이지 단위 결과만 받는다.
5. AI Suggest는 선택한 이미지 파일명에서 Position 문자열을 한 개로 판정하고, 해당 Position의 Green Workspace를 Runtime에 자동 로드해 1회 검사한다.
6. AI 검사 결과는 Tool별 Score와 NG Heatmap Overlay 저장을 지원하고, 화면에서 원본/Overlay를 전환한다. Heatmap은 Green 검사 전용이며 Integrated/Blue에는 적용하지 않는다.
7. 설정 아이콘은 이모지가 아닌 SVG 톱니바퀴로 표시한다.
8. Local Agent의 History 관련 책임을 `Services/HistoryService.cs`, `Services/CsvHistoryFileImporter.cs`, `Persistence/SqliteRunStore.cs`로 분리하고, 각 폴더 문서를 유지한다.
9. 회귀 테스트, Agent 빌드, 설치 패키지 빌드, 실제 설치/실행 확인, Git 커밋·푸시·태그·GitHub Release를 수행한다.

## 데이터 보존 원칙

- DB 위치: `%LOCALAPPDATA%\VisionQC\LocalAgent\data\visionqc-history.sqlite`
- 저장 대상: 실행/가져오기 이력, Cell ID, Position, 캡처 시각, 검사 시각, FullPath, 전체 결과, Tool 결과·Score·Overlay 경로.
- 저장하지 않는 대상: 원본 이미지 바이트, 브라우저의 전체 CSV 행 목록, VPDL Workspace 파일 자체.
- 따라서 원본 이미지를 열려면 CSV 또는 검사 당시의 FullPath가 현재 PC에서 여전히 유효해야 한다. DB를 지우기 전까지 이력은 Agent 재설치 후에도 남는다.

## 사용 전제와 제한

- 대용량 직접 가져오기는 CSV 전용이다. XLSX는 기존 웹 분석 경로를 사용한다.
- 직접 CSV 가져오기는 일반적인 인용부호/쉼표 필드를 지원하지만, 하나의 셀 값이 여러 물리 줄에 걸친 CSV는 지원하지 않는다.
- AI 자동 검사는 파일명에서 설정된 Position이 정확히 하나만 일치하고, 그 Position에 Green Workspace와 Stream이 설정돼 있을 때만 실행한다. 모호하거나 미설정이면 임의로 검사하지 않고 오류를 알린다.
- 실제 VPDL Tool/Heatmap의 결과 품질은 설치된 Cognex VPDL Runtime, 라이선스, Workspace, 원본 이미지에 의존한다. 해당 실데이터 조합은 배포 전 운영 PC에서 한 장으로 최종 확인한다.

## 후속 요청 기록 원칙

새 기능 요청이 생기면 이 파일을 덮어쓰지 말고 다음 버전의 `USER_REQUESTS_Vx.y.z_KR.md`를 만들고, 구현 여부·제한·검증 결과를 함께 기록한다.
