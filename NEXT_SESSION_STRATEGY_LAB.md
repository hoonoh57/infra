# StrategyLab Next Session Handover

## 목적
- 다음 세션에서 현재 진행 상태를 100% 이어가기 위한 인수인계 문서다.
- 현재 전략 연구용 분리 프로젝트의 구현 범위, 검증 상태, 남은 작업, 주의사항을 기록한다.

## 현재 방향
- `MainApp`은 운영용으로 유지한다.
- 전략 연구/평가/개선/후보 관리/승격 후보 고정은 별도 프로젝트 `StrategyLabApp`에서 수행한다.
- 공통 모델과 평가 코어는 `StrategyCore`로 분리한다.
- 운영 앱과 연구 앱은 직접 UI를 공유하지 않는다.
- 운영 앱에서는 별도 메뉴로 `StrategyLabApp.exe`를 테스트 실행만 할 수 있게 한다.

## 현재 생성된 프로젝트
- `StrategyCore/StrategyCore.vbproj`
- `StrategyLabApp/StrategyLabApp.vbproj`
- `Infra.slnx` 에 두 프로젝트가 추가된 상태

## StrategyCore 구현 상태

### 주요 파일
- `StrategyCore/Models/StrategyModels.vb`
- `StrategyCore/Services/StrategyPromptCompiler.vb`
- `StrategyCore/Services/BaselineEvaluationService.vb`
- `StrategyCore/Services/StrategyDiagnosisService.vb`
- `StrategyCore/Services/StrategyImprovementSuggestionService.vb`
- `StrategyCore/Services/StrategyPackageServices.vb`

### 현재 지원 기능
1. 프롬프트 -> 전략 초안/전략 정의 변환
2. baseline 평가
3. KPI 계산
4. diagnosis 생성
5. improvement suggestion 생성
6. strategy package 생성/검증
7. promotion manifest 생성

### 현재 프롬프트 컴파일 특성
- 타임프레임 토큰 감지:
  - intraday: `m1`, `m3`, `m5`, `T30`, `T60`, `T120`
  - swing: `m15`, `m30`, `m60`
- 지표 키워드 감지:
  - `volume`
  - `volume20`
  - `volume20 slope`
  - `macd`
  - `rsi`
  - `jma`
  - `supertrend`
- 키워드가 없으면 기본적으로 `MACD + SuperTrend` fallback이 들어간다.

## StrategyLabApp 구현 상태

### 주요 파일
- `StrategyLabApp/Program.vb`
- `StrategyLabApp/StrategyLabForm.vb`
- `StrategyLabApp/StrategyLabSessionModels.vb`
- `StrategyLabApp/StrategyLabSessionService.vb`

### 현재 화면 구성
- 좌측:
  - 세션 상태
  - symbol / from / mode / target
  - history
  - prompt
  - 버튼들
  - recommendation label
  - recommendation reason
  - promotion candidate label
  - candidate list
- 우측 상단:
  - 실험용 차트
- 우측 하단:
  - KPI grid
  - baseline comparison grid
  - candidate ranking grid
  - diagnosis grid
  - suggestions grid
  - trades grid

### 현재 동작 흐름
1. prompt 입력
2. Evaluate Prompt
3. baseline 자동 초기화
4. diagnosis / improvement suggestions 생성
5. suggestion 더블클릭 또는 `Apply Top Suggestion`
6. prompt 보강 후 재평가
7. candidate 저장
8. candidate ranking 계산
9. recommended candidate 계산
10. recommendation reason 표시
11. promotion candidate pin
12. pinned candidate만 package export 가능

### 후보 체인 관리
- candidate는 `StrategyLabCandidateRecord` 단위로 저장한다.
- 포함 필드:
  - `CandidateId`
  - `ParentCandidateId`
  - `VersionTag`
  - `SourcePrompt`
  - `SavedAt`
  - `Result`
- 표시는 `C1 <- baseline`, `C2 <- <candidateId>` 식으로 연결된다.

### recommendation / promotion 상태
- recommendation:
  - 자동 산출
  - 현재 기준: `Primary KPI desc -> Secondary KPI desc -> Drawdown desc`
