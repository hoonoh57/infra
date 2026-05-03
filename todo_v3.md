# todo_v3.md — Strategy Circuit Tester / TopN 검증 로직 Session Handover

작성일: 2026-05-03  
브랜치: `infra-hardening-p0`  
프로젝트: `E:\2026\infra`  
핵심 목표: 조건식 포착 후보 중 진짜 대장주 TopN을 선별하고, 목표수익 가능 구간만 매매하도록 검증 가능한 체계를 구축한다.

---

## 0. 현재 세션 결론 요약

이번 세션의 핵심 성과는 다음이다.

1. **Range Test 정상화**
   - 이전에는 현재 선택된 봉의 마지막 평가값이 Range Test 전체에 반복 적용되는 현상이 있었다.
   - 현재는 Range Test가 각 봉을 순회 평가하여 매수신호 결과를 정상 출력한다.
   - 024840, 025860, 059090, 004430 등의 단일 종목 테스트에서 결과표가 정상 출력됨.

2. **JMA 조건 수정 완료**
   - 기존 `JMA 상승 전환 후 ConfirmBars 이내` 조건은 상승 초입 포착과 불일치가 컸다.
   - 현재는 `JMA_Direction > 0`, 즉 **현재 JMA 상승 상태**로 판단한다.
   - 관련 파일:
     - `MainApp/SimTrade/SignalEvaluator.vb`
     - `MainApp/SimTrade/Circuit/CircuitEngine.vb`
     - `MainApp/SimTrade/Circuit/CircuitDesignerForm.vb`

3. **TickSum 표시/동기화 수정 완료**
   - StockState의 TickSum은 `TickIntensity_Indicator`에서 계산된 최종값을 그대로 사용한다.
   - 이중 Normalize로 인해 `1.0/약`만 보이던 문제를 수정했다.

4. **OUT_BUY 최종 매수신호 동기화 완료**
   - `OUT_BUY` 노드는 `CircuitEvalResult.BuySignal`과 동기화된다.
   - 화면의 BUY AND와 최종 매수 신호가 일치하도록 정리했다.

5. **Range Test 분석 컬럼 v1 정상 출력**
   - 기존 단순 `+1/+3/+5/+10/+20` 분석보다 실전 목적에 맞는 분석 컬럼을 추가했다.
   - 현재 결과표에 다음 컬럼들이 정상 출력된다.

```text
Seq
PrevGap
Open%
Low%
HighGap%
Tick/MA5
Tick/MA20
MFE10M
MFE30M
MFE60M
MAE10M
MAE30M
MAE60M
T10
T30
T60
ExitReason
Realized%
HoldMin
Ban
RiskFlags
```

6. **프린트/백테스트 출력 폼 실험은 롤백**
   - `CircuitBacktestReportDialog.vb`를 만들었으나 평가 상태 공유 문제로 안정적이지 않았다.
   - 해당 신규 파일은 삭제했다.
   - 이후에는 먼저 Range Test 결과를 내부 리스트화한 뒤 별도 뷰어를 만든다.

7. **현재 설계 방향 확정**
   - 목표는 매수신호를 많이 만드는 것이 아니다.
   - 조건식으로 포착된 “물반 고기반” 후보군 중에서:
     - 진짜 대장주 후보
     - 상승 초입
     - 가장 강한 매매가 들어오는 구간
     - 손실 위험이 낮은 구간
   - 이 조합을 찾아 TopN에 반영하는 것이 목적이다.

---

## 1. 현재 Git 히스토리 기준 중요 커밋

최근 핵심 커밋 흐름:

```text
9b703f9 Align circuit tester JMA score with rising-state condition
dc07297 Sync circuit buy output node with final buy signal
83e91bc Add single-stock circuit range validation MVP
9af3785 Fix remaining implicit conversion warnings
fe203e7 Suppress noisy SkiaSharp obsolete warnings
af87b67 Use JMA rising state instead of turn window for buy condition
4f13198 Sync StockState TickSum from TickIntensity indicator
05abb37 Add local server startup scripts
707fe5d Show TickSum array state in SimTrade watch grid
```

현재 세션 마지막 작업으로 `Range Test 분석 컬럼 v1`을 적용했으므로 아직 커밋하지 않았다면 아래 명령으로 커밋/푸시한다.

```powershell
cd E:\2026\infra

git status --short

dotnet build .\MainApp\MainApp.vbproj -c Debug -p:Platform=x64

git add MainApp/SimTrade/Circuit/CircuitDesignerForm.vb

git diff --cached --name-only

git commit -m "Add range test signal quality metrics"
git push origin infra-hardening-p0

git status --short
git log --oneline -5
```

