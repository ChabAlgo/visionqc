# VisionQC 폴더 역할·관리 기준

이 문서는 `visionqc` 저장소의 폴더별 책임과 보관 규칙을 빠르게 파악하기 위한 기준이다. 기능 요청은 `USER_REQUESTS_V4.7.0_KR.md`, 구조 변경은 `ARCHITECTURE_V4.7.0_KR.md`를 함께 읽는다.

| 경로 | 역할 | 수정/보관 규칙 |
| --- | --- | --- |
| `.github/` | GitHub Actions 등 저장소 자동화 | 배포/검증 흐름을 바꿀 때만 수정한다. |
| `assets/` | 오프라인 웹에서 쓰는 JS, CSS, 폰트, 로고 | 외부 CDN 없이 동작하도록 유지한다. 새 자산은 Installer EmbeddedResource에도 추가한다. |
| `downloads/` | 사용자 배포 EXE·오프라인 ZIP·안내문 | 버전 파일명으로 추가한다. 이전 버전은 복구용으로 삭제하지 않는다. SHA-256은 `RELEASE_MANIFEST.json`과 일치해야 한다. |
| `LocalAgent_v0.2.12/` | Windows 파일 선택, SQLite, VPDL Runtime을 제공하는 .NET Agent 소스 | 아래 Agent 세부 표와 하위 `*_KR.md`를 따른다. |
| `tests/` | Web 정적 회귀와 Playwright 브라우저 회귀 | UI/API 변경마다 회귀 항목을 추가하고 `npm.cmd test`, `npm.cmd run test:browser`를 실행한다. |
| `node_modules/` | 로컬 Node 개발 의존성 | Git에 저장하지 않는다. `npm ci`로 재생성한다. |
| `playwright-report/`, `test-results/` | 브라우저 테스트 산출물 | Git에 저장하지 않는 일회성 결과다. |
| `index.html` | GitHub Pages·오프라인 UI의 진입 HTML | 새 CSS/JS를 추가하면 cache version과 Installer payload를 함께 갱신한다. |
| `visionqc-extension.js` | Web 상태, 화면 렌더링, Agent API 연결 | 기존 화면과 신규 화면을 연결하는 조율 파일이다. 대용량 데이터는 이 파일에 누적하지 않고 Agent API를 사용한다. |
| `visionqc-extension.css`, `visionqc-v470.css` | 기본 UI와 v4.7 이력 대시보드 UI | 화면 단위 CSS는 별도 파일을 우선 사용한다. |
| `RELEASE_MANIFEST.json` | 현재 배포 버전·파일명·SHA-256 | 배포 파일을 다시 만들면 해시를 반드시 다시 기록한다. |
| `RELEASE_NOTES_*.md` | 버전별 변경·검증·운영 확인 항목 | 릴리스마다 새 파일을 만든다. |
| `USER_REQUESTS_*.md` | 사용자의 요청, 결정, 제한사항 | 새 요구는 다음 버전 파일에 먼저 적고 구현/검증 결과를 함께 갱신한다. |
| `ARCHITECTURE_*.md` | 버전별 구조와 책임 분리 기준 | 새 대화에서 가장 먼저 읽는 문서 중 하나다. |

## Local Agent 세부 폴더

| 경로 | 역할 | 수정/보관 규칙 |
| --- | --- | --- |
| `LocalAgent_v0.2.12/Engine/` | Green Overlay, Blue Crop, Simulation 처리 | VPDL 실제 결과 로직이다. Tool/이미지 처리 변경에는 실제 Workspace 이미지 검증을 추가한다. |
| `LocalAgent_v0.2.12/Services/` | Picker, Naming Parser, Position Resolver, Image Preview, History Service, CSV Importer | HTTP 조율/Engine과 독립된 재사용 서비스다. 새 CSV/이미지/규칙 기능은 우선 이곳에 둔다. |
| `LocalAgent_v0.2.12/Persistence/` | SQLite 이력 스키마·쓰기·검색 | DB 접근은 `SqliteRunStore`로 한정한다. 스키마 변경에는 migration·index·기존 DB 테스트가 필요하다. |
| `LocalAgent_v0.2.12/Domain/` | 파일명 규칙 등 공용 도메인 모델 | Web/Agent가 같은 의미를 유지할 수 있는 순수 모델만 둔다. |
| `LocalAgent_v0.2.12/OfflineInstaller/` | 단일 설치 EXE 제작 | 새 Web/Agent 파일은 `.csproj` EmbeddedResource와 `Program.cs` Payload 목록을 모두 갱신한다. |
| `LocalAgent_v0.2.12/tests/` | Agent 정적 테스트와 CSV fixture | API/서비스 변경 시 fixture와 회귀 테스트를 추가한다. |
| `LocalAgent_v0.2.12/lib/` | 빌드에 필요한 로컬 DLL | 라이선스/호환성 확인 없이 교체하지 않는다. |
| `LocalAgent_v0.2.12/Properties/` | Assembly 버전 메타데이터 | Agent 버전 변경 시 `Program.cs`, Installer 버전, 빌드 안내와 함께 갱신한다. |
| `LocalAgent_v0.2.12/bin/`, `obj/` | 빌드 산출물/중간 산출물 | 직접 수정하지 않는다. 배포용 Installer는 `bin`의 Release 결과를 포함한다. |
| `LocalAgent_v0.2.12/.cache/` | 로컬 테스트 임시 파일 | Git에 저장하지 않으며 운영 이력 DB와 분리한다. |

## 백업 규칙

버전을 배포할 때마다 `C:\Temp\AUtempR\01. Project\0.MyProject\Vision_QC\BackUp\VisionQC_v<web>_Agent_v<agent>` 폴더를 만든다. 안에는 Git commit 기준 `Source/`, 원본 tar 스냅샷, `Release/`의 EXE·ZIP·manifest·문서를 둔다. Git 태그와 backup manifest의 commit/해시가 일치해야 한다.