- promotion candidate:
  - 수동 pin
  - 현재 선택 candidate가 있으면 그것을 pin
  - 없으면 recommended candidate를 pin
- package export:
  - 반드시 pinned promotion candidate 기준

## MainApp 변경 상태

### 현재 변경 파일
- `MainApp/Shell/MainShell.vb`

### 현재 추가된 내용
- 운영 앱에서 `StrategyLabApp`을 별도 프로세스로 여는 테스트 진입점 추가
- 방식:
  - `mnuTradeTest` 아래에 동적으로 `StrategyLab Test...` 메뉴 추가
  - `StrategyLabApp.exe` 경로를 후보 목록에서 탐색 후 실행

### 중요한 점
- `MainApp`은 `StrategyLabApp` 프로젝트를 직접 참조하지 않는다.
- 실행 파일만 찾고 `Process.Start`로 띄운다.

## 검증 상태

### 새 프로젝트 검증
반복적으로 확인 완료:
- `dotnet build StrategyCore/StrategyCore.vbproj`
- `dotnet build StrategyLabApp/StrategyLabApp.vbproj`
- `StrategyLabApp.exe --smoke-test`

### smoke test 에서 확인한 것
- prompt evaluation
- improvement suggestion 생성
- suggestion 적용
- candidate 저장
- session save/load
- promotion candidate pin
- package save

### MainApp 검증 한계
- `MainApp/MainApp.vbproj` 빌드는 샌드박스에서 Windows SDK 경로 접근 제한 때문에 실패했다.
- 따라서 `MainShell.vb` 변경은 diff 기반으로만 확인했다.
- 다음 세션 또는 실제 로컬 환경에서는:
  1. `MainApp` 실행 중지
  2. `dotnet build MainApp/MainApp.vbproj`
  3. 메뉴에서 `StrategyLab Test...` 실행 확인
  를 해야 한다.

## 현재 작업 트리 상태
- 수정:
  - `Infra.slnx`
  - `MainApp/Shell/MainShell.vb`
- 신규:
  - `StrategyCore/`
  - `StrategyLabApp/`
- 임시 생성:
  - `.dotnet_cli/`
- 무관 항목:
  - `_obj_test/`

## 다음 세션 시작 직후 해야 할 일
1. 실행 중인 `StrategyLabApp.exe`와 `MainApp.exe`가 있으면 종료
2. 필요 시 `.dotnet_cli/` 정리 여부 판단
3. `dotnet build StrategyLabApp/StrategyLabApp.vbproj`
4. `dotnet build MainApp/MainApp.vbproj`
5. `MainApp`에서 `StrategyLab Test...` 메뉴로 새 앱 실행 확인

## 다음 구현 우선순위

### 1순위
- `MainApp` 테스트 메뉴 실제 실행 검증
- pinned promotion candidate 기반 package export 최종 동작 검증

### 2순위
- 차트 설명력 강화
  - baseline / candidate 진입 마커 분리
  - exit 마커 추가
  - 선택 지표 overlay 최소 1~2개 표시

### 3순위
- 하단 grid 가독성 개선
  - 컬럼 폭 조정
  - 일부 grid 탭화 또는 요약/상세 분리

### 4순위
- recommendation engine 고도화
  - category별 추천 이유 강화
  - prompt template 다양화
  - 상황별 exclusion rule 제안 추가

### 5순위
- promotion workflow 강화
  - pinned candidate만 `Validated` 표기
  - package export 시 promotion metadata 더 명확히 기록
  - import 전용 패키지 목록 관리

## 구현 시 주의사항
1. 한글 포함 기존 파일은 최소 라인 단위로만 수정
2. 수정 직후 `git diff`로 인코딩 손상 여부 확인
3. `MainApp`과 `StrategyLabApp`의 책임 경계를 유지
4. `MainApp`은 연구 UI/로직을 직접 참조하지 않음
5. `StrategyLabApp.exe` 파일이 실행 중이면 빌드가 잠길 수 있음

## 추천 다음 세션 작업 순서
1. `StrategyLabApp.exe` 종료
2. `MainApp` 빌드 확인
3. `StrategyLab Test...` 메뉴 실행 확인
4. package export 경계 최종 확인
5. 차트 오버레이 개선 시작

