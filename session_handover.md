# session_handover.md — SimTrade 수익검증 집중 인계서

## 1. 현재 방향

현재 프로젝트는 **수익검증 집중**으로 진행한다. 대형 구조개편은 보류한다.

보류 대상:

- DataHub / MarketDataHub / IndicatorHub / SignalStateHub 전면 도입
- multi-timeframe 중앙 컨텍스트 구조
- Chart V2 구조 변경
- Top10Filter 전면 재작성

현재 우선순위:

1. 정상화된 Chart V2는 잠금
2. 모의매매 데이터 신뢰성 확보
3. TickIntensity / TickSum 계산값과 표시값 일치
4. TopN 선정 종목이 실제 이후 수익률로 이어지는지 검증

핵심 원칙: **신뢰할 수 없는 데이터는 극약이다. 활용은 다음 문제이고, 우선은 정확한 데이터 계산 및 표시가 먼저다.**

---

## 2. 이번 세션에서 구현된 주요 내용

### 2.1 SimTrade 데이터 확인 다이얼로그 추가

추가/수정 파일:

```text
MainApp/SimTrade/SimTradeStockDataDebugForm.vb
MainApp/SimTrade/SimTradeForm.vb
```

모의매매 화면에 `[데이터확인]` 버튼을 추가했다. 감시 그리드에서 종목 선택 후 버튼 클릭 또는 행 더블클릭으로 선택 종목의 내부 데이터를 확인한다.

데이터 확인창 탭:

```text
요약
캔들(1분봉)
틱원본(30틱)
틱→1분봉 매핑
지표
TopN 점수
매매/신호
Raw Dump
```

목적:

- 로그만으로 보기 어려운 Candle / Tick / Indicator / State / TopN 값을 한 번에 검증
- TickSum 오류 원인을 한 종목 기준으로 빠르게 분리

---

### 2.2 TickIntensity 원본 tick timestamp 표시

`TickIntensity_Indicator` 내부에 로드된 원본 tick timestamp를 외부 진단창에서 확인할 수 있게 했다.

수정 파일:

```text
MainApp/ChartEngine/Indicators/TickIntensity_Indicator.vb
```

추가 메서드:

```vb
Public Function GetTickBarsSnapshot() As List(Of DateTime)
    SyncLock _tickLock
        Return New List(Of DateTime)(_tickBars)
    End SyncLock
End Function
```

데이터 확인창의 `틱원본(30틱)` 탭은 이 값을 시간순으로 표시한다.

표시 컬럼:

```text
Index
TickTimestamp
TimeOfDay
진단
```

---

### 2.3 Cybos tick timestamp 파싱 오류 수정

문제:

```text
2026-04-30 00:14:59
2026-04-30 00:15:00
2026-04-30 00:15:30
```

위 값은 실제로는 다음 장중 시간을 의미했다.

```text
2026-04-30 14:59:00
2026-04-30 15:00:00
2026-04-30 15:30:00
```

이 오류 때문에 1분봉 캔들은 09:00~15:30인데 tick timestamp는 00:09~00:15로 들어와 `RawTickMatched`가 0이 되었다.

수정 함수:

```vb
Private Shared Function NormalizeMarketTickTimestamp(ts As DateTime) As DateTime
    If ts.Hour = 0 AndAlso ts.Minute >= 8 AndAlso ts.Minute <= 15 Then
        Return New DateTime(ts.Year, ts.Month, ts.Day, ts.Minute, ts.Second, 0)
    End If
    Return ts
End Function
```

`SetTickBars()`에서 저장 전 보정하도록 수정했다.

수정 후 확인된 상태:

- `RawTickMatched` 정상 발생
- `Indicator.TickSum`이 양봉/음봉에 따라 + / - 로 정상 계산
- 예: 95, 83, -55, 88 등 정상 TickSum 확인

---

### 2.4 TickIntensity 계산 정상화 확인

데이터 확인창 `틱→1분봉 매핑` 탭에서 다음이 확인되었다.

```text
RawTickMatched > 0
Indicator.TickSum 정상
Indicator.MA5 정상
Indicator.MA20 정상
IndicatorExists = Y
진단 = OK
```

중요:

- `Candle.TickCount`와 `Candle.NTS`는 여전히 0일 수 있음
- 현재 신뢰 가능한 TickIntensity 출처는 `StockState.Engine.Results("TICKINT_...")`의 최신 결과다.