---

## 2. 현재 정상 동작 확인 기준

### 실행

```powershell
cd E:\2026\infra
dotnet build .\MainApp\MainApp.vbproj -c Debug -p:Platform=x64
.\restart.bat
```

### Circuit Tester 확인

1. `Strategy Circuit Tester v5.0` 실행
2. 종목코드 입력
   - 예: `024840`, `025860`, `059090`, `004430`
3. `[캔들 로드]`
4. From / To 설정
   - 예: `2026-04-29` ~ `2026-04-30`
5. 목표수익률 `T% = 5.0`, 위험 기준 `S% = 1.5`
6. `[Range Test]`
7. 결과표에 분석 컬럼이 출력되는지 확인

---

## 3. 확정된 매도 검증 기준

앞으로 모든 매수신호의 검증은 동일한 매도 기준으로 평가한다.

### 표준 매도 기준 v1

```text
[Entry]
- 회로 BuySignal=True인 봉에서 매수 후보 발생

[Target]
- 매수가 대비 고가가 TargetPct 이상 도달하면 TargetReached=True

[Exit]
1. SuperTrend 하락 전환
   → 즉시 청산
   → 목표수익 달성 여부와 무관

2. 목표수익 전 JMA 하락 + SuperTrend 상승 유지
   → 매도 보류
   → 큰 추세는 살아 있으므로 작은 JMA 흔들림에 조기청산하지 않음

3. 목표수익 달성 후 JMA 하락
   → 매도
   → 해당 종목 당일 매매금지
   → ExitReason = TARGET_THEN_JMA_DOWN

4. 검증 종료까지 미청산
   → 마지막 봉 기준 평가 청산
   → 목표수익 달성 여부에 따라 TARGET_NO_JMA_EXIT 또는 LAST_BAR_NO_TARGET
```

### 현재 Range Test 컬럼의 의미

- `MFE10M`: 매수 후 10분 내 최대 수익률
- `MFE30M`: 매수 후 30분 내 최대 수익률
- `MFE60M`: 매수 후 60분 내 최대 수익률
- `MAE10M`: 매수 후 10분 내 최대 역행률
- `MAE30M`: 매수 후 30분 내 최대 역행률
- `MAE60M`: 매수 후 60분 내 최대 역행률
- `T10`: 10분 내 목표수익률 도달 여부
- `T30`: 30분 내 목표수익률 도달 여부
- `T60`: 60분 내 목표수익률 도달 여부
- `ExitReason`: 표준 매도 기준에 따른 청산 사유
- `Realized%`: 표준 매도 기준 실현 수익률
- `HoldMin`: 보유 시간
- `Ban`: 목표수익 달성 후 JMA 하락 매도되어 해당 종목 당일 재매매 금지 여부
- `RiskFlags`: 덫/위험 후보 플래그

---

## 4. 철학과 목표

조건식은 이미 “물반 고기반” 후보군을 제공한다.  
우리의 역할은 그 안에서 다음을 골라내는 것이다.

```text
1. 진짜 대장주
2. 상승 초입
3. 가장 강한 매매가 들어오는 순간
4. 손실 가능성이 낮은 타점
5. 목표수익을 달성하고 이탈할 수 있는 구간
```

### 금지할 착각

```text
TickIntensity + 노드 통과 = 무조건 매수
```

Tick이 큰 구간은 다음도 포함한다.

```text
- 상승 초입
- 고점권 물량 교환
- VI 직전 추격
- 급등 후 분배
- 반등 실패 구간
- 손절/익절 물량 혼재
```

따라서 TickIntensity는 단독 매수 조건이 아니라 **대장주 랭킹 핵심 피처**로 사용한다.

---

## 5. 손실/수익 패턴 지식베이스 방향

수익 사례뿐 아니라 손실 사례도 반드시 지식베이스화한다.

### 수익 패턴 예시

```text
WIN_EARLY_LEADER_TICK_EXPANSION
- 조건식 포착 직후
- Tick 강도 급증
- Tick/MA5, Tick/MA20 양호
- ST/JMA/OBV/MACD 정렬
- MFE10M 또는 MFE30M 목표 달성
```

```text
WIN_BREAKOUT_WITH_TICK_OBV_CONFIRM
- 고점 돌파
- Tick 증가
- OBV 동반 상승
- MACD 양호
- MAE 작고 MFE 빠르게 확대
```

### 손실/덫 패턴 예시

```text
TRAP_GAP_OVERHEAT
- 갭상승 후 추가 Tick 폭발
- 이미 상승 여력 소진 가능
```

