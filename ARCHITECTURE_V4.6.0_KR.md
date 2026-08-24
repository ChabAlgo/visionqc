# VisionQC v4.6.0 구조 기준

## 실행 경계

`index.html`과 `visionqc-extension.js`는 GitHub Pages 또는 Agent의 `Web` 폴더에서 실행되는 GUI다. 브라우저는 CSV 분석·화면 렌더링·사용자 설정만 담당한다. 로컬 파일 선택, VPDL Runtime, 이미지 바이트 읽기, SQLite 쓰기는 `LocalAgent_v0.2.12`의 Loopback API만 담당한다.

```text
Web GUI
  ├─ CSV/XLSX 파싱과 집계(브라우저 메모리)
  ├─ /api/image/preview ──────┐
  ├─ /api/history/import ─────┼─ Local Agent
  └─ /api/simulation/* ───────┘     ├─ VPDL Engine
                                      ├─ 이미지 Picker/Preview Service
                                      └─ SQLite Run Store
```

## 데이터 수명

| 데이터 | 위치 | 수명 | 비고 |
|---|---|---|---|
| CSV 분석 행 | 브라우저 메모리 | 페이지 새로고침 전 | 100만 행을 장기 보관하지 않는다. 필요할 때 SQLite로 명시 저장한다. |
| 원본 이미지 | 기존 현장 경로 | 외부 소유 | Agent는 한 장의 축소 미리보기만 읽으며 복사하지 않는다. |
| Simulation 실시간 행 | Agent 배치/SSE + 브라우저 | 실행 중 | `printEvery` 단위로 전송한다. |
| 검사 이력 | SQLite | 영구 | 결과·경로·점수만 저장한다. |
| Heatmap Overlay | 사용자가 지정한 Output | 영구 | Green 단독 NG만 도구별 JPEG를 생성한다. |

## Local Agent 분리 기준

- `AgentServer.cs`: HTTP/SSE 조율, Runtime 수명, API 라우팅만 맡는다. 새 비즈니스 규칙을 이 파일에 직접 추가하지 말고 아래 폴더에 먼저 둔다.
- `Domain`: 외부 형식과 독립적인 파일명 규칙 모델.
- `Services`: Picker, 이름 파싱, Position 결정, 이미지 미리보기처럼 단일 책임 서비스.
- `Persistence`: SQLite 스키마와 실행 단위 기록. 원본 이미지/대규모 파일 목록을 저장하지 않는다.
- `Engine`: Cognex VPDL 전용 처리. Web/HTTP/SQLite 타입을 직접 참조하지 않는다.
- `OfflineInstaller`: Agent와 오프라인 Web 자산을 단일 EXE에 넣는 배포 전용 프로젝트.

## 성능 원칙

1. 대량 이미지 목록 전체를 브라우저나 SQLite에 선행 적재하지 않는다.
2. SQLite는 WAL 모드와 200행 트랜잭션 배치로 쓰기 부하를 낮춘다.
3. 이미지 조회는 요청한 한 장만 최대 4096px(웹 사용은 2560px)로 축소한다.
4. VPDL Runtime은 Runtime File Load 후 재사용하며, Simulation과 단일 검사는 동시에 Runtime을 점유하지 않는다.
5. 추후 100만 장 규모의 전체 이력 조회는 SQLite 인덱스 기반 페이지네이션 API를 추가한 뒤 GUI에 연결한다.
