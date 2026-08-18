# VisionQC Local Agent API 초안

기본 주소: `http://127.0.0.1:17891`

## 1. 상태 확인
`GET /api/status`

예상 응답:
```json
{
  "agentVersion": "0.1.0",
  "vpdlVersion": "4.0",
  "license": "OK",
  "gpu": "NVIDIA GPU 0"
}
```

## 2. 향후 추가 예정
- Windows Workspace 파일 선택 창
- 이미지 입력 폴더 선택 창
- Output 폴더 선택 창
- Simulation Start / Stop
- 진행률 및 현재 이미지 결과 실시간 전달
- 완료 결과를 VisionQC 분석 화면으로 즉시 연결

브라우저에서는 실제 절대 경로를 Local Agent에 직접 전달하기 어렵기 때문에, Workspace/폴더 선택 창은 Local Agent가 Windows Native Dialog를 띄우는 방식이 적합합니다.