```text
TRAP_VI_NEAR_CHASE
- VI 근접 급등 중 추격 매수
- 순간 수익보다 급락 위험 큼
```

```text
TRAP_POST_SURGE_REENTRY
- 첫 상승 성공 이후 반복 신호
- 후행 진입 가능성 큼
```

```text
TRAP_TICK_NO_PRICE_EFF
- Tick은 큰데 가격이 못 오름
- 물량 소화/분배 가능성
```

```text
TRAP_FAKE_BREAKOUT
- 고점 돌파처럼 보이다가 돌파 후 바로 밀림
```

```text
TRAP_HIGH_CHASE
- 당일 저점 대비 이미 과도하게 상승한 후 진입
```

---

## 6. 현재 Range Test v1에서 적용된 RiskFlags 후보

현재 `RiskFlags`에 들어가는 초기 후보:

```text
GAP_OVERHEAT
VI_NEAR
POST_SURGE_REENTRY
HIGH_CHASE
FAKE_BREAK_OR_PULLBACK
TICK_NO_PRICE_EFF
JMA_LATE
FAST_ADVERSE_MOVE
```

이 플래그들은 아직 매수차단 조건이 아니다.  
현재 단계에서는 **기록용/분석용**으로만 사용한다.

향후 검증을 통해 특정 RiskFlag가 반복 손실과 강하게 연결되면:

```text
기록용 플래그 → 감점 → 진입 제한 → 당일 제외
```

순서로 승격한다.

---

## 7. 다음 작업: 전체 종목 일괄 분석

현재 단일 종목 Range Test 분석은 정상화되었다.  
다음 목표는 조건식 포착 종목 전체 일괄 분석이다.

### 기능명

```text
Condition Batch Validator
```

### 입력

```text
- 조건식 포착 종목 리스트
- From 날짜
- To 날짜
- 조건식 포착 시각
- 목표수익률
- TopN 개수
```

### 출력 1: 종목별 요약표

```text
Code
Name
SignalCount
FirstSignalTime
BestMFE10M
BestMFE30M
BestMFE60M
WorstMAE10M
AvgMAE10M
Target10Count
Target30Count
Target60Count
BestExitReason
RiskFlags
LeaderScore
Rank
```

### 출력 2: 신호별 상세표

```text
Code
Name
Time
Seq
Price
Score
Tick
Tick/MA5
Tick/MA20
Open%
Low%
HighGap%
MFE10M
MFE30M
MFE60M
MAE10M
MAE30M
MAE60M
T10
T30
T60
ExitReason
Realized%
HoldMin
Ban
RiskFlags
```

---

## 8. TopN 선별 로직 초안

처음에는 실매매 적용이 아니라 검증용 LeaderScore로 시작한다.

### LeaderScore v0

```text
LeaderScore =
    TickPowerScore
  + TickPositionScore
  + ConfirmationScore
  - RiskPenalty
```

### TickPowerScore

```text
Tick/MA5 >= 1.5        +15
Tick/MA20 >= 1.5       +15
Tick 자체 상위권       +10
```

### TickPositionScore

```text
Seq = 1                +20
PrevGap >= 10          +5
Low% <= 8              +10
HighGap% >= -2         +10
```

### ConfirmationScore

```text
ST 상승                +10
JMA 상승               +10
OBV 상승               +8
MACD 양호              +8
Volume 양호            +5
```

### RiskPenalty

```text
POST_SURGE_REENTRY     -20
HIGH_CHASE             -15
TICK_NO_PRICE_EFF      -20
GAP_OVERHEAT           -15
VI_NEAR                -20
FAST_ADVERSE_MOVE      -25
JMA_LATE               -10
```

### 검증 질문

```text
조건식 포착 종목 중 LeaderScore TopN이
실제 MFE10M/MFE30M/MFE60M 상위 종목과 얼마나 겹치는가?
```

---

## 9. 향후 UI/로직 확장 단계

### P1. 현재 상태 커밋/푸시

```powershell
cd E:\2026\infra

git status --short
dotnet build .\MainApp\MainApp.vbproj -c Debug -p:Platform=x64
git add MainApp/SimTrade/Circuit/CircuitDesignerForm.vb
git commit -m "Add range test signal quality metrics"
git push origin infra-hardening-p0
```

### P2. Range Test 결과를 내부 List로 반환 가능하게 분리

현재 결과는 `_dgvValidation` 중심이다.  
다음 단계에서는 내부적으로 `List(Of RangeSignalQualityResult)`를 생성하고, UI는 이 리스트를 출력만 하도록 분리한다.

필요한 클래스 후보:

