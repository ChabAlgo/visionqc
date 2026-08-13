# VisionQC Web v4.4.10 — GitHub Pages 배포본

이 폴더의 **내용 전체를 GitHub 저장소 최상위(root)** 에 올리면 됩니다.

## GitHub Pages 설정

GitHub 저장소에서:

`Settings → Pages → Build and deployment → Source: Deploy from a branch`

설정값:

- Branch: `main`
- Folder: `/(root)`

저장 후 배포 URL은 일반적으로 다음 형태입니다.

`https://<GitHub아이디>.github.io/<저장소이름>/`

## 주의

- `index.html`은 저장소 최상위에 있어야 합니다.
- `assets` 폴더 구조를 변경하지 마십시오.
- `.nojekyll` 파일을 삭제하지 않는 것을 권장합니다.
- 이미지/CSV 분석 데이터는 GitHub로 업로드하지 않고 사용자의 Chrome/로컬 PC에서 처리하는 현재 구조를 유지합니다.
- AI/Gemini 관련 기능을 실제로 사용할 경우에는 인터넷 연결이 필요할 수 있습니다. 일반 분류/CSV/NG 분석 기능의 정적 리소스는 로컬 번들입니다.

자세한 버전 관리 방법은 `GITHUB_SETUP_AND_VERSION_GUIDE_KR.md`를 확인하십시오.
