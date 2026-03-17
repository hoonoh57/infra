' ═══════════════════════════════════════════════════════════════
' ZeroLossLiveStrategy.vb — Zero Loss 실전 매매 전략
' ═══════════════════════════════════════════════════════════════
'
' "결코 잃지 마라" — 25일 백테스트에서 유일하게 생존한 전략
'
' ★ 검증된 파라미터 (2026-02-02 ~ 2026-03-12, 25 거래일):
'   OC=7%, Pos=5, N=3%, S=-3%, T=10%
'   → Net +33.75%, 80 trades, 0 Loss Days
'
' ★ 핵심 설계 원칙:
'   1) 키움 조건식으로 KOSDAQ150 중 OC 3%+ 종목 포착
'   2) 포착된 종목만 실시간 구독 → 틱 기반 정밀 진입 판단
'   3) 시가 대비 7%+ 급등 + 거래대금 100억+ + RS 3% 돌파 시 매수
'   4) 동시 최대 5포지션, 포지션당 자본 10%
'   5) Stop -3% / Target +10% / 14:50 일괄 청산
'   6) 시장 급락 -2% 시 진입 중단, -3% 시 전체 청산
'   7) 오버나이트 보유 절대 불가
'
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Concurrent
Imports System.Globalization
Imports [Shared]

