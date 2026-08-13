# VisionQC DirectExport v4.1

## 핵심 기능

### Load Folder
- Chrome/Edge의 폴더 권한 방식으로 원본 폴더를 불러옵니다.
- 같은 실행 세션에서는 Organize Folder가 이 원본 폴더를 자동으로 재사용합니다.

### Save Labels
- 현재 이미지별 라벨 상태를 JSON으로 저장합니다.
- File_Ogarnizer 호환을 위해 `status`, `statusId`, `statusLabel`은 화면에 보이는 라벨명으로 저장합니다.
- 내부 복원용 ID는 `classId`, `internalStatusId`에 보존합니다.

### Load Labels
- Save Labels로 저장한 JSON 또는 기존 VisionQC JSON을 불러와 현재 이미지 목록에 라벨을 적용합니다.
- 파일 path 또는 fileName 기준으로 매칭합니다.

### Organize Folder
- 현재 라벨 기준으로 기존 원본 폴더 안에 Classification별 폴더를 생성합니다.
- 버튼 선택: 복사 / 이동 / 취소
- 복사: 원본 유지
- 이동: 분류 폴더로 복사 후 원본 위치 파일 삭제
- 별도 `_VisionQC_` 로그 파일은 생성하지 않습니다.

### Export Folder
- 별도 출력 폴더를 선택해 라벨별 폴더로 복사합니다.
- 별도 `_VisionQC_` 로그 파일은 생성하지 않습니다.

## 대량 이미지 안정화
- 모든 이미지의 preview URL을 한 번에 만들지 않고, 현재 화면에 필요한 메인 이미지/썸네일만 생성합니다.
- 썸네일은 오른쪽 File Explorer에서 보이는 항목만 생성합니다.
- 이미지 로드 실패 시 preview URL을 재생성하여 재시도합니다.
