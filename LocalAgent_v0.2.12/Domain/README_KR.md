# Domain

파일명 규칙이라는 순수 데이터 모델을 둔다. `NamingProfile.cs`의 `NamingProfile`, `NamingFieldRule`, 미리보기 DTO는 Web 설정과 Agent 파서가 공통으로 사용한다.

- VPDL, HTTP, SQLite에 의존하지 않는다.
- 규칙 변경은 이전에 저장한 SQLite `naming_profile_json`을 해석할 수 있도록 호환성을 고려한다.
- 공정별 Cell ID/날짜/시간 위치가 달라도 토큰 위치 또는 자동 후보 길이 규칙으로 표현한다.