```vbnet
Public Class RangeSignalQualityResult
    Public Property Code As String
    Public Property Name As String
    Public Property EntryIndex As Integer
    Public Property EntryTime As DateTime
    Public Property EntryPrice As Double
    Public Property Seq As Integer
    Public Property PrevGap As Integer
    Public Property OpenPct As Double
    Public Property LowPct As Double
    Public Property HighGapPct As Double
    Public Property Tick As Double
    Public Property TickMA5 As Double
    Public Property TickMA20 As Double
    Public Property TickVsMA5 As Double
    Public Property TickVsMA20 As Double
    Public Property MFE10M As Double
    Public Property MFE30M As Double
    Public Property MFE60M As Double
    Public Property MAE10M As Double
    Public Property MAE30M As Double
    Public Property MAE60M As Double
    Public Property T10 As Boolean
    Public Property T30 As Boolean
    Public Property T60 As Boolean
    Public Property ExitReason As String
    Public Property RealizedPct As Double
    Public Property HoldMin As Double
    Public Property BanAfterExit As Boolean
    Public Property RiskFlags As String
    Public Property LeaderScore As Double
End Class
```

### P3. Batch Validator 구현

- 조건식 포착 종목 리스트를 입력받는다.
- 각 종목을 순회하며 기존 Range Test 분석 로직을 실행한다.
- 종목별 Summary와 신호별 Detail을 출력한다.

### P4. LeaderScore 계산 추가

`RangeSignalQualityResult`마다 LeaderScore를 계산한다.  
처음에는 검증용 점수로만 사용하고 실매매에는 적용하지 않는다.

### P5. TopN Hit Rate 계산

아래 기준을 계산한다.

```text
TopN 평균 MFE10M
TopN 평균 MFE30M
TopN 평균 MFE60M
TopN 평균 MAE10M
TopN T10/T30/T60 비율
TopN ST_DOWN_FORCE_EXIT 비율
TopN RiskFlags 비율
```

---

## 10. 다음 세션 시작 시 바로 할 일

### 1단계: 현재 커밋 상태 확인

```powershell
cd E:\2026\infra

git status --short
git log --oneline -8
dotnet build .\MainApp\MainApp.vbproj -c Debug -p:Platform=x64
```

### 2단계: Range Test v1 정상 재확인

```text
024840, 025860, 059090 중 하나를 로드
2026-04-29 ~ 2026-04-30
T=5.0
S=1.5
Range Test 실행
분석 컬럼 출력 확인
```

### 3단계: 결과 리스트 분리 설계/구현

`CircuitDesignerForm.vb` 내부에서 `_dgvValidation`에 직접 쓰는 구조를 유지하되, 동시에 `List(Of RangeSignalQualityResult)`를 생성하도록 한다.

### 4단계: 전체 종목 Batch Validator로 확장

조건식 포착 종목 리스트를 받아서 종목별 Range Test를 일괄 실행한다.

---

## 11. 주의사항

1. **백테스트 출력 다이얼로그 재시도 금지**
   - 이번 세션에서 `CircuitBacktestReportDialog.vb` 실험은 실패 후 삭제했다.
   - 다시 만들려면 반드시 Range Test 결과 리스트가 안정된 뒤에 별도 ReadOnly 뷰어로 만든다.

2. **Range Test 정상화 이후 EvaluateAtCandle 구조를 불필요하게 흔들지 말 것**
   - 현재 Range Test는 정상 작동한다.
   - 다음 작업은 결과값을 리스트화/Batch화하는 것이지, 회로 평가 자체를 다시 바꾸는 것이 아니다.

3. **RiskFlags는 당장 매수 차단에 쓰지 말 것**
   - 우선 기록/분석용.
   - 충분한 샘플에서 손실 상관성이 확인된 뒤 감점/차단으로 승격한다.

4. **+1/+3/+5/+10/+20 중심 분석으로 회귀하지 말 것**
   - 현재 목적은 고정 봉 후 종가 수익률이 아니라:
     - MFE10M/30M/60M
     - MAE10M/30M/60M
     - 목표 도달 여부
     - 표준 매도 기준 실현수익
   - 이 기준을 유지한다.

5. **TopN 검증 목표**
   - 상승률 순위가 아니라:
     - TickPower
     - TickPosition
     - Confirmation
     - RiskPenalty
   - 이 조합으로 조건식 후보군 중 진짜 대장주 TopN을 고른다.

---

## 12. 현재 한 줄 목표

```text
조건식이 물반 고기반 후보를 주면,
우리는 틱강도·틱포지션·보조지표·위험플래그를 이용해
TopN에 오른 종목이 목표수익을 내고 이탈하는 구조를 검증한다.
```