Public Class ZeroLossLiveStrategy

    ' ─── 싱글톤 ───
    Private Shared _instance As ZeroLossLiveStrategy
    Private Shared ReadOnly _lock As New Object()

    Public Shared ReadOnly Property I As ZeroLossLiveStrategy
        Get
            If _instance Is Nothing Then
                SyncLock _lock
                    If _instance Is Nothing Then _instance = New ZeroLossLiveStrategy()
                End SyncLock
            End If
            Return _instance
        End Get
    End Property

    ' ═══════════════════════════════════════
    ' 전략 파라미터 (25일 검증 확정값)
    ' ═══════════════════════════════════════

    ''' <summary>시가 대비 상승률 % (진입 기준)</summary>
    Public Property OpenChangeThreshold As Decimal = 7D

    ''' <summary>누적 거래대금 억원 (진입 기준)</summary>
    Public Property TradeAmountThresholdEok As Decimal = 100D

    ''' <summary>RS 돌파 기준 % (종목수익률 - 코스닥수익률)</summary>
    Public Property RelativeStrengthThreshold As Decimal = 3D

    ''' <summary>스톱로스 % (진입가 대비)</summary>
    Public Property StopLossPct As Decimal = -3D

    ''' <summary>익절 % (진입가 대비)</summary>
    Public Property TargetProfitPct As Decimal = 10D

    ''' <summary>동시 최대 포지션 수</summary>
    Public Property MaxPositions As Integer = 5

    ''' <summary>포지션당 자본 비율 %</summary>
    Public Property PositionSizePct As Decimal = 10D

    ''' <summary>일일 최대 손실 % (자본 대비)</summary>
    Public Property MaxDailyLossPct As Decimal = 1D

    ''' <summary>시장 급락 진입 중단 %</summary>
    Public Property MarketDropHaltPct As Decimal = -2D

    ''' <summary>시장 급락 전체 청산 %</summary>
    Public Property MarketCrashExitPct As Decimal = -3D

    ''' <summary>스캔 시작 시각</summary>
    Public Property ScanStartTime As TimeSpan = New TimeSpan(9, 1, 0)

    ''' <summary>스캔 종료 시각 (이후 신규 진입 금지)</summary>
    Public Property ScanEndTime As TimeSpan = New TimeSpan(14, 30, 0)

    ''' <summary>일괄 청산 시각</summary>
    Public Property FinalExitTime As TimeSpan = New TimeSpan(14, 50, 0)

    Private Const STRATEGY_NAME As String = "ZeroLoss"

    ' ═══════════════════════════════════════
    ' 런타임 상태
    ' ═══════════════════════════════════════

    ''' <summary>전략 활성화 여부</summary>
    Public Property IsRunning As Boolean = False

    ' ── KOSDAQ150 유니버스 ──
    Private ReadOnly _universe As New ConcurrentDictionary(Of String, StockState)(StringComparer.OrdinalIgnoreCase)

    ' ── 금일 포지션 추적 (TradeManager와 별도로 전략 레벨 추적) ──
    Private ReadOnly _activePositions As New ConcurrentDictionary(Of String, LivePosition)(StringComparer.OrdinalIgnoreCase)

    ' ── 금일 진입 이력 (동일 종목 재진입 방지) ──
    Private ReadOnly _todayEnteredCodes As New ConcurrentDictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)

    ' ── 금일 손익 추적 ──
    Private _dailyRealizedPnL As Decimal = 0D
    Private _initialCapital As Decimal = 0D

    ' ── 시장 상태 ──
    Private _kosdaqOpenPrice As Decimal = 0D
    Private _kosdaqCurrentPrice As Decimal = 0D
    Private _marketHalted As Boolean = False
    Private _dailyLossHalted As Boolean = False

    ' ── 타이머 ──
    Private _monitorTimer As System.Threading.Timer
    Private ReadOnly _stateLock As New Object()
    Private _lastDate As Date = Date.MinValue

    ' ═══════════════════════════════════════
    ' 초기화 / 시작 / 중지
    ' ═══════════════════════════════════════

    Private Sub New()
        AppLogger.I.Info("ZeroLoss 전략 인스턴스 생성", "ZeroLoss")
    End Sub

    ''' <summary>전략 시작. 실시간 틱 구독 기반 모니터링.</summary>
    Public Sub Start()
        If IsRunning Then Return

        AppLogger.I.Info("═══════════════════════════════════════", "ZeroLoss")
        AppLogger.I.Info("  Zero Loss 전략 시작", "ZeroLoss")
        AppLogger.I.Info($"  OC={OpenChangeThreshold}% Amt={TradeAmountThresholdEok}억 N={RelativeStrengthThreshold}%", "ZeroLoss")
        AppLogger.I.Info($"  S={StopLossPct}% T={TargetProfitPct}% Pos={MaxPositions}", "ZeroLoss")
        AppLogger.I.Info("═══════════════════════════════════════", "ZeroLoss")

        ' 초기 자본 기록
        _initialCapital = TradeManager.I.AvailableCash + TradeManager.I.TotalEvalAmount
        If _initialCapital <= 0 Then _initialCapital = TradeManager.I.AvailableCash

        ResetDailyState()

        ' ── MessageBus 구독 ──
        MessageBus.I.On(Topics.TICK, AddressOf OnTick)
        MessageBus.I.On(Topics.TRADE_ORDER_FILLED, AddressOf OnOrderFilled)
        MessageBus.I.On(Topics.TRADE_POSITION_UPDATED, AddressOf OnPositionUpdated)

        ' ── 1초 간격 모니터 타이머 ──
        _monitorTimer = New System.Threading.Timer(AddressOf OnMonitorTick, Nothing, 1000, 1000)

        IsRunning = True
        AppLogger.I.Info("ZeroLoss 전략 활성화 완료", "ZeroLoss")
    End Sub

    ''' <summary>전략 중지. 구독 해제.</summary>
    Public Sub [Stop]()
        If Not IsRunning Then Return

        IsRunning = False
        _monitorTimer?.Dispose()
        _monitorTimer = Nothing

        MessageBus.I.Off(Topics.TICK, AddressOf OnTick)
        MessageBus.I.Off(Topics.TRADE_ORDER_FILLED, AddressOf OnOrderFilled)
        MessageBus.I.Off(Topics.TRADE_POSITION_UPDATED, AddressOf OnPositionUpdated)

        AppLogger.I.Info("ZeroLoss 전략 중지", "ZeroLoss")
    End Sub

    ' ═══════════════════════════════════════
    ' KOSDAQ150 유니버스 관리
    ' ═══════════════════════════════════════

    ''' <summary>
    ''' KOSDAQ150 종목 목록 설정. MainShell 또는 UI에서 호출.
    ''' codes: 종목코드 리스트 (6자리)
    ''' </summary>
    Public Sub SetUniverse(codes As IEnumerable(Of String))
        _universe.Clear()
        For Each code In codes
            _universe(code) = New StockState() With {.Code = code}
        Next
        AppLogger.I.Info($"ZeroLoss 유니버스 설정: {_universe.Count}종목", "ZeroLoss")
    End Sub

    ''' <summary>
    ''' 종목의 당일 시가를 설정 (장 시작 시 또는 첫 틱 수신 시).
    ''' </summary>
    Public Sub SetOpenPrice(code As String, openPrice As Decimal)
        Dim state As StockState = Nothing
        If _universe.TryGetValue(code, state) Then
            If state.OpenPrice = 0D AndAlso openPrice > 0D Then
                state.OpenPrice = openPrice
            End If
        End If
    End Sub

    ''' <summary>코스닥 지수 시가 설정</summary>
    Public Sub SetKosdaqOpen(openPrice As Decimal)
        If _kosdaqOpenPrice = 0D AndAlso openPrice > 0D Then
            _kosdaqOpenPrice = openPrice
            AppLogger.I.Info($"KOSDAQ 시가 설정: {openPrice:N2}", "ZeroLoss")
        End If
    End Sub

    ' ═══════════════════════════════════════
    ' 실시간 틱 처리 (★ 핵심 진입 로직)
    ' ═══════════════════════════════════════

    Private Sub OnTick(m As Msg)
        If Not IsRunning Then Return

        Dim code = m.Str("code")
        If String.IsNullOrEmpty(code) Then Return

        Dim price = Math.Abs(CInt(m.Dbl("price")))
        Dim volume = m.Lng("volume")
        If price <= 0 Then Return

        ' ── 1. 코스닥 지수 틱 처리 ──
        If code = "101" OrElse code = "001" Then
            UpdateKosdaqIndex(price)
            Return
        End If

        ' ── 2. 유니버스 종목만 처리 ──
        Dim state As StockState = Nothing
        If Not _universe.TryGetValue(code, state) Then Return

        ' ── 3. 시가 초기화 (첫 틱) ──
        If state.OpenPrice = 0D Then state.OpenPrice = price

        ' ── 4. 상태 업데이트 ──
        state.CurrentPrice = price
        state.CumulativeVolume += volume
        state.CumulativeAmount += CDec(price) * volume
        state.LastTickTime = DateTime.Now

        ' ── 5. 보유 종목 퇴출 체크 ──
        Dim activePos As LivePosition = Nothing
        If _activePositions.TryGetValue(code, activePos) Then
            CheckExitConditions(activePos, price)
            Return  ' 보유 중이면 진입 로직 스킵
        End If

        ' ── 6. 진입 조건 평가 ──
        EvaluateEntry(state, price)
    End Sub

    Private Sub UpdateKosdaqIndex(price As Integer)
        ' 코스닥 지수는 x100으로 전달되는 경우가 있음
        Dim indexPrice = If(price > 10000, CDec(price) / 100D, CDec(price))

        If _kosdaqOpenPrice = 0D Then
            _kosdaqOpenPrice = indexPrice
        End If
        _kosdaqCurrentPrice = indexPrice

        ' ── 시장 급락 체크 ──
        If _kosdaqOpenPrice > 0D Then
            Dim marketReturn = (_kosdaqCurrentPrice / _kosdaqOpenPrice - 1D) * 100D

            If marketReturn <= MarketCrashExitPct Then
                ' 시장 급락 → 전체 청산
                If Not _marketHalted Then
                    AppLogger.I.Trade($"★★★ 시장 급락 감지: KOSDAQ {marketReturn:+0.00;-0.00}% ≤ {MarketCrashExitPct}% → 전체 청산", "ZeroLoss")
                    _marketHalted = True
                    ExitAllPositions("시장급락청산")
                End If
            ElseIf marketReturn <= MarketDropHaltPct Then
                ' 시장 하락 → 진입 중단
                If Not _marketHalted Then
                    AppLogger.I.Trade($"★ 시장 하락 감지: KOSDAQ {marketReturn:+0.00;-0.00}% ≤ {MarketDropHaltPct}% → 진입 중단", "ZeroLoss")
                    _marketHalted = True
                End If
            Else
                If _marketHalted AndAlso marketReturn > MarketDropHaltPct Then
                    _marketHalted = False
                    AppLogger.I.Info($"시장 회복: KOSDAQ {marketReturn:+0.00;-0.00}% → 진입 재개", "ZeroLoss")
                End If
            End If
        End If
    End Sub

    ' ═══════════════════════════════════════
    ' 진입 조건 평가
    ' ═══════════════════════════════════════

    Private Sub EvaluateEntry(state As StockState, price As Integer)
        Dim now = DateTime.Now
        Dim timeOfDay = now.TimeOfDay

        ' ── 시간 필터 ──
        If timeOfDay < ScanStartTime OrElse timeOfDay > ScanEndTime Then Return

        ' ── 이미 금일 진입한 종목 ──
        If _todayEnteredCodes.ContainsKey(state.Code) Then Return

        ' ── 이미 RS 돌파 기록 (한번만 진입) ──
        If state.RsTriggered Then Return

        ' ── 포지션 한도 ──
        If _activePositions.Count >= MaxPositions Then Return

        ' ── 시장 급락 중단 ──
        If _marketHalted Then Return

        ' ── 일일 손실 한도 ──
        If _dailyLossHalted Then Return

        ' ── 조건 1: 시가 대비 상승률 >= OC% ──
        If state.OpenPrice <= 0D Then Return
        Dim openChange = (CDec(price) / state.OpenPrice - 1D) * 100D
        If openChange < OpenChangeThreshold Then Return

        ' ── 조건 2: 누적 거래대금 >= Amt 억원 ──
        Dim tradeAmountEok = state.CumulativeAmount / 100_000_000D
        If tradeAmountEok < TradeAmountThresholdEok Then Return

        ' ── 조건 3: RS 돌파 (종목수익률 - 코스닥수익률 >= N%) ──
        If _kosdaqOpenPrice <= 0D Then Return
        Dim stockReturn = openChange
        Dim kosdaqReturn = (_kosdaqCurrentPrice / _kosdaqOpenPrice - 1D) * 100D
        Dim relativeStrength = stockReturn - kosdaqReturn

        If relativeStrength < RelativeStrengthThreshold Then Return

        ' ══════════════════════════════════
        ' ★ 모든 조건 충족 → 매수 시그널!
        ' ══════════════════════════════════

        state.RsTriggered = True
        state.TriggerTime = now
        state.TriggerPrice = price
        state.RelativeStrengthAtTrigger = relativeStrength

        AppLogger.I.Trade($"★ ZeroLoss 매수 시그널: {state.Code} " &
                          $"OC={openChange:+0.0}% Amt={tradeAmountEok:N0}억 RS={relativeStrength:+0.0}% " &
                          $"@{price:N0}", "ZeroLoss")

        ' ── 주문 수량 계산 ──
        Dim capital = TradeManager.I.AvailableCash + TradeManager.I.TotalEvalAmount
        If capital <= 0 Then capital = _initialCapital
        Dim positionAmount = capital * PositionSizePct / 100D
        Dim qty = CInt(Math.Floor(positionAmount / price))

        If qty <= 0 Then
            AppLogger.I.Warn($"ZeroLoss: 주문수량 0 (자본={capital:N0}, 가격={price:N0})", "ZeroLoss")
            Return
        End If

        ' ── 전략 레벨 포지션 기록 ──
        Dim pos As New LivePosition() With {
            .Code = state.Code,
            .EntryTime = now,
            .EntryPrice = price,
            .Quantity = qty,
            .OpenChange = openChange,
            .RelativeStrength = relativeStrength,
            .TradeAmountEok = tradeAmountEok
        }
        _activePositions(state.Code) = pos
        _todayEnteredCodes.TryAdd(state.Code, True)

        ' ── TradeManager에 매수 요청 ──
        TradeManager.I.BuyMarket(state.Code, qty, STRATEGY_NAME,
                                  $"OC={openChange:+0.0}% RS={relativeStrength:+0.0}% Amt={tradeAmountEok:N0}억")

        ' ── 실시간 구독 (아직 안 되어 있으면) ──
        MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", state.Code)

        ' ── 시그널 알림 ──
        Dim sm As New Msg(Topics.STRATEGY_SIGNAL)
        sm("type") = "BUY"
        sm("code") = state.Code
        sm("price") = price
        sm("strategy") = STRATEGY_NAME
        sm("reason") = $"OC={openChange:+0.0}% RS={relativeStrength:+0.0}%"
        MessageBus.I.EmitOnUI(sm)
    End Sub

    ' ═══════════════════════════════════════
    ' 퇴출 조건 체크
    ' ═══════════════════════════════════════

    Private Sub CheckExitConditions(pos As LivePosition, currentPrice As Integer)
        If pos.ExitPending Then Return  ' 이미 매도 주문 중

        Dim pnlPct = (CDec(currentPrice) / pos.EntryPrice - 1D) * 100D

        ' MFE/MAE 추적
        If pnlPct > pos.MaxReturnPct Then pos.MaxReturnPct = pnlPct
        If pnlPct < pos.MaxAdverseExcursionPct Then pos.MaxAdverseExcursionPct = pnlPct

        Dim exitReason As String = Nothing

        ' ── Stop-Loss ──
        If pnlPct <= StopLossPct Then
            exitReason = $"손절 {pnlPct:+0.00;-0.00}% ≤ {StopLossPct}%"

        ' ── Target Profit ──
        ElseIf pnlPct >= TargetProfitPct Then
            exitReason = $"익절 {pnlPct:+0.00;-0.00}% ≥ {TargetProfitPct}%"
        End If

        If exitReason IsNot Nothing Then
            ExecuteExit(pos, exitReason, currentPrice)
        End If
    End Sub

    ''' <summary>매도 실행</summary>
    Private Sub ExecuteExit(pos As LivePosition, reason As String, currentPrice As Integer)
        pos.ExitPending = True
        pos.ExitTime = DateTime.Now
        pos.ExitPrice = currentPrice

        Dim availQty = TradeManager.I.GetAvailableQty(pos.Code)
        If availQty <= 0 Then
            AppLogger.I.Warn($"ZeroLoss 매도 불가: {pos.Code} 매도가능수량=0", "ZeroLoss")
            pos.ExitPending = False
            Return
        End If

        Dim pnlPct = (CDec(currentPrice) / pos.EntryPrice - 1D) * 100D

        AppLogger.I.Trade($"★ ZeroLoss 매도: {pos.Code} {availQty}주 @{currentPrice:N0} " &
                          $"PnL={pnlPct:+0.00;-0.00}% ({reason})", "ZeroLoss")

        TradeManager.I.SellMarket(pos.Code, availQty, STRATEGY_NAME, reason)

        ' 실현 손익 누적
        _dailyRealizedPnL += pnlPct * PositionSizePct / 100D

        ' 일일 손실 한도 체크
        If _dailyRealizedPnL <= -MaxDailyLossPct Then
            _dailyLossHalted = True
            AppLogger.I.Trade($"★ 일일 손실 한도 도달: {_dailyRealizedPnL:+0.00;-0.00}% → 진입 중단", "ZeroLoss")
        End If

        ' 시그널 알림
        Dim sm As New Msg(Topics.STRATEGY_SIGNAL)
        sm("type") = "SELL"
        sm("code") = pos.Code
        sm("price") = currentPrice
        sm("strategy") = STRATEGY_NAME
        sm("reason") = reason
        MessageBus.I.EmitOnUI(sm)
    End Sub

    ''' <summary>전체 포지션 일괄 청산</summary>
    Private Sub ExitAllPositions(reason As String)
        For Each kvp In _activePositions
            Dim pos = kvp.Value
            If Not pos.ExitPending Then
                Dim currentPrice = CInt(TradeManager.I.GetPosition(pos.Code)?.CurrentPrice)
                If currentPrice <= 0 Then currentPrice = pos.EntryPrice
                ExecuteExit(pos, reason, currentPrice)
            End If
        Next
    End Sub

    ' ═══════════════════════════════════════
    ' 타이머 모니터 (1초 간격)
    ' ═══════════════════════════════════════

    Private Sub OnMonitorTick(state As Object)
        If Not IsRunning Then Return

        Dim now = DateTime.Now
        Dim timeOfDay = now.TimeOfDay

        ' ── 날짜 변경 감지 → 일일 상태 초기화 ──
        If now.Date <> _lastDate Then
            ResetDailyState()
            _lastDate = now.Date
        End If

        ' ── 14:50 일괄 청산 ──
        If timeOfDay >= FinalExitTime AndAlso _activePositions.Count > 0 Then
            AppLogger.I.Trade($"★ 14:50 일괄 청산: {_activePositions.Count}개 포지션", "ZeroLoss")
            ExitAllPositions("14:50 일괄청산")
        End If
    End Sub

    ' ═══════════════════════════════════════
    ' 일일 상태 초기화
    ' ═══════════════════════════════════════

    Private Sub ResetDailyState()
        SyncLock _stateLock
            ' 전일 포지션 정리 (오버나이트 방지)
            If _activePositions.Count > 0 Then
                AppLogger.I.Warn($"ZeroLoss: 전일 미청산 포지션 {_activePositions.Count}개 발견 — 즉시 청산", "ZeroLoss")
                ExitAllPositions("오버나이트방지")
            End If

            _activePositions.Clear()
            _todayEnteredCodes.Clear()
            _dailyRealizedPnL = 0D
            _dailyLossHalted = False
            _marketHalted = False
            _kosdaqOpenPrice = 0D
            _kosdaqCurrentPrice = 0D

            ' 유니버스 종목 상태 초기화
            For Each kvp In _universe
                Dim s = kvp.Value
                s.OpenPrice = 0D
                s.CurrentPrice = 0D
                s.CumulativeVolume = 0
                s.CumulativeAmount = 0D
                s.RsTriggered = False
                s.TriggerTime = DateTime.MinValue
                s.TriggerPrice = 0
                s.RelativeStrengthAtTrigger = 0D
                s.LastTickTime = DateTime.MinValue
            Next

            AppLogger.I.Info($"ZeroLoss 일일 상태 초기화 완료 ({DateTime.Now:yyyy-MM-dd})", "ZeroLoss")
        End SyncLock
    End Sub

    ' ═══════════════════════════════════════
    ' 체결/잔고 이벤트 후처리
    ' ═══════════════════════════════════════

    Private Sub OnOrderFilled(m As Msg)
        If m.Str("strategy") <> STRATEGY_NAME Then Return
        ' TradeManager가 이미 처리하므로 로그만 기록
        AppLogger.I.Info($"ZeroLoss 체결 확인: {m.Str("side")} {m.Str("code")} " &
                         $"{m.Int("filledQty")}주 @{m.Int("filledPrice"):N0}", "ZeroLoss")
    End Sub

    Private Sub OnPositionUpdated(m As Msg)
        Dim code = m.Str("code")
        Dim qty = m.Int("qty")

        ' 매도 완료 → 전략 포지션에서 제거
        If qty <= 0 Then
            Dim removed As LivePosition = Nothing
            If _activePositions.TryRemove(code, removed) Then
                AppLogger.I.Info($"ZeroLoss 포지션 청산 완료: {code} " &
                                 $"(보유 {_activePositions.Count}/{MaxPositions})", "ZeroLoss")
            End If
        End If
    End Sub

    ' ═══════════════════════════════════════
    ' 상태 조회 (UI용)
    ' ═══════════════════════════════════════

    ''' <summary>현재 활성 포지션 목록</summary>
    Public Function GetActivePositions() As List(Of LivePosition)
        Return _activePositions.Values.ToList()
    End Function

    ''' <summary>금일 진입한 종목 수</summary>
    Public ReadOnly Property TodayEntryCount As Integer
        Get
            Return _todayEnteredCodes.Count
        End Get
    End Property

    ''' <summary>금일 실현 손익 %</summary>
    Public ReadOnly Property DailyRealizedPnL As Decimal
        Get
            Return _dailyRealizedPnL
        End Get
    End Property

    ''' <summary>시장 중단 여부</summary>
    Public ReadOnly Property IsMarketHalted As Boolean
        Get
            Return _marketHalted
        End Get
    End Property

    ''' <summary>일일 손실 중단 여부</summary>
    Public ReadOnly Property IsDailyLossHalted As Boolean
        Get
            Return _dailyLossHalted
        End Get
    End Property

    ''' <summary>유니버스 종목 상태 전체 조회</summary>
    Public Function GetUniverseStates() As List(Of StockState)
        Return _universe.Values.ToList()
    End Function

    ''' <summary>KOSDAQ 지수 수익률 %</summary>
    Public ReadOnly Property KosdaqReturnPct As Decimal
        Get
            If _kosdaqOpenPrice <= 0D Then Return 0D
            Return (_kosdaqCurrentPrice / _kosdaqOpenPrice - 1D) * 100D
        End Get
    End Property

    ''' <summary>전략 상태 요약</summary>
    Public Function GetStatusSummary() As String
        Return $"[ZeroLoss] Active={_activePositions.Count}/{MaxPositions} " &
               $"Entries={TodayEntryCount} PnL={_dailyRealizedPnL:+0.00;-0.00}% " &
               $"KOSDAQ={KosdaqReturnPct:+0.00;-0.00}% " &
               $"Halted={If(_marketHalted, "MKT", If(_dailyLossHalted, "LOSS", "NO"))}"
    End Function

    ' ═══════════════════════════════════════
    ' 내부 모델
    ' ═══════════════════════════════════════

    ''' <summary>유니버스 종목의 당일 실시간 상태</summary>
    Public Class StockState
        Public Property Code As String = ""
        Public Property OpenPrice As Decimal = 0D
        Public Property CurrentPrice As Decimal = 0D
        Public Property CumulativeVolume As Long = 0
        Public Property CumulativeAmount As Decimal = 0D
        Public Property RsTriggered As Boolean = False
        Public Property TriggerTime As DateTime = DateTime.MinValue
        Public Property TriggerPrice As Integer = 0
        Public Property RelativeStrengthAtTrigger As Decimal = 0D
        Public Property LastTickTime As DateTime = DateTime.MinValue

        Public ReadOnly Property OpenChangePct As Decimal
            Get
                If OpenPrice <= 0D Then Return 0D
                Return (CurrentPrice / OpenPrice - 1D) * 100D
            End Get
        End Property

        Public ReadOnly Property TradeAmountEok As Decimal
            Get
                Return CumulativeAmount / 100_000_000D
            End Get
        End Property
    End Class

    ''' <summary>전략 레벨 포지션 추적</summary>
    Public Class LivePosition
        Public Property Code As String = ""
        Public Property EntryTime As DateTime
        Public Property EntryPrice As Integer
        Public Property Quantity As Integer
        Public Property ExitTime As DateTime
        Public Property ExitPrice As Integer
        Public Property ExitPending As Boolean = False
        Public Property OpenChange As Decimal
        Public Property RelativeStrength As Decimal
        Public Property TradeAmountEok As Decimal
        Public Property MaxReturnPct As Decimal = 0D
        Public Property MaxAdverseExcursionPct As Decimal = 0D

        Public ReadOnly Property CurrentPnLPct As Decimal
            Get
                If EntryPrice <= 0 Then Return 0D
                Dim cp = TradeManager.I.GetPosition(Code)?.CurrentPrice
                If cp Is Nothing OrElse cp <= 0 Then Return 0D
                Return (CDec(cp) / EntryPrice - 1D) * 100D
            End Get
        End Property
    End Class

End Class
