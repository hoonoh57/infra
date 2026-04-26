# TODO — 진성대장주 초입 Top3 전략 구현 계획

## 목표

현재 구현된 `StrategyManager / StrategyEngine / FastChartControl / TradeManager` 흐름을 그대로 활용하여, 신규 시스템을 따로 만들지 않고 **사용자 등록 전략**으로 `진성대장주 초입 Top3 전략`을 추가한다.

최종 목표는 다음과 같다.

```text
조건검색 약 100종목
→ 좌측 Top10 선별/검증 패널에서 진짜 대장주 후보 압축
→ 우측 실시간 차트에서 선택 전략 적용
→ 백테스트/모의매매/실거래 동일 전략으로 실행
→ 신호·주문·체결·손익을 차트와 수익분석 화면에 기록
→ 장 종료 후 선별성능과 매매성능을 저장하여 로직 개선 데이터로 활용
```

핵심 원칙:

```text
많이 오른 종목을 사지 않는다.
강하기만 한 종목도 사지 않는다.
하루에 진정한 대장주 1~3종목만, 가장 안전한 대세상승 초입 또는 조정 후 재반등 초입에서 매수한다.
```

---

## 패널 역할 정의

### 1. 좌측 패널: Top10 선별/검증 패널

좌측 패널은 **무엇을 살 것인가**를 결정하고 검증한다.

역할:

- 키움 조건검색으로 들어온 약 100종목 수신
- `LeaderScore`, `TrendStartScore`, `EntrySafetyScore`, `TradePriorityScore` 계산
- `TradePriorityScore` 기준 Top10 선별
- Top10 진입 시점의 순위, 점수, 가격, 시각 저장
- 포착가 대비 최고수익률 실시간 추적
- 점수/순위와 최고수익률의 상관관계 계산
- 장 종료 후 CSV/DB 저장

검증 질문:

```text
Top10으로 선별된 종목이 실제로 당일 최고수익률 상위 종목과 상관관계가 높은가?
```

### 2. 우측 패널: 실시간 차트 전략 실행 패널

우측 패널은 **언제 사고 언제 팔 것인가**를 실행하고 검증한다.

역할:

- 좌측 Top10/Top3 후보 중 선택 또는 자동 추적
- 사용자가 선택한 전략을 차트에 등록
- 실시간 캔들/틱 갱신 시 전략 평가
- 매수/매도/보유/차단 신호를 차트에 표시
- 검증만/모의매매/실거래 모드에 따라 주문 연결
- 체결 결과와 손익을 차트에 표시
- 별도 수익분석 화면으로 결과 전송

검증 질문:

```text
선별된 대장주에서 실제 매수/매도 신호가 수익 가능한 지점에 발생했는가?
```

---

## 전략 이름

```text
내부명: TrueLeaderEarlyTrendTop3
표시명: 진성대장주 초입 Top3 전략
```

---

## 핵심 점수 체계

### LeaderScore

이 종목이 오늘 진짜 대장주인가?

구성 요소:

- TickIntensity 파생강도
- 거래대금 증가/가속
- 조건검색 포착 여부 및 반복성
- OBV > Signal
- JMA 상승/재상승
- SuperTrend 상승
- 섹터/테마 상대강도, 향후 확장

### TrendStartScore

지금 대세상승 초입인가?

구성 요소:

- JMA 상승전환 또는 재상승전환
- SuperTrend 상승 상태 또는 상승전환
- OBV > Signal
- MACD Histogram 개선
- TickIntensity 재확대
- 눌림 후 기준선 회복

### EntrySafetyScore

지금 사도 손익비가 맞는 위치인가?

구성 요소:

- VI 위험권 회피
- 목표수익률 5% 달성 공간 존재
- 갭상승 직후 추격 회피
- 당일고점 추격 회피
- 조정 후 재반등 확인
- 손절/방어매도 가능 구간

### TradePriorityScore

실제 매수 우선순위.

