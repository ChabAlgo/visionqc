# VPDL Runtime Workspace에서 확인 가능한 정보

## 결론

Runtime Workspace(`.vrws`)는 원본 학습 Workspace의 축소 배포본입니다. Cognex 공식 설명대로 이미지와 데이터베이스는 포함하지 않고, 분석 실행에 필요한 Stream과 Tool 및 배포용 모델/설정을 포함합니다.

VisionQC v4.4.30의 현재 구조 화면이 실제로 읽어 표시하는 범위는 다음과 같습니다.

- Workspace 파일 경로/이름, 로드 방식, Stream/Tool 개수
- Stream 이름과 순서
- Tool 이름, Tool Type, 부모/자식 Tool 경로
- Green `KnownTags`, Red `KnownClasses`, Blue `KnownFeatures`
- Agent가 실제로 로드한 VPDL 설치 버전, Runtime/License 상태, GPU 이름

현재 화면은 모든 Tool 파라미터를 아직 덤프하지 않습니다. 아래 항목은 VPDL 버전에 맞는 Runtime 인터페이스를 추가로 구현하면 확인 가능한 범위입니다.

## 추가로 읽을 수 있는 정보

| 구분 | 가능한 정보 | 주의점 |
|---|---|---|
| 파일 메타데이터 | 파일 크기, 수정 시각, SHA-256, 파일명/경로 | VPDL API가 아니라 Windows 파일 정보입니다. |
| 구조 | Workspace, Stream, Tool 트리, Tool 이름/순서/Type | 버전별 enum 및 interface 이름이 달라질 수 있습니다. |
| Tool 분류 | Blue Locate/Read, Green Classify, Red Analyze, Standard/Legacy, Mode, Network Model | Runtime interface가 노출하는 값에 한합니다. |
| 공통 처리 파라미터 | ROI, GPU/처리 모드, TensorRT mode, batch 관련 값, adaptive downsampling 등 | 일부는 학습 전용이거나 Runtime export에서 제외될 수 있습니다. |
| Green | KnownTags, tag별 threshold, probability threshold, heatmap/outlier 사용 가능 여부 | VPDL 4.1부터 tag별 `TagThresholds` 의미가 중요합니다. |
| Red | KnownClasses, class/region threshold와 color, Standard/Legacy/Unsupervised 구분 | VPDL 4.0부터 multiclass `RegionThresholds`가 도입됐습니다. |
| Blue | KnownFeatures, model/feature 이름, Locate/Read 결과 구조, `max_scan_iterations` 등 | 모델의 학습 원본 이미지나 라벨 DB는 포함되지 않습니다. |
| 실행 결과 | Green score/tag, Red class/region/mask score, Blue feature/pose/geometry, 처리 시간 | 이미지 한 장을 실제 Process한 뒤 생성되는 정보입니다. |

## Runtime Workspace만으로 확인할 수 없는 정보

- 원본 학습 이미지와 이미지 폴더
- Training/Validation/Test set의 실제 이미지 목록
- 원본 라벨 데이터베이스와 전체 학습 이력
- epoch별 loss graph, 학습 시간, 모든 Parameter Search 조합
- export 시 포함하지 않은 HeatMap/Outlier 데이터
- License key 자체
- 파일 내부에 명시적인 exporter version 메타데이터가 없을 때의 정확한 생성 VPDL 버전

Agent가 보여주는 `VPDL 4.x`는 현재 파일의 생성 버전이 아니라, 그 파일을 열기 위해 실제 로드한 설치 DLL 버전입니다. 정확한 파일 생성 버전이 필요하면 export 시 `workspace.vrws`와 함께 `workspace.manifest.json`에 VPDL 버전, export 시각, Tool 목록, SHA-256을 저장하는 방식이 가장 확실합니다.

## 버전별 핵심 차이

