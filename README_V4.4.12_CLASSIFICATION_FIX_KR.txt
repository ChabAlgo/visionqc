VisionQC Web v4.4.12 - Classification 복구

원인
- v4.4.10/v4.4.11 GitHub 배포본의 메인 React 번들(index-A-5itVpl.js)에 직접 수정 과정에서 Cell ID 폴더 선택 UI 코드가 중복 삽입되었습니다.
- 그 결과 메인 번들 자체에 JavaScript SyntaxError가 발생하여 React가 전혀 시작되지 않았습니다.
- 메뉴는 별도 visionqc-extension.js에서 생성되므로 메뉴만 보이고 분류 화면은 빈 화면으로 남았습니다.

수정
1. 문법 검증이 통과하는 v4.4.4 정상 React 번들을 기준으로 재구성
2. 이미지 전환 시 확대율/화면 위치/드래그 위치 유지 패치만 정확히 재적용
3. Input Cell ID > Select Folder를 실제 file input을 포함한 label 방식으로 재적용
4. 기존 분석/메인/설정 로직은 최신 extension JS/CSS 유지
5. 실제 NG 최소 Score는 NG Image와 Cell ID+Position이 매칭된 원본 NG Score만 사용, 없으면 '-' 유지
6. 브라우저 캐시로 깨진 이전 번들이 재사용되지 않도록 메인 번들 파일명을 index-v4.4.12.js로 변경

검증
- node --check 메인 번들 통과
- node --check extension JS 통과
- React root/header/main 렌더링 확인
- 분류 -> 메인 -> 분류 전환 후 React main 복원 확인
- Input Cell ID 모달의 Select Folder file chooser 이벤트 확인