```text
TradePriorityScore = LeaderScore * TrendStartScore * EntrySafetyScore
```

단, `EntrySafetyScore = 0`이면 아무리 강한 종목이라도 매수 금지.

---

## 매수 원칙

매수 후보는 아래를 모두 통과해야 한다.

```text
1. 조건검색 편입 종목
2. LeaderScore 충분
3. TrendStartScore 충분
4. EntrySafetyScore 충분
5. SuperTrend 상승
6. JMA 상승전환 또는 재상승전환
7. OBV > Signal
8. TickIntensity 파생강도 우수
9. VI/갭상승/고점추격 위험 아님
10. Top3 또는 Top10 내 상위권
```

### 위험구간 원칙

다음 종목은 강해도 즉시 매수하지 않는다.

```text
1. 목표수익 5% 달성 전에 VI 위험권에 닿는 구조
2. 갭상승 직후 조정 없음
3. 당일고점 근접 추격
4. RawTickPower는 강하지만 EntrySafetyScore가 낮음
5. ST/JMA/OBV 정렬 부족
```

위험권 종목은 제외가 아니라 다음 상태로 보낸다.

```text
OverheatedLeader
→ PullbackWatching
→ ReboundSetup
→ BuyReady
```

즉, 강한 종목은 버리지 않고 조정 후 재반등 기회를 본다.

---

## 매도 원칙

```text
1. 목표수익률 5% 이상 + JMA 하락전환 → 이익확정 매도
2. 목표수익률 미달 + SuperTrend 상승 유지 → 매도 자제
3. SuperTrend 하락전환 → 방어매도
4. VI 접근 + 수익 상태 → 위험회피 매도
```

핵심:

```text
Profit < Target And ST Bullish → Hold
Profit >= Target And JMA TurnDown → Sell
ST TurnDown → Sell
```

---

## 구현 단계

## P0. 현재 브랜치 안정화

- [x] Interop DLL 경로 상대경로화
- [x] 서버 READY 핸드셰이크 추가
- [x] TickTime 기반 `RealtimeCandleBuilder` 추가
- [x] `StockInfoManager.UpdateCandleCache()`를 TickTime 기반 빌더로 연결
- [x] MainApp이 StrategyLabApp 소스를 링크 컴파일하지 않도록 1차 분리

완료 기준:

- `infra-hardening-p0` 브랜치에서 변경 파일 확인
- 로컬에서 `git fetch`, `git checkout infra-hardening-p0`, `dotnet build Infra.slnx -c Debug` 실행

---

## P1. 전략 모델/신호 구조 정리

- [ ] `TradeExecutionMode` 추가
  - `ValidateOnly`
  - `PaperTrade`
  - `LiveTrade`
- [ ] `StrategySignal` 확장 검토
  - `SignalType`
  - `Reason`
  - `Confidence`
  - `StrategyName`
  - 필요 시 `SignalId`, `TradePriorityScore`, `LeaderState` 추가
- [ ] 차트 전략 실행 상태 모델 추가
  - 적용 전략명
  - 실행 모드
  - 보유 여부
  - 진입가
  - 목표수익률
  - 마지막 신호

완료 기준:

- 기존 `FastChartControl.SetStrategySignals()`와 호환 유지
- 기존 전략 신호 표시가 깨지지 않아야 함

---

## P2. `TrueLeaderEarlyTrendStrategy` 구현

추가 파일:

```text
MainApp/Strategies/TrueLeaderEarlyTrendStrategy.vb
```

작업:

- [ ] `IStrategy` 구현
- [ ] `Name = TrueLeaderEarlyTrendTop3`
- [ ] `DisplayName = 진성대장주 초입 Top3 전략`
- [ ] `RequiredIndicators()` 정의
  - `SuperTrend`
  - `JMA`
  - `OBV`
  - `OBVSignal`
  - `TickIntensity`
  - `TradeStrength`
  - `Volume/Turnover`
