VisionQC Web v4.4.7 Profile Safe

기준 버전: v4.4.4 Offline Clean

변경 사항
1. React 헤더 내부 텍스트를 확장 JS가 직접 수정하지 않음
2. 브랜드 제목/버전은 CSS 가상 요소로 고정 표시
3. Chrome 프로필마다 LocalStorage/IndexedDB 상태가 달라도 저장 Input 복원 완료를 기다리지 않고 즉시 화면 진입
4. 저장 파일 및 NG 폴더 복원은 백그라운드에서 실행
5. IndexedDB blocked 상태를 오류로 처리하여 무한 대기 방지
6. 외부 CDN/CSS/폰트 참조 없음

Chrome 프로필마다 데이터가 다르게 보이는 이유
- LocalStorage와 IndexedDB는 Chrome 프로필별로 분리됨
- 저장된 결과 CSV 파일 핸들, NG 폴더 핸들, 마지막 페이지가 프로필마다 다름
- 동일 주소 127.0.0.1:3021이라도 Chrome 계정/프로필이 다르면 서로 공유되지 않음
