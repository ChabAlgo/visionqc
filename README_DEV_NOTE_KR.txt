개발/운영 메모 v4.1

이 버전은 STATIC 실행 방식입니다.
run_app.bat -> node server_static.mjs -> dist 표시 구조입니다.
실행 시 npm install, vite, rollup을 사용하지 않습니다.

브라우저 File System Access API 특성상 Chrome/Edge에서 사용하는 것을 권장합니다.
Load Folder로 폴더를 열면 같은 실행 세션에서는 Organize Folder가 해당 폴더 핸들을 재사용합니다.