신뢰 기준:

```text
StockState.Engine.Results("TICKINT_...").Last().Val("TickSum")
StockState.Engine.Results("TICKINT_...").Last().Val("MA5")
StockState.Engine.Results("TICKINT_...").Last().Val("MA20")
```

---

### 2.5 StateManager UI snapshot 표시 신뢰성 보강

문제:

데이터 확인창의 `Indicator.TickSum`은 정상인데 감시 그리드 `TickSum`이 전 종목 `2.0`처럼 표시되었다.

의심:

```text
TickBarCount=2000
TickSum=2.0
=> 2000 / 1000 형태의 잘못된 중간값이 StockState.TickSum_Normalized에 들어간 것으로 추정
```

수정 파일:

```text
MainApp/SimTrade/StateManager.vb
```

수정 내용:

`CreateSnapshot()`에서 UI 표시용 TickSum은 `StockState.TickSum_Normalized`보다 `IndicatorEngine.Results`의 최신 `TICKINT_` 값을 우선 사용하도록 변경했다.

핵심 로직:

```vb
Dim indicatorTick As Double = Double.NaN
Dim indicatorMA5 As Double = Double.NaN
Dim indicatorMA20 As Double = Double.NaN
Dim hasIndicatorTick As Boolean = TryGetLatestTickIntensity(s, indicatorTick, indicatorMA5, indicatorMA20)

If hasIndicatorTick Then
    snap.TickSum_Normalized = indicatorTick
    snap.TickMA5_Normalized = indicatorMA5
    snap.TickMA20_Normalized = indicatorMA20
    snap.StateTickSum_Normalized = s.TickSum_Normalized
    snap.TickSource = "Indicator"
Else
    snap.TickSum_Normalized = s.TickSum_Normalized
    snap.TickMA5_Normalized = s.TickMA5_Normalized
    snap.TickMA20_Normalized = s.TickMA20_Normalized
    snap.StateTickSum_Normalized = s.TickSum_Normalized
    snap.TickSource = "State"
End If
```

추가 snapshot 필드:

```vb
Public Property StateTickSum_Normalized As Double = Double.NaN
Public Property TickSource As String = ""
```

---

## 3. 미완료/다음 적용 대상

### 3.1 SimTradeUI TickSum 표시 개선

목표:

감시 그리드 `TickSum` 컬럼을 단순 숫자가 아니라 값/배열상태로 표시한다.

예:

```text
11.0/정
-11.0/강
3.0/약
0.0/무
2.0/역
```

판정 기준:

```text
정 = Abs(TickSum) >= 5 AND Abs(TickSum) > MA5 AND MA5 > MA20
역 = MA20 > MA5 AND MA5 > Abs(TickSum)
강 = Abs(TickSum) >= 5 이지만 정배열은 아님
약 = 0 < Abs(TickSum) < 5
무 = TickSum = 0 또는 지표 없음
```

대상 파일:

```text
MainApp/SimTrade/SimTradeUI.vb
```

수정 포인트:

```vb
row.Cells(7).Value = FormatTickStrength(s)
```

새 행 추가부도 `FormatTickStrength(s)`로 변경한다.

추가 함수:

```vb
Private Shared Function FormatTickStrength(s As StockStateSnapshot) As String
    If s Is Nothing Then Return "-"
    If Double.IsNaN(s.TickSum_Normalized) Then Return "-"

    Dim label As String = GetTickArrayLabel(s.TickSum_Normalized, s.TickMA5_Normalized, s.TickMA20_Normalized)
    Return s.TickSum_Normalized.ToString("F1") & "/" & label
End Function

Private Shared Function GetTickArrayLabel(tickSum As Double, ma5 As Double, ma20 As Double) As String
    If Double.IsNaN(tickSum) Then Return "무"
    Dim absTick As Double = Math.Abs(tickSum)
    If absTick = 0.0R Then Return "무"

    If Double.IsNaN(ma5) OrElse Double.IsNaN(ma20) Then
        If absTick >= 5.0R Then Return "강"
        Return "약"
    End If

    If absTick >= 5.0R AndAlso absTick > ma5 AndAlso ma5 > ma20 Then Return "정"
    If ma20 > ma5 AndAlso ma5 > absTick Then Return "역"
    If absTick >= 5.0R Then Return "강"
    Return "약"
End Function
```

