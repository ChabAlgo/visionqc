# VisionQC GitHub Pages / 버전 관리 가이드

## 1. 이 저장소에서 관리하는 것

이 저장소에는 GitHub Pages에서 실제로 내려주는 정적 웹 배포 파일만 둡니다.

- `index.html`
- `assets/`
- `visionqc-extension.js`
- `visionqc-extension.css`
- `.nojekyll`
- 버전 문서

검사 이미지, NG 이미지, 결과 CSV 등 실제 생산 데이터는 저장소에 올리지 않습니다.

## 2. 최초 배포

1. GitHub에서 새 Repository를 생성합니다. 예: `visionqc`.
2. GitHub Desktop에서 해당 Repository를 Clone합니다.
3. 이 배포본의 내용 전체를 Clone된 Repository 폴더 최상위에 복사합니다.
4. GitHub Desktop에서 Summary에 `Initial VisionQC v4.4.10`을 입력합니다.
5. `Commit to main`을 누릅니다.
6. `Push origin`을 누릅니다.
7. GitHub 웹 → Repository → `Settings → Pages`로 이동합니다.
8. `Source = Deploy from a branch`, `Branch = main`, `Folder = /(root)`로 설정합니다.
9. 배포된 `https://<ID>.github.io/<repo>/` 주소에서 확인합니다.

## 3. Commit / Tag / Release 차이

### Commit

파일이 어떻게 바뀌었는지 기록하는 기본 단위입니다.

예:

`Fix NG minimum score calculation`

### Tag

특정 Commit에 버전 번호를 붙이는 표식입니다.

예:

`v4.4.10`

안정 버전으로 확정한 Commit에만 Tag를 붙이는 것을 권장합니다.

### Release

Tag를 사용자에게 보기 좋게 정리한 배포 페이지입니다. 변경사항을 적고 Offline ZIP 같은 파일을 첨부할 수 있습니다.

예:

- Tag: `v4.4.10`
- Release title: `VisionQC v4.4.10`
- Asset: `VisionQC_Offline_v4.4.10.zip`

## 4. 추천 버전 번호 규칙

현재 규칙을 그대로 이어갑니다.

`v4.4.10 → v4.4.11 → v4.4.12 ...`

프로그램 화면, `VERSION.txt`, Tag, Release의 버전 번호를 동일하게 맞추는 것이 중요합니다.

## 5. 평상시 새 버전 배포 순서

먼저 새 버전을 로컬에서 충분히 확인한 다음 GitHub에 올립니다.

1. GitHub Desktop에서 `Fetch origin`.
2. 새 배포 파일을 Repository에 덮어쓰기.
3. Changes 확인.
4. Commit message 작성.
5. `Commit to main`.
6. `Push origin`.
7. GitHub Pages 주소에서 실제 동작 확인.
8. 정상 확인 후 그 Commit에 Tag 생성.
9. GitHub 웹에서 해당 Tag를 사용하여 Release 생성.
10. Offline ZIP이 있으면 Release asset으로 첨부.

## 6. 버전이 잘못됐을 때 — 과거 파일로 수동 덮어쓰기 금지

예:

- `v4.4.10`: 정상
- `v4.4.11`: 새 기능
- `v4.4.12`: 심각한 버그

`v4.4.12`가 이미 `main`에 Push된 상태라면 과거 ZIP을 찾아 덮어쓰는 방식보다 Git 기록으로 취소합니다.

GitHub Desktop:

`History → 문제 Commit 우클릭 → Revert Changes in Commit → Push origin`

Revert는 과거 기록을 지우지 않고 "이 변경을 취소했다"는 새 Commit을 추가합니다. 그래서 버그가 있었던 이력과 원복 이력이 모두 남습니다.

## 7. Reset과 Revert를 구분

운영 `main`에 이미 Push된 버전은 **Revert 우선**입니다.

`Reset to commit`은 브랜치의 기준점을 과거 Commit으로 옮기는 기능이라 초보 운영 단계에서는 공유/운영 브랜치의 복구 방법으로 사용하지 않는 것을 권장합니다.

## 8. Tag는 언제 붙이나

Commit하자마자 무조건 Tag를 붙이지 않습니다.

권장 순서:

`Commit → Push → Pages 실제 확인 → 문제 없음 → Tag → Release`

따라서 Tag는 "이 Commit은 안정 배포본"이라는 의미가 됩니다.

## 9. Release에 Offline ZIP 보관

Offline 버전은 GitHub Repository 본문에 계속 누적하지 말고 Release asset으로 올리는 방식이 깔끔합니다.

예:

- Release v4.4.10 → `VisionQC_Offline_v4.4.10.zip`
- Release v4.4.11 → `VisionQC_Offline_v4.4.11.zip`
- Release v4.4.12 → `VisionQC_Offline_v4.4.12.zip`

그러면 온라인 웹은 항상 `main`의 최신 안정 상태를 사용하고, 과거 Offline 패키지는 Release에서 버전별로 다시 받을 수 있습니다.

## 10. 처음에는 main 하나만 권장

GitHub가 익숙하지 않은 초기에는 `main` 하나만 운영하는 것이 실수 가능성이 낮습니다.

작업 원칙은 다음과 같습니다.

`로컬에서 테스트 → main Commit/Push → 웹 확인 → Tag/Release`

GitHub 사용에 익숙해진 뒤 필요하면 `develop` 브랜치를 추가해 새 기능을 별도로 시험할 수 있습니다.

## 11. 중요한 금지사항

- Repository 폴더의 숨김 `.git` 폴더 삭제 금지
- GitHub Desktop을 무시하고 Repository 폴더 자체를 새 폴더로 갈아끼우지 않기
- 검사 이미지/NG 이미지/생산 CSV를 GitHub에 Commit하지 않기
- 이미 Push한 운영 기록을 이유 없이 Reset/Force Push하지 않기
- 버전 Tag를 동일 이름으로 재사용하지 않기

## 12. 추천 Commit message

기능 변경:

`Add Tool score analysis`

버그 수정:

`Fix image view position persistence`

배포 단위:

`Release VisionQC v4.4.11`

원복:

GitHub Desktop이 생성하는 Revert Commit 내용을 유지해도 됩니다.