| 버전 | Runtime/API에서 중요한 차이 |
|---|---|
| 4.0 | Tool 분류가 Architecture 중심에서 Standard/Legacy + Mode로 개편되었습니다. Red multiclass `RegionThresholds`/색상, Green HeatMap/Outlier runtime export option, TensorRT `none/basic/int8` mode가 도입됐습니다. 4.0 이전 High Detail Runtime은 재학습이 필요할 수 있습니다. |
| 4.1 | Green threshold가 tag별로 확장되어 `TagThresholds`가 추가됐고 Red 결과에 `FilteredScores`가 추가됐습니다. `IWorkspaceList.Add` 및 runtime export schema가 변경됐으며 `gpu_hdm`은 `gpu_standard`, `visual_debugger_on`은 `heat_map_on`으로 변경됐습니다. |
| 4.2 | 공개 API changelog에는 별도 4.2 API 장이 없고 4.1/4.0 변경사항이 유지됩니다. Release History에는 대형 Runtime Workspace export 실패, Runtime API license, GPU memory 등 안정성 문제가 수정된 것으로 나옵니다. VisionQC Agent v0.2.8은 4.2 설치 경로를 탐지합니다. |

버전이 다르면 단순히 DLL 하나만 섞어 쓰면 안 됩니다. Cognex managed DLL, native runtime, export schema, license runtime을 같은 설치 계열로 맞추고 해당 버전에서 Workspace를 실제 로드해 검증해야 합니다.

## VPDL이 로컬 PC에 없을 때

### 1. 다른 PC에서 Agent만 빌드해 복사

컴파일은 가능합니다. Cognex SDK 참조 DLL이 있는 빌드 PC에서 EXE를 만들 수 있습니다. 하지만 현재 Local Agent는 `ViDi.NET.Local`과 native runtime을 실행 시 사용하므로, EXE만 복사해도 로컬 Workspace 검사/Simulation은 동작하지 않습니다. Runtime 종속성과 License가 여전히 필요합니다.

### 2. VPDL Remote Service 사용

가장 현실적인 무설치 구조입니다.

- VPDL 설치, GPU, Runtime License는 별도 실행 서버에 둡니다.
- 사용자 PC에는 Cognex Remote Client용 .NET 패키지와 VisionQC Agent만 둡니다.
- Agent를 `ViDi2.Runtime.Remote.Client.Control` 기반 adapter로 확장해 서버 URI에 연결합니다.
- 이미지/Workspace 전송, 인증, TLS, 연결 상태, 결과 저장 위치를 별도로 설계해야 합니다.

Cognex 4.2 공식 문서는 Remote Control이 service URI에 연결되어 Local Control처럼 사용될 수 있다고 설명합니다. 네트워크 License 모델에서는 서버에 Security Key를 두고 client에는 키가 없는 구성도 공식적으로 안내합니다.

### 3. VPDL 없는 경량 Agent Host

파일/폴더 선택, CSV 처리, 로그, UI 연동은 VPDL 없는 Host로 분리할 수 있습니다. VPDL 기능만 선택형 plugin으로 로드하면 Agent 자체는 실행할 수 있지만, plugin 또는 Remote Service가 없으면 Workspace 구조 읽기와 Simulation은 비활성 상태가 됩니다.

### 4. `.vrws` 파일을 직접 해석

권장하지 않습니다. 공개된 안정적 파일 포맷 parser가 아니며 버전별 schema와 모델 데이터가 바뀔 수 있어 구조가 일부 보이더라도 정확성과 호환성을 보장할 수 없습니다.

## 공식 자료

- [Cognex 4.2 Runtime API Processing Guide](https://docs.cognex.com/deep-learning_420/web/EN/deep-learning/Content/Topics/developer/runtime-api-processing-guide.htm)
- [Cognex 4.2 Runtime Client C# API](https://docs.cognex.com/deep-learning_420/web/EN/deep-learning/Content/Topics/developer/runtime-client-api.htm)
- [Cognex 4.2 Server Setup](https://docs.cognex.com/deep-learning_420/web/EN/deep-learning/Content/Topics/developer/server-setup.htm)
- [Cognex 4.2 C/.NET Integration](https://docs.cognex.com/deep-learning_420/web/EN/deep-learning/Content/Topics/developer/c-net-integration.htm)
- [Cognex 4.1 API Changelog](https://docs.cognex.com/deep-learning_410/web/EN/deep-learning/Content/Topics/developer/api-changelog.htm)
- [Cognex 4.2 Release History](https://docs.cognex.com/deep-learning_420/web/EN/deep-learning/Content/Topics/release-notes/release-history.htm)