- [ ] `Evaluate()`에서 전체 캔들 기준 신호 생성
- [ ] 마지막 캔들 기준 실시간 신호 생성
- [ ] 매수 조건 구현
- [ ] 매도 조건 구현
- [ ] 차단 조건도 `Reason`에 기록

기본 파라미터:

```vb
TargetProfitPct = 5.0
MaxBuyCount = 3
WatchCount = 10
MinLeaderScore = 70.0
MinTrendStartScore = 70.0
MinEntrySafetyScore = 70.0
MaxOpenRiseForNewBuy = 5.0
GapCooldownThreshold = 3.0
PullbackConfirmPct = 1.5
ReboundConfirmPct = 0.5
```

완료 기준:

- `FastChartControl.AddStrategy(New TrueLeaderEarlyTrendStrategy())`로 등록 가능
- 차트에서 Buy/Sell/None 신호가 생성됨
- `StrategySignal.Reason`에 매수/매도/차단 사유가 기록됨

---

## P3. 전략관리자/차트 메뉴에 등록

작업:

- [ ] 차트 우클릭 메뉴의 `전략 관리` 경로 확인
- [ ] `전략 적용 및 분석 시작` 메뉴에서 `진성대장주 초입 Top3 전략` 선택 가능하게 추가
- [ ] 선택 시 현재 차트에 전략 등록
- [ ] 전략 중복 등록 방지
- [ ] 전략 제거/교체 기능 확인

완료 기준:

- 우측 차트에서 메뉴로 전략 선택 가능
- 선택 후 실시간 캔들 갱신 시 신호 평가 실행
- 차트에 신호 마커 표시

---

## P4. 좌측 Top10 선별/검증 패널 재편성

작업:

- [ ] `StockInfoForm` 컬럼 재정의
- [ ] `TradePriorityScore` 기준 Top10 정렬
- [ ] 기존 등락률 중심 컬럼 후순위 또는 숨김 처리
- [ ] 신규 컬럼 추가

추천 컬럼:

```text
순위
종목코드
종목명
현재가
LeaderState
LeaderScore
TrendStartScore
EntrySafetyScore
TradePriorityScore
RawTickPower
Top10진입시각
Top10진입가격
현재수익률
최고수익률
수익률순위
점수순위-수익률순위차
BlockReason
```

완료 기준:

- Top10 버튼이 등락률이 아닌 `TradePriorityScore` 기준으로 작동
- 포착가 대비 최고수익률이 실시간 갱신됨
- 좌측 패널만 봐도 선별 점수와 결과 수익률의 관계가 보임

---

## P5. 선별 검증 데이터 저장

작업:

- [ ] `LeaderValidationService` 완성
- [ ] Top10 진입시각/가격 저장
- [ ] 최고수익률 업데이트
- [ ] 점수-최고수익률 상관계수 계산
- [ ] 순위-최고수익률 상관계수 계산
- [ ] CSV 저장
- [ ] 추후 MySQL 저장 확장 가능 구조 유지

저장 항목:

```text
일자
시각
종목코드
종목명
Top10순위
LeaderScore
TrendStartScore
EntrySafetyScore
TradePriorityScore
포착가
현재가
최고가
최고수익률
수익률순위
BlockReason
```

완료 기준:

- `%LOCALAPPDATA%/Infra/Validation/leader_top10_YYYYMMDD.csv` 저장
- 장 종료 후 좌측 선별 성능 재검토 가능

---

## P6. 우측 차트 전략 런타임 연결

작업:

- [ ] 현재 `FastChartControl.UpdateTick()` → `EvaluateStrategies()` 흐름 검토
- [ ] 전략 신호가 발생하면 차트에 즉시 표시
- [ ] 신호 발생 시 `MessageBus`로 표준 이벤트 발행
- [ ] 실행 모드에 따라 주문 연결

실행 모드:

```text
ValidateOnly: 신호와 마커만 표시
PaperTrade: 가상 체결/손익 계산
LiveTrade: TradeManager로 실제 주문 요청
```

완료 기준:

- 차트가 passive viewer가 아니라 능동형 전략 실행 패널로 작동
- 동일 전략이 백테스트/모의/실거래에 사용됨

---

## P7. 모의매매/실거래 연결

작업:

- [ ] `StrategySignal Buy/Sell` → 주문 의도 변환
- [ ] `ValidateOnly`에서는 주문 없음
- [ ] `PaperTrade`에서는 가상 포지션 생성
- [ ] `LiveTrade`에서는 `TradeManager`로 주문 요청
- [ ] 주문/체결 결과를 차트와 하단 매매 모니터에 표시

완료 기준:

- 같은 신호로 검증/모의/실거래 모드 전환 가능
- 주문 발생 사유가 전략 신호 사유와 연결됨

---

## P8. 백테스트 연결

작업:

- [ ] `TrueLeaderEarlyTrendStrategy`를 과거 캔들에 적용
- [ ] 진입/청산 신호 생성
- [ ] 포착가 대비 최고수익률과 실제 매매수익률 분리 기록
- [ ] Top10 선별 성능과 매매 성능을 같이 출력

완료 기준:

- 한 종목 차트 기준 백테스트 가능
- Top10 후보 일괄 백테스트로 확장 가능
- 매수/매도 마커가 차트에 표시됨

---

## P9. 수익분석 화면 구현/연결

작업:

- [ ] 선별 성능 탭
- [ ] 매매 성능 탭
- [ ] 종목별 상세 탭
- [ ] 신호별 상세 탭

선별 성능:

```text
Top1 최고수익률
Top3 평균 최고수익률
Top10 평균 최고수익률
점수-최고수익률 상관계수
순위-최고수익률 상관계수
실제 최고수익률 종목이 Top10 안에 있었는지
```

매매 성능:

```text
총 거래수
승률
평균수익률
최대수익률
최대손실률
PF
MDD
평균보유시간
매수 후 최고수익률
매도 후 추가상승률
```

완료 기준:

- 선별은 좋았는데 매수 타이밍이 나빴는지 구분 가능
- 선별과 매매가 동시에 좋은지 확인 가능

---

## P10. 장 종료 후 개선 루프

작업:

- [ ] 당일 CSV/DB 로드
- [ ] Top10 점수와 최고수익률 상관도 분석
- [ ] 매매 수익률과 신호 발생 위치 분석
- [ ] 실패 종목의 BlockReason/SignalReason 분석
- [ ] 다음날 파라미터 조정 후보 생성

개선 대상:

```text
MinLeaderScore
MinTrendStartScore
MinEntrySafetyScore
MaxOpenRiseForNewBuy
GapCooldownThreshold
PullbackConfirmPct
ReboundConfirmPct
TickIntensity 가중치
OBV/JMA/ST 가중치
```

완료 기준:

- 장 종료 후 “왜 이 종목은 잡았고, 왜 이 종목은 놓쳤는지”가 데이터로 보임
- 다음날 조정해야 할 파라미터 후보가 도출됨

---

## 구현 우선순위

1. `TrueLeaderEarlyTrendStrategy` 추가
2. 차트 메뉴에서 전략 등록 가능하게 연결
3. 차트에서 신호 발생/표시 확인
4. 좌측 Top10 컬럼을 `TradePriorityScore` 중심으로 재편성
5. Top10 포착가 대비 최고수익률 추적
6. 선별 상관도 계산/저장
7. 모의매매 연결
8. 수익분석 화면 연결
9. 실거래 모드 연결

---

## 현재 가장 먼저 할 작업

```text
P2. MainApp/Strategies/TrueLeaderEarlyTrendStrategy.vb 추가
```

이 전략을 먼저 `IStrategy`로 구현한 뒤, 기존 차트 전략 등록 구조에 연결한다.