이 작업은 화면 표시 개선이며 매매판단/TopN 산식 변경이 아니다.

---

## 4. 가장 중요한 남은 문제

### 4.1 StockState.TickSum_Normalized 오염 원인 제거

현재 UI snapshot은 Indicator 값을 우선 표시하게 했지만, 내부 `StockState.TickSum_Normalized` 자체가 여전히 오염되어 있을 수 있다.

의심 위치:

```text
MainApp/SimTrade/SimTradeEngine.vb
UpdateStateIndicators(state)
StateManager.UpdateIndicators(...) 호출부
```

의심 산식:

```text
TickBarCount=2000 → TickSum=2.0
```

수정 원칙:

```text
StockState.TickSum_Normalized도 IndicatorResult 최신 TICKINT 값과 일치해야 한다.
전체 TickBarCount 요약값을 TickSum으로 저장하면 안 된다.
```

목표:

```vb
Dim tiResults As List(Of IndicatorResult) = ...
Dim lastTi As IndicatorResult = tiResults(tiResults.Count - 1)

tickSum = CDbl(lastTi.Val("TickSum"))
tickMA5 = CDbl(lastTi.Val("MA5"))
tickMA20 = CDbl(lastTi.Val("MA20"))
```

---

### 4.2 Top10Filter / TopNScore 데이터 출처 확인

위험:

감시 그리드는 Indicator 값을 표시하지만, TopN 계산은 여전히 오염된 `StockState.TickSum_Normalized`를 사용할 수 있다.

확인 대상:

```text
MainApp/SimTrade/Top10Filter.vb
MainApp/SimTrade/SimTradeEngine.vb
```

확인할 것:

- TopTickScore 산식
- TickSum_Normalized 사용 여부
- MA5/MA20 배열 반영 여부
- StateManager Snapshot과 다른 출처를 쓰는지

수정 원칙:

```text
TopN 활용은 다음 문제지만, 데이터 출처는 반드시 신뢰 가능한 IndicatorEngine TICKINT 결과와 일치해야 한다.
```

---

## 5. 다음 세션 시작 절차

```powershell
cd E:\2026\infra

git status
git branch --show-current
git log --oneline -10

dotnet build .\MainApp\MainApp.vbproj -c Debug -p:Platform=x64
```

확인할 것:

1. 현재 브랜치가 `infra-hardening-p0`인지
2. 로컬 수정분이 있는지
3. `SimTradeUI.vb` TickSum 표시 패치가 적용됐는지
4. 빌드 성공 여부

---

## 6. 다음 작업 순서

1. `SimTradeUI.vb` TickSum 값/배열상태 표시 패치 적용
2. 감시 그리드 TickSum과 데이터확인창 마지막 `Indicator.TickSum` 일치 확인
3. `SimTradeEngine.UpdateStateIndicators()`에서 `StockState.TickSum_Normalized` 오염 원인 제거
4. `Top10Filter`가 신뢰 가능한 Indicator TickSum을 사용하는지 확인
5. `순위→수익 검증` 탭 본격화

수익검증 필수 컬럼:

```text
시간
Rank
코드
종목명
진입기준가
TickSum
TickMA5
TickMA20
Tick배열상태
TopScore
이후5분최대수익률
이후10분최대수익률
이후20분최대수익률
최대역행률
결과등급
```

---

## 7. Git 주의사항

푸시 전 반드시 확인:

```powershell
git status --short
git diff --cached --name-only
```

절대 포함 금지:

```text
*.db
*.db-wal
*.db-shm
.env
connection.ts
secret/config/token/password 관련 파일
```

---

## 8. 짧은 결론

현재 상태:

```text
Chart V2는 정상화됨
TickIntensity timestamp 파싱 오류 수정됨
원본 tick timestamp 직접 확인 가능
틱→1분봉 매핑 정상화됨
Indicator.TickSum / MA5 / MA20 정상 계산 확인됨
StateManager Snapshot은 Indicator TickSum을 우선 표시하도록 보강됨
```

남은 핵심:

```text
StockState.TickSum_Normalized 오염 원인 제거
TopN이 신뢰 가능한 TickIntensity를 쓰는지 확인
감시 그리드 TickSum을 값/배열상태로 표시
순위→수익 검증 탭 본격화
```
