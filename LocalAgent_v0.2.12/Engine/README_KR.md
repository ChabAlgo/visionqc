# Engine

Cognex VisionPro Deep Learning 전용 검사 파이프라인이다.

- `GreenOverlayProcessor`: Green Tool 검사, 결과 CSV, 실시간 `LiveAnalysisRecord`, NG Heatmap 합성 JPEG 생성.
- `BlueCropCore`: Locate Blue Tool 기반 Crop 및 Integrated 파이프라인 보조.
- `Models.cs`: Engine 내부 설정/결과 모델.

Green Heatmap은 **Green 단독 검사 NG**에서만 원본 복제본에 합성해 저장한다. Runtime ROI를 읽을 수 없으면 잘못된 위치에 그리지 않고 진단 파일을 남긴다. 이 폴더에는 HTTP 응답, 브라우저 상태, SQLite SQL을 넣지 않는다.
