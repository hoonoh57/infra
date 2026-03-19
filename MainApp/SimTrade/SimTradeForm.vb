' ═══════════════════════════════════════════════════════════════
' SimTradeForm.vb — 모의매매 전용 폼 (v4.0 리팩토링)
' ═══════════════════════════════════════════════════════════════
' ★ 원칙서 v4.0 전체 적용:
'   - CandleBuilder (동적 캔들 전환)
'   - SignalEvaluator (7조건 AND 매수 / P0~P8 청산)
'   - FilterEngine (6종 위험 필터)
'   - OrderSimulator (주문 실행 / 비용 / 통계)
'   - AdaptiveParamCalc (적응형 파라미터)
'   - StateManager (종목 상태 중앙 관리)
'
' ★ 키움 모의매매 서버에 실제 주문 (지정가/시장가)
' ★ 캔들 다운로드: StockInfoManager → Cybos 일괄 고속
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports MainApp.SimTrade
Imports [Shared]

Public Class SimTradeForm
    Inherits Form

    ' ─── v4.0 엔진 ───
    Private ReadOnly _settings As New SimTradeSettings()
    Private ReadOnly _stateManager As New StateManager()
    Private _candleBuilder As CandleBuilder
    Private _signalEvaluator As SignalEvaluator
    Private _filterEngine As FilterEngine
    Private _orderSimulator As OrderSimulator
    Private _adaptiveCalc As AdaptiveParamCalc

    ' ─── 상태 ───
    Private _isRunning As Boolean = False
    Private _conditionName As String = ""
    Private _conditionIndex As Integer = -1

    ' ─── 틱 쓰로틀링 ───
    Private ReadOnly _lastTickTime As New Dictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)

    ' ─── UI ───
    Private WithEvents _tmrRefresh As New Timer()
    Private _dgvWatch As DataGridView
    Private _dgvPositions As DataGridView
    Private _dgvHistory As DataGridView
    Private _rtbLog As RichTextBox
    Private _lblStatus As Label
    Private _lblSummary As Label
    Private _btnCondition As Button
    Private _btnStart As Button
    Private _btnStop As Button
    Private _tabControl As TabControl
    Private _pnlSettings As Panel

    ' ─── 설정 UI 컨트롤 ───
    Private _nudST_Period As NumericUpDown
    Private _nudST_Multiplier As NumericUpDown
    Private _nudRSI_Period As NumericUpDown
    Private _nudRSI_Overbought As NumericUpDown
    Private _chkVolumeConfirm As CheckBox
    Private _nudMaxPosition As NumericUpDown
    Private _nudPositionSize As NumericUpDown
    Private _nudStopLoss As NumericUpDown
    Private _nudTakeProfit As NumericUpDown
    Private _nudTrailingStop As NumericUpDown
    Private _chkTrailingStop As CheckBox
    Private _nudCandleInterval As NumericUpDown
    Private _nudMinCandles As NumericUpDown
    Private _txtStartTime As TextBox
    Private _txtNoNewBuy As TextBox
    Private _txtForceClose As TextBox
    Private _cboBuyOrder As ComboBox
    Private _cboSellOrder As ComboBox


    ' ═══════════════════════════════════════
    ' 생성/소멸
    ' ═══════════════════════════════════════

    Public Sub New()
        InitializeEngines()
        InitializeUI()
        _tmrRefresh.Interval = 1000
    End Sub

    ''' <summary>v4.0 엔진 초기화</summary>
    Private Sub InitializeEngines()
        _candleBuilder = New CandleBuilder(_settings)
        _signalEvaluator = New SignalEvaluator(_settings)
        _filterEngine = New FilterEngine(_settings)
        _orderSimulator = New OrderSimulator(_settings, _stateManager)
        _adaptiveCalc = New AdaptiveParamCalc(_settings)

        ' 캔들 완성 이벤트 → 지표 계산 + 신호 판단
        AddHandler _candleBuilder.CandleCompleted, AddressOf OnCandleCompleted
        AddHandler _candleBuilder.CandleForceClosedOnPhaseChange, AddressOf OnPhaseChanged

        ' 주문 이벤트 로깅
        AddHandler _orderSimulator.OrderExecuted, AddressOf OnSimOrderExecuted
        AddHandler _orderSimulator.TradeCompleted, AddressOf OnSimTradeCompleted
        AddHandler _orderSimulator.OrderFailed, AddressOf OnSimOrderFailed
    End Sub

    Private Sub SimTradeForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Log("모의매매 폼 로드 (v4.0). 조건식을 선택한 뒤 [시작]을 누르세요.")
        Log("★ 엔진: CandleBuilder + SignalEvaluator(7조건) + FilterEngine(6종) + OrderSimulator")
        Log($"★ 캔들 간격: 개장={_settings.CandleInterval_Open}초, 초반={_settings.CandleInterval_EarlyMorning}초, 정상={_settings.CandleInterval_Normal}초")
    End Sub

    Private Sub SimTradeForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        StopSim()
    End Sub


    ' ═══════════════════════════════════════
    ' 조건검색
    ' ═══════════════════════════════════════

    Private Sub OnConditionClick(sender As Object, e As EventArgs)
        Dim dlg As New ConditionSelectDialog()
        If dlg.ShowDialog(Me) = DialogResult.OK Then
            _conditionName = dlg.SelectedConditionName
            _conditionIndex = dlg.SelectedConditionIndex
            _settings.ConditionName = _conditionName
            _settings.ConditionIndex = _conditionIndex
            Log($"조건식 선택: [{_conditionIndex}] {_conditionName}")
            _lblStatus.Text = $"조건식: {_conditionName} | 대기 중"
        End If
    End Sub


    ' ═══════════════════════════════════════
    ' 시작 / 중지
    ' ═══════════════════════════════════════

    Private Sub OnStartClick(sender As Object, e As EventArgs)
        If _conditionIndex < 0 Then
            MessageBox.Show("먼저 조건식을 선택하세요.")
            Return
        End If
        ApplySettingsFromUI()
        StartSim()
    End Sub

    Private Sub OnStopClick(sender As Object, e As EventArgs)
        StopSim()
    End Sub

    Private Sub StartSim()
        If _isRunning Then Return
        _isRunning = True
        _btnStart.Enabled = False
        _btnStop.Enabled = True
        _btnCondition.Enabled = False
        SetSettingsEnabled(False)
        LogCurrentSettings()

        ' ── v4.0: 엔진 재초기화 ──
        _candleBuilder.Clear()
        _stateManager.Clear()

        MessageBus.I.On(Topics.TICK, AddressOf OnTick)
        MessageBus.I.On(Topics.ORDERBOOK, AddressOf OnOrderBook)
        MessageBus.I.On(Topics.CONDITION_HIT, AddressOf OnConditionHit)
        MessageBus.I.On(Topics.TRADE_ORDER_FILLED, AddressOf OnOrderFilled)
        MessageBus.I.On(Topics.TRADE_POSITION_UPDATED, AddressOf OnPositionUpdated)
        MessageBus.I.On(Topics.CONDITION_SEARCH_RESULT, AddressOf OnConditionSearchResult)
        MessageBus.I.On(Topics.CANDLE_LOADED, AddressOf OnCandleDownloaded)

        Log($"▶ 모의매매 시작 (v4.0) — 조건식: {_conditionName}")
        _lblStatus.Text = $"● 실행 중 | {_conditionName}"
        _lblStatus.ForeColor = Color.Lime
        _tmrRefresh.Start()

        Threading.ThreadPool.QueueUserWorkItem(
            Sub()
                Try
                    Dim existingItems = StockInfoManager.I.GetBySource(DataSourceType.조건검색)
                    If existingItems IsNot Nothing AndAlso existingItems.Count > 0 Then
                        Log($"기존 조건검색 종목 {existingItems.Count}건 로드")
                        For Each item In existingItems
                            SafeUI(Sub() AddWatchItem(item.Code))
                            Threading.Thread.Sleep(50)
                        Next
                    End If
                    Threading.Thread.Sleep(200)
                    MessageBus.I.Emit(Topics.CONDITION_START,
                                      "name", _conditionName,
                                      "index", _conditionIndex,
                                      "realtime", If(_settings.UseRealtimeCondition, 1, 0))
                Catch ex As Exception
                    Log($"[오류] StartSim 비동기 처리 실패: {ex.Message}")
                End Try
                Threading.Thread.Sleep(500)
                Dim watchCodes = ""
                SafeUI(Sub() watchCodes = String.Join(";", GetAllWatchCodes()))
                Threading.Thread.Sleep(100)
                If watchCodes <> "" Then
                    MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", watchCodes)
                    Log($"실시간 일괄 구독: {watchCodes}")
                End If
            End Sub)
    End Sub

    Private Sub StopSim()
        If Not _isRunning Then Return
        _isRunning = False

        MessageBus.I.Off(Topics.TICK, AddressOf OnTick)
        MessageBus.I.Off(Topics.ORDERBOOK, AddressOf OnOrderBook)
        MessageBus.I.Off(Topics.CONDITION_HIT, AddressOf OnConditionHit)
        MessageBus.I.Off(Topics.TRADE_ORDER_FILLED, AddressOf OnOrderFilled)
        MessageBus.I.Off(Topics.TRADE_POSITION_UPDATED, AddressOf OnPositionUpdated)
        MessageBus.I.Off(Topics.CONDITION_SEARCH_RESULT, AddressOf OnConditionSearchResult)
        MessageBus.I.Off(Topics.CANDLE_LOADED, AddressOf OnCandleDownloaded)

        If _conditionIndex >= 0 Then
            MessageBus.I.Emit(Topics.CONDITION_STOP,
                              "name", _conditionName, "index", _conditionIndex)
        End If
        Dim codes = GetAllWatchCodes()
        If codes.Count > 0 Then
            MessageBus.I.Emit(Topics.REALTIME_UNSUBSCRIBE,
                              "codes", String.Join(";", codes))
        End If

        _tmrRefresh.Stop()
        _btnStart.Enabled = True
        _btnStop.Enabled = False
        _btnCondition.Enabled = True
        SetSettingsEnabled(True)
        _lblStatus.Text = "■ 중지됨"
        _lblStatus.ForeColor = Color.Gray
        Log("■ 모의매매 중지")
        Log($"═══ 최종 통계: {_orderSimulator.GetStatsSummary()} ═══")
    End Sub

    Private Function GetAllWatchCodes() As List(Of String)
        Return _stateManager.GetSnapshot().Select(Function(s) s.Code).ToList()
    End Function

    ' ═══════════════════════════════════════
    ' 조건검색 결과 수신
    ' ═══════════════════════════════════════

    Private Sub OnConditionSearchResult(m As Msg)
        If Not m.Bool("success") Then
            Log($"[오류] 조건검색 실패: {m.Str("message")}")
            If _stateManager.TotalCount = 0 Then
                Dim fb = StockInfoManager.I.GetBySource(DataSourceType.조건검색)
                If fb IsNot Nothing AndAlso fb.Count > 0 Then
                    Log($"[폴백] StockInfoManager {fb.Count}종목")
                    For Each it In fb : AddWatchItem(it.Code) : Next
                End If
            End If
            Return
        End If
        Dim codes = TryCast(m("codes"), String())
        If codes Is Nothing OrElse codes.Length = 0 Then
            Log("조건검색 결과: 0건") : Return
        End If
        Log($"조건검색 초기 결과: {codes.Length}건 — {String.Join(",", codes.Take(5))}...")
        For Each code In codes : AddWatchItem(code) : Next
    End Sub

    Private Sub OnConditionHit(m As Msg)
        Dim code = m.Str("code"), hitType = m.Str("type")
        If hitType = "I" Then
            If AddWatchItem(code) Then Log($"[편입] {code} — 실시간 편입")
        ElseIf hitType = "D" Then
            Log($"[이탈] {code} — 무시 (감시 유지)")
        End If
    End Sub


    ' ═══════════════════════════════════════
    ' ★ 종목 추가 — StateManager + CandleBuilder 연동
    ' ═══════════════════════════════════════

    Private Function AddWatchItem(code As String) As Boolean
        If String.IsNullOrEmpty(code) Then Return False
        If _stateManager.GetState(code) IsNot Nothing Then Return False
        If _stateManager.TotalCount >= 50 Then Return False

        Dim si = StockInfoManager.I.GetItem(code)
        Dim name = If(si IsNot Nothing, si.Name, "")

        Dim state = _stateManager.AddStock(code, name)

        ' 지표 엔진 등록
        RegisterIndicators(state.Engine)

        ' 캐시 캔들 로드
        Dim cached = StockInfoManager.I.GetCachedCandleItems(code)
        If cached IsNot Nothing AndAlso cached.Count > 0 Then
            For Each c In cached : state.Candles.Add(c) : Next
            _candleBuilder.InitializeFromHistory(code, state.Candles)
            state.Engine.CalculateAll(state.Candles)
            UpdateStateIndicators(state)

            ' Adaptive: 기준봉 산출
            If _settings.TICKINT_UseReferenceCandle Then
                Dim rc = _adaptiveCalc.CalcReferenceCandle(state.Candles)
                If rc.IsValid Then
                    state.HasReferenceCandle = True
                    state.ReferenceCandleHigh = rc.High
                    state.ReferenceCandleTickSum = rc.TickSum
                    state.ReferenceCandleVolume = rc.Volume
                    state.ReferenceCandleDate = rc.CandleDate
                End If
            End If

            _stateManager.TransitionTo(code, DataState.Ready)
            Log($"[감시추가] {code} {name} — 캐시캔들 {state.Candles.Count}개 (총 {_stateManager.TotalCount}종목)")
        Else
            _stateManager.TransitionTo(code, DataState.Downloading)
            StockInfoManager.I.AddStock(code, DataSourceType.조건검색, "SimTrade")
            Log($"[감시추가] {code} {name} — Cybos 캔들 요청 (총 {_stateManager.TotalCount}종목)")
        End If

        Threading.ThreadPool.QueueUserWorkItem(
            Sub()
                Threading.Thread.Sleep(100)
                MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", code)
            End Sub)
        Return True
    End Function


    ' ═══════════════════════════════════════
    ' 지표 등록 (기존 5종 + MACD 추가)
    ' ═══════════════════════════════════════

    Private Sub RegisterIndicators(engine As IndicatorEngine)
        engine.Register(New SuperTrend_Indicator(_settings.ST_Period, _settings.ST_Multiplier))
        engine.Register(New RSI_Indicator(_settings.RSI_Period))
        engine.Register(New Volume_Indicator())
        engine.Register(New OBV_Indicator())
        engine.Register(New TickIntensity_Indicator())
        ' v4.0: MACD 추가
        Try
            engine.Register(New MACD_Indicator(_settings.MACD_Fast, _settings.MACD_Slow, _settings.MACD_Signal))
        Catch
            ' MACD_Indicator 미존재 시 무시 — 기존 빌드 호환
        End Try
        ' v4.0: JMA 추가
        Try
            engine.Register(New JMA_Indicator(_settings.JMA_Period, _settings.JMA_Phase, _settings.JMA_Power))
        Catch
            ' JMA_Indicator 미존재 시 무시
        End Try
    End Sub

    ''' <summary>지표 결과에서 접두사로 찾기</summary>
    Private Shared Function FindResult(results As Dictionary(Of String, List(Of IndicatorResult)),
                                       prefix As String) As List(Of IndicatorResult)
        If results Is Nothing Then Return Nothing
        Dim key = results.Keys.FirstOrDefault(Function(k) k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        If key Is Nothing Then Return Nothing
        Dim list As List(Of IndicatorResult) = Nothing
        results.TryGetValue(key, list)
        Return list
    End Function


    ' ═══════════════════════════════════════
    ' ★ 캔들 다운로드 완료
    ' ═══════════════════════════════════════

    Private Sub OnCandleDownloaded(m As Msg)
        Dim code = m.Str("code")
        If String.IsNullOrEmpty(code) Then Return
        Dim state = _stateManager.GetState(code)
        If state Is Nothing Then Return
        If state.Candles.Count >= _settings.MinCandlesForSignal Then Return

        Dim rows = m.DictList("rows")
        If rows Is Nothing OrElse rows.Count = 0 Then
            Log($"[캔들수신] {code} — 없음") : Return
        End If

        Dim downloaded As New List(Of CandleItem)()
        For Each row In rows
            Try
                Dim c As New CandleItem()
                Dim dtStr = "" : If row.ContainsKey("dt") Then dtStr = row("dt")
                If dtStr <> "" Then DateTime.TryParse(dtStr, c.Dt)
                If row.ContainsKey("open") Then Single.TryParse(row("open"), c.Open)
                If row.ContainsKey("high") Then Single.TryParse(row("high"), c.High)
                If row.ContainsKey("low") Then Single.TryParse(row("low"), c.Low)
                If row.ContainsKey("close") Then Single.TryParse(row("close"), c.Close)
                If row.ContainsKey("volume") Then Long.TryParse(row("volume"), c.Volume)
                downloaded.Add(c)
            Catch : End Try
        Next
        If downloaded.Count = 0 Then Return
        downloaded.Sort(Function(a, b) a.Dt.CompareTo(b.Dt))

        SafeUI(
            Sub()
                Dim existing = New List(Of CandleItem)(state.Candles)
                state.Candles.Clear()
                state.Candles.AddRange(downloaded)
                state.Candles.AddRange(existing)
                While state.Candles.Count > 500 : state.Candles.RemoveAt(0) : End While

                _candleBuilder.InitializeFromHistory(code, state.Candles)
                state.Engine.CalculateAll(state.Candles)
                UpdateStateIndicators(state)

                ' 기준봉 산출
                If _settings.TICKINT_UseReferenceCandle Then
                    Dim rc = _adaptiveCalc.CalcReferenceCandle(state.Candles)
                    If rc.IsValid Then
                        state.HasReferenceCandle = True
                        state.ReferenceCandleHigh = rc.High
                        state.ReferenceCandleTickSum = rc.TickSum
                        state.ReferenceCandleVolume = rc.Volume
                        state.ReferenceCandleDate = rc.CandleDate
                    End If
                End If

                If state.Name = "" Then
                    Dim sn = StockInfoManager.I.GetItem(code)
                    If sn IsNot Nothing Then state.Name = sn.Name
                End If

                _stateManager.TransitionTo(code, DataState.Ready)
                Log($"[캔들수신] {code} {state.Name} — {downloaded.Count}개 (총 {state.Candles.Count}개)")
            End Sub)
    End Sub


    ' ═══════════════════════════════════════
    ' ★ 틱 수신 → CandleBuilder → 신호 판단
    ' ═══════════════════════════════════════

    Private Sub OnTick(m As Msg)
        If Not _isRunning Then Return
        Dim code = m.Str("code")
        Dim state = _stateManager.GetState(code)
        If state Is Nothing Then Return

        Dim now = DateTime.Now
        If _lastTickTime.ContainsKey(code) AndAlso (now - _lastTickTime(code)).TotalMilliseconds < 200 Then Return
        _lastTickTime(code) = now

        Dim price = Math.Abs(CInt(m.Dbl("price")))
        Dim vol = CLng(Math.Abs(m.Dbl("volume")))
        If price <= 0 Then Return

        ' 시세 업데이트
        Dim ask = Math.Abs(CInt(m.Dbl("ask1")))
        Dim bid = Math.Abs(CInt(m.Dbl("bid1")))
        Dim cumVol = CLng(Math.Abs(m.Dbl("cumVolume")))
        Dim changeRate = m.Dbl("changeRate")
        _stateManager.UpdatePrice(code, price, cumVol, If(ask > 0, ask, state.Ask1), If(bid > 0, bid, state.Bid1), changeRate)

        state.Strength = m.Dbl("strength")
        If state.Name = "" Then
            Dim si = StockInfoManager.I.GetItem(code)
            If si IsNot Nothing Then state.Name = si.Name
        End If

        ' ★ CandleBuilder에 위임 (캔들 완성 시 이벤트 발화)
        _candleBuilder.OnTick(code, price, vol, now, state.Candles)

        ' 진행 중 캔들 업데이트 (증분 지표)
        If state.Candles.Count > 0 Then
            state.Engine.UpdateLast(state.Candles)
        End If
    End Sub

    Private Sub OnOrderBook(m As Msg)
        Dim code = m.Str("code")
        Dim state = _stateManager.GetState(code)
        If state Is Nothing Then Return
        Dim ap = TryCast(m("askPrices"), Double())
        Dim bp = TryCast(m("bidPrices"), Double())
        If ap IsNot Nothing AndAlso ap.Length > 0 Then state.Ask1 = CInt(Math.Abs(ap(0)))
        If bp IsNot Nothing AndAlso bp.Length > 0 Then state.Bid1 = CInt(Math.Abs(bp(0)))
    End Sub


    ' ═══════════════════════════════════════
    ' ★ 캔들 완성 이벤트 → 지표 전체계산 + 신호판단
    ' ═══════════════════════════════════════

    Private Sub OnCandleCompleted(code As String, candle As CandleItem, candles As List(Of CandleItem))
        Dim state = _stateManager.GetState(code)
        If state Is Nothing Then Return

        ' 전체 지표 재계산
        state.Engine.CalculateAll(state.Candles)
        UpdateStateIndicators(state)

        ' DayMax TickSum 갱신
        If candle.NormalizedTickSum > state.DayMaxTickSum Then
            state.DayMaxTickSum = candle.NormalizedTickSum
        End If

        ' 신호 판단
        If state.Candles.Count >= _settings.MinCandlesForSignal Then
            SafeUI(Sub() EvaluateSignal(state))
        Else
            state.LastSignal = $"캔들수집중({state.Candles.Count}/{_settings.MinCandlesForSignal})"
        End If
    End Sub

    Private Sub OnPhaseChanged(code As String, oldSec As Integer, newSec As Integer)
        Log($"[구간전환] {code} — {oldSec}초→{newSec}초")
    End Sub


    ' ═══════════════════════════════════════
    ' ★ v4.0 신호 판단 — 엔진 위임
    ' ═══════════════════════════════════════

    Private Sub EvaluateSignal(state As StockState)
        Dim now = DateTime.Now.TimeOfDay

        If now < _settings.TradingStartTime Then
            state.LastSignal = "시간전" : Return
        End If

        ' ── 보유 중이면 매도 판단 ──
        If state.HasPosition Then
            ' P8: 장마감 강제청산
            If now >= _settings.ForceCloseTime Then
                Dim forceResult As New SellSignalResult() With {
                    .ShouldSell = True, .Priority = "P8",
                    .Reason = $"장마감강제청산(수익{state.CurrentPnLRate:F1}%)"}
                _orderSimulator.ExecuteSell(state, forceResult)
                state.LastSignal = forceResult.Reason
                Log($"[매도-P8] {state.Code} {state.Name} — {forceResult.Reason}")
                Return
            End If

            ' P0~P7 판단
            Dim sellResult = _signalEvaluator.EvaluateSell(state)
            state.LastSignal = sellResult.Reason

            If sellResult.ShouldSell Then
                _orderSimulator.ExecuteSell(state, sellResult)
                Log($"[매도-{sellResult.Priority}] {state.Code} {state.Name} — {sellResult.Reason}")
            End If
            Return
        End If

        ' ── 매수 금지 시간 ──
        If now >= _settings.NoNewBuyAfter Then
            state.LastSignal = "매수금지시간" : Return
        End If

        ' ── 필터 검사 ──
        Dim filterResult = _filterEngine.Evaluate(state)
        If Not filterResult.Passed Then
            state.LastSignal = $"필터차단:{filterResult.BlockedBy}"
            Return
        End If
        ' Observe 경고 로그
        For Each warn In filterResult.ObserveWarnings
            Log($"[필터경고] {state.Code} {warn.FilterId}: {warn.Detail}")
        Next

        ' ── 7조건 매수 판단 ──
        Dim holdingCount = TradeManager.I.PositionCount
        Dim cash = TradeManager.I.AvailableCash
        Dim equity = cash + TradeManager.I.TotalEvalAmount

        Dim buyResult = _signalEvaluator.EvaluateBuy(state, holdingCount, cash, equity)
        state.LastSignal = buyResult.Reason

        If buyResult.ShouldBuy Then
            _orderSimulator.ExecuteBuy(state, buyResult)
            Log($"[매수-{buyResult.Profile}] {state.Code} {state.Name} {buyResult.SuggestedQty}주 @{buyResult.SuggestedPrice:N0} — {buyResult.Reason}")
        End If
    End Sub


    ' ═══════════════════════════════════════
    ' StockState ← IndicatorEngine 동기화
    ' ═══════════════════════════════════════

    Private Sub UpdateStateIndicators(state As StockState)
        Dim results = state.Engine.Results
        Dim idx = state.Candles.Count - 1
        If idx < 1 Then Return

        ' ST
        Dim stList = FindResult(results, "ST_")
        If stList IsNot Nothing AndAlso stList.Count > idx Then
            state.ST_Direction = stList(idx).Val("Direction")
        End If

        ' JMA
        Dim jmaList = FindResult(results, "JMA_")
        If jmaList IsNot Nothing AndAlso jmaList.Count > idx Then
            state.JMA_Direction = jmaList(idx).Val("Direction")
            state.JMA_PrevDirection = If(idx > 0, jmaList(idx - 1).Val("Direction"), 0)
        End If

        ' TickIntensity
        Dim tiList = FindResult(results, "TICKINT_")
        If tiList IsNot Nothing AndAlso tiList.Count > idx Then
            Dim rawTickSum = tiList(idx).Val("TickSum")
            Dim intervalSec = _candleBuilder.GetCurrentIntervalSec()
            If intervalSec > 0 AndAlso Not Single.IsNaN(rawTickSum) Then
                state.TickSum_Normalized = rawTickSum * (60.0 / intervalSec)
            Else
                state.TickSum_Normalized = rawTickSum
            End If
            state.TickMA5_Normalized = tiList(idx).Val("MA5") * If(intervalSec > 0, 60.0 / intervalSec, 1)
            state.TickMA20_Normalized = tiList(idx).Val("MA20") * If(intervalSec > 0, 60.0 / intervalSec, 1)
        End If

        ' OBV
        Dim obvList = FindResult(results, "OBV_")
        If obvList IsNot Nothing AndAlso obvList.Count > idx Then
            state.OBV_Direction = obvList(idx).Val("Direction")
        End If

        ' RSI
        Dim rsiList = FindResult(results, "RSI_")
        If rsiList IsNot Nothing AndAlso rsiList.Count > idx Then
            state.RSI_Value = rsiList(idx).Val("Value")
        End If

        ' MACD
        Dim macdList = FindResult(results, "MACD_")
        If macdList IsNot Nothing AndAlso macdList.Count > idx Then
            state.MACD_Histogram = macdList(idx).Val("Histogram")
        End If

        ' Volume
        Dim volList = FindResult(results, "VOL_")
        If volList IsNot Nothing AndAlso volList.Count > idx Then
            state.Volume_Ratio = volList(idx).Val("Ratio")
        End If

        ' StateManager 지표 갱신 (JMA 전환 봉 추적 포함)
        _stateManager.UpdateIndicators(state.Code,
            state.ST_Direction, state.JMA_Direction, state.JMA_PrevDirection,
            state.TickSum_Normalized, state.TickMA5_Normalized, state.TickMA20_Normalized,
            state.OBV_Direction, state.RSI_Value,
            state.MACD_Histogram, state.Volume_Ratio)
    End Sub

    ' ═══════════════════════════════════════
    ' 체결/포지션 이벤트
    ' ═══════════════════════════════════════

    Private Sub OnOrderFilled(m As Msg)
        If m.Str("strategy") <> "SimTrade" Then Return
        Dim code = m.Str("code")
        Dim side = m.Str("side")
        Dim filledPrice = m.Int("filledPrice")
        Dim filledQty = m.Int("filledQty")

        If side.ToUpper().Contains("BUY") OrElse side.ToUpper().Contains("매수") Then
            _orderSimulator.OnBuyFilled(code, filledPrice, filledQty)
        Else
            _orderSimulator.OnSellFilled(code, filledPrice, filledQty)
        End If

        Log($"[체결] {side} {code} {filledQty}주 @{filledPrice:N0} [{m.Str("status")}]")
    End Sub

    Private Sub OnPositionUpdated(m As Msg)
        ' StateManager가 이미 관리
    End Sub

    ' ── OrderSimulator 이벤트 ──

    Private Sub OnSimOrderExecuted(order As SimOrder)
        ' 로그는 EvaluateSignal에서 이미 출력
    End Sub

    Private Sub OnSimTradeCompleted(record As TradeRecord)
        Log($"[매매완결] {record.Code} {record.Name} 순손익={record.NetProfit:N0}({record.NetProfitRate:F2}%) " &
            $"사유={record.SellReason} 비용={record.TotalCost:N0} 보유={record.HoldingBars}봉")

        ' 통계 업데이트 로그
        Log($"  ▸ 누적: {_orderSimulator.GetStatsSummary()}")
    End Sub

    Private Sub OnSimOrderFailed(code As String, reason As String)
        Log($"[주문실패] {code} — {reason}")
    End Sub


    ' ═══════════════════════════════════════
    ' 설정 UI ↔ SimTradeSettings 동기화
    ' ═══════════════════════════════════════

    Private Sub ApplySettingsFromUI()
        _settings.ST_Period = CInt(_nudST_Period.Value)
        _settings.ST_Multiplier = CDbl(_nudST_Multiplier.Value)
        _settings.RSI_Period = CInt(_nudRSI_Period.Value)
        _settings.RSI_OverboughtLimit = CDbl(_nudRSI_Overbought.Value)
        _settings.RequireVolumeConfirm = _chkVolumeConfirm.Checked
        _settings.MaxPositionCount = CInt(_nudMaxPosition.Value)
        _settings.PositionSizeRate = CDbl(_nudPositionSize.Value) / 100.0
        _settings.StopLossRate = CDbl(_nudStopLoss.Value)
        _settings.TakeProfitRate = CDbl(_nudTakeProfit.Value)
        _settings.TrailingStopRate = CDbl(_nudTrailingStop.Value)
        _settings.EnableTrailingStop = _chkTrailingStop.Checked
        _settings.CandleIntervalSec = CInt(_nudCandleInterval.Value)
        _settings.MinCandlesForSignal = CInt(_nudMinCandles.Value)
        Dim ts As TimeSpan
        If TimeSpan.TryParse(_txtStartTime.Text.Trim(), ts) Then _settings.TradingStartTime = ts
        If TimeSpan.TryParse(_txtNoNewBuy.Text.Trim(), ts) Then _settings.NoNewBuyAfter = ts
        If TimeSpan.TryParse(_txtForceClose.Text.Trim(), ts) Then _settings.ForceCloseTime = ts
        _settings.BuyOrderType = CType(_cboBuyOrder.SelectedIndex, SimOrderType)
        _settings.SellOrderType = CType(_cboSellOrder.SelectedIndex, SimOrderType)

        ' v4.0 엔진 재생성 (파라미터 변경 반영)
        _candleBuilder = New CandleBuilder(_settings)
        _signalEvaluator = New SignalEvaluator(_settings)
        _filterEngine = New FilterEngine(_settings)
        _orderSimulator = New OrderSimulator(_settings, _stateManager)
        _adaptiveCalc = New AdaptiveParamCalc(_settings)
        AddHandler _candleBuilder.CandleCompleted, AddressOf OnCandleCompleted
        AddHandler _candleBuilder.CandleForceClosedOnPhaseChange, AddressOf OnPhaseChanged
        AddHandler _orderSimulator.OrderExecuted, AddressOf OnSimOrderExecuted
        AddHandler _orderSimulator.TradeCompleted, AddressOf OnSimTradeCompleted
        AddHandler _orderSimulator.OrderFailed, AddressOf OnSimOrderFailed
    End Sub

    Private Sub LoadSettingsToUI()
        _nudST_Period.Value = _settings.ST_Period
        _nudST_Multiplier.Value = CDec(_settings.ST_Multiplier)
        _nudRSI_Period.Value = _settings.RSI_Period
        _nudRSI_Overbought.Value = CDec(_settings.RSI_OverboughtLimit)
        _chkVolumeConfirm.Checked = _settings.RequireVolumeConfirm
        _nudMaxPosition.Value = _settings.MaxPositionCount
        _nudPositionSize.Value = CDec(_settings.PositionSizeRate * 100)
        _nudStopLoss.Value = CDec(_settings.StopLossRate)
        _nudTakeProfit.Value = CDec(_settings.TakeProfitRate)
        _nudTrailingStop.Value = CDec(_settings.TrailingStopRate)
        _chkTrailingStop.Checked = _settings.EnableTrailingStop
        _nudCandleInterval.Value = _settings.CandleIntervalSec
        _nudMinCandles.Value = _settings.MinCandlesForSignal
        _txtStartTime.Text = _settings.TradingStartTime.ToString("hh\:mm")
        _txtNoNewBuy.Text = _settings.NoNewBuyAfter.ToString("hh\:mm")
        _txtForceClose.Text = _settings.ForceCloseTime.ToString("hh\:mm")
        _cboBuyOrder.SelectedIndex = CInt(_settings.BuyOrderType)
        _cboSellOrder.SelectedIndex = CInt(_settings.SellOrderType)
    End Sub

    Private Sub SetSettingsEnabled(enabled As Boolean)
        If _pnlSettings Is Nothing Then Return
        For Each ctrl As Control In _pnlSettings.Controls
            If TypeOf ctrl Is NumericUpDown OrElse TypeOf ctrl Is TextBox OrElse
               TypeOf ctrl Is ComboBox OrElse TypeOf ctrl Is CheckBox Then
                ctrl.Enabled = enabled
            End If
        Next
    End Sub

    Private Sub LogCurrentSettings()
        Log("─── 현재 설정 (v4.0) ───")
        Log($"  SuperTrend: Period={_settings.ST_Period}, Multiplier={_settings.ST_Multiplier:F1}")
        Log($"  RSI: Period={_settings.RSI_Period}, 모멘텀하한={_settings.RSI_MomentumLower:F0}, 과매수={_settings.RSI_OverboughtLimit:F0}")
        Log($"  MACD: {_settings.MACD_Fast}/{_settings.MACD_Slow}/{_settings.MACD_Signal}, AllPositive={_settings.MACD_RequireAllPositive}")
        Log($"  TickIntensity: 임계={_settings.TICKINT_Threshold:F1}, 정규화={_settings.TICKINT_NormalizeToMinute}, 기준봉={_settings.TICKINT_UseReferenceCandle}")
        Log($"  포지션: 최대={_settings.MaxPositionCount}종목, 비중={_settings.PositionSizeRate * 100:F0}%")
        Log($"  손절={_settings.StopLossRate:F1}%, 익절={_settings.TakeProfitRate:F1}%, 트레일링={_settings.TrailingStopRate:F1}%(강화={_settings.TightenedTrailingRate:F1}%)")
        Log($"  GracePeriod: {_settings.GracePeriod_Bars}봉, 악화{_settings.GracePeriod_ExitConditions}개시 청산")
        Log($"  캔들: 개장={_settings.CandleInterval_Open}초→초반={_settings.CandleInterval_EarlyMorning}초→정상={_settings.CandleInterval_Normal}초")
        Log($"  프로파일: {_settings.ActiveProfileMode}, Adaptive={_settings.AdaptiveMode}")
        Log("──────────────")
    End Sub


    ' ═══════════════════════════════════════
    ' UI 갱신 — StateManager 스냅샷 기반
    ' ═══════════════════════════════════════

    Private Sub OnTimerRefresh(sender As Object, e As EventArgs) Handles _tmrRefresh.Tick
        If Not _isRunning Then Return
        RefreshWatchGrid()
        RefreshPositionGrid()
        RefreshSummary()
    End Sub

    Private Sub RefreshWatchGrid()
        Dim snapshots = _stateManager.GetSnapshot().OrderBy(Function(s) s.Code).ToList()
        If _dgvWatch.Rows.Count <> snapshots.Count Then
            _dgvWatch.SuspendLayout()
            _dgvWatch.Rows.Clear()
            For Each s In snapshots
                _dgvWatch.Rows.Add(s.Code, s.Name, s.CurrentPrice.ToString("N0"),
                    s.ChangeRate.ToString("F2") & "%", s.DayVolume.ToString("N0"),
                    $"ST{If(s.ST_Direction > 0, "+", "-")} JMA{If(s.JMA_Direction > 0, "↑", "↓")}",
                    s.State.ToString(), s.LastSignal)
            Next
            _dgvWatch.ResumeLayout()
        Else
            For i = 0 To snapshots.Count - 1
                Dim s = snapshots(i), row = _dgvWatch.Rows(i)
                row.Cells(0).Value = s.Code
                row.Cells(1).Value = s.Name
                row.Cells(2).Value = s.CurrentPrice.ToString("N0")
                row.Cells(3).Value = s.ChangeRate.ToString("F2") & "%"
                row.Cells(4).Value = s.DayVolume.ToString("N0")
                row.Cells(5).Value = $"ST{If(s.ST_Direction > 0, "+", "-")} JMA{If(s.JMA_Direction > 0, "↑", "↓")}"
                row.Cells(6).Value = s.State.ToString()
                row.Cells(7).Value = s.LastSignal
            Next
        End If
    End Sub

    Private Sub RefreshPositionGrid()
        _dgvPositions.SuspendLayout()
        _dgvPositions.Rows.Clear()
        For Each pos In TradeManager.I.GetPositions()
            _dgvPositions.Rows.Add(pos.Code, pos.Name, pos.Quantity,
                pos.AvgPrice.ToString("N0"), pos.CurrentPrice.ToString("N0"),
                pos.ProfitLoss.ToString("N0"), pos.ProfitRate.ToString("F2") & "%")
        Next
        _dgvPositions.ResumeLayout()
    End Sub

    Private Sub RefreshSummary()
        Dim cash = TradeManager.I.AvailableCash
        Dim eval = TradeManager.I.TotalEvalAmount
        Dim pnl = TradeManager.I.TotalProfitLoss
        Dim stats = _orderSimulator.GetStatsSummary()
        _lblSummary.Text = $"현금: {cash:N0} | 평가: {eval:N0} | " &
                           $"총자산: {cash + eval:N0} | 손익: {pnl:N0} | " &
                           $"감시: {_stateManager.TotalCount} | 보유: {TradeManager.I.PositionCount} | " &
                           $"{stats}"
    End Sub


    ' ═══════════════════════════════════════
    ' 로그
    ' ═══════════════════════════════════════

    Private Sub Log(text As String)
        Dim line = $"[{DateTime.Now:HH:mm:ss}] {text}"
        AppLogger.I.Trade(text, "SimTrade")
        If _rtbLog Is Nothing Then Return
        If _rtbLog.InvokeRequired Then
            _rtbLog.BeginInvoke(Sub() AppendLog(line))
        Else
            AppendLog(line)
        End If
    End Sub

    Private Sub AppendLog(line As String)
        _rtbLog.AppendText(line & Environment.NewLine)
        If _rtbLog.Lines.Length > 2000 Then
            _rtbLog.Text = String.Join(Environment.NewLine, _rtbLog.Lines.Skip(_rtbLog.Lines.Length - 1000))
        End If
        _rtbLog.SelectionStart = _rtbLog.Text.Length
        _rtbLog.ScrollToCaret()
    End Sub

    Private Sub SafeUI(action As Action)
        If Me.InvokeRequired Then
            Try : Me.Invoke(action) : Catch : End Try
        Else
            action()
        End If
    End Sub


    ' ═══════════════════════════════════════
    ' UI 구성 (기존 동일)
    ' ═══════════════════════════════════════

    Private Sub InitializeUI()
        Me.Text = "모의매매 v4.0 (7조건 AND / P0-P8 청산 / 동적캔들)"
        Me.Size = New Size(1200, 800)
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.BackColor = Color.FromArgb(25, 25, 35)
        Me.ForeColor = Color.White

        Dim pnlTop As New Panel With {.Dock = DockStyle.Top, .Height = 70,
            .BackColor = Color.FromArgb(30, 30, 45), .Padding = New Padding(10)}
        _lblStatus = New Label With {.Text = "대기 중", .Location = New Point(10, 8),
            .AutoSize = True, .Font = New Font("맑은 고딕", 11, FontStyle.Bold), .ForeColor = Color.Gray}
        _lblSummary = New Label With {.Text = "", .Location = New Point(10, 38),
            .AutoSize = True, .Font = New Font("맑은 고딕", 9), .ForeColor = Color.Silver}
        _btnCondition = MakeButton("조건식 선택", 820, 10, Color.FromArgb(60, 80, 120))
        AddHandler _btnCondition.Click, AddressOf OnConditionClick
        _btnStart = MakeButton("시작", 940, 10, Color.FromArgb(40, 100, 40))
        AddHandler _btnStart.Click, AddressOf OnStartClick
        _btnStop = MakeButton("중지", 1030, 10, Color.FromArgb(100, 40, 40))
        _btnStop.Enabled = False
        AddHandler _btnStop.Click, AddressOf OnStopClick
        pnlTop.Controls.AddRange({_lblStatus, _lblSummary, _btnCondition, _btnStart, _btnStop})

        _tabControl = New TabControl With {.Dock = DockStyle.Fill}

        Dim tabWatch As New TabPage("감시종목")
        _dgvWatch = MakeGrid({"코드", "종목명", "현재가", "등락률", "거래량", "지표", "상태", "신호"})
        tabWatch.Controls.Add(_dgvWatch) : _tabControl.TabPages.Add(tabWatch)

        Dim tabPos As New TabPage("보유종목")
        _dgvPositions = MakeGrid({"코드", "종목명", "수량", "매입가", "현재가", "손익", "수익률"})
        tabPos.Controls.Add(_dgvPositions) : _tabControl.TabPages.Add(tabPos)

        Dim tabHistory As New TabPage("매매이력")
        _dgvHistory = MakeGrid({"시간", "구분", "코드", "종목명", "수량", "가격", "손익", "사유"})
        tabHistory.Controls.Add(_dgvHistory) : _tabControl.TabPages.Add(tabHistory)

        Dim tabLog As New TabPage("로그")
        _rtbLog = New RichTextBox With {.Dock = DockStyle.Fill, .ReadOnly = True,
            .BackColor = Color.FromArgb(20, 20, 30), .ForeColor = Color.FromArgb(200, 200, 200),
            .Font = New Font("Consolas", 9), .BorderStyle = BorderStyle.None}
        tabLog.Controls.Add(_rtbLog) : _tabControl.TabPages.Add(tabLog)

        Dim tabSettings As New TabPage("설정")
        tabSettings.BackColor = Color.FromArgb(30, 30, 42)
        tabSettings.AutoScroll = True
        _pnlSettings = New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True,
            .BackColor = Color.FromArgb(30, 30, 42), .ForeColor = Color.White, .Padding = New Padding(15)}
        BuildSettingsPanel(_pnlSettings)
        tabSettings.Controls.Add(_pnlSettings) : _tabControl.TabPages.Add(tabSettings)

        Me.Controls.Add(_tabControl)
        Me.Controls.Add(pnlTop)
        LoadSettingsToUI()
    End Sub

    Private Sub BuildSettingsPanel(pnl As Panel)
        Dim y = 10
        Dim lf = New Font("맑은 고딕", 9)

        AddLabel(pnl, "SuperTrend Period:", 10, y, lf)
        _nudST_Period = AddNud(pnl, 200, y, 1, 50, 10) : y += 30
        AddLabel(pnl, "SuperTrend Multiplier:", 10, y, lf)
        _nudST_Multiplier = AddNud(pnl, 200, y, 1, 10, 3, 1) : y += 30
        AddLabel(pnl, "RSI Period:", 10, y, lf)
        _nudRSI_Period = AddNud(pnl, 200, y, 2, 50, 14) : y += 30
        AddLabel(pnl, "RSI 과매수:", 10, y, lf)
        _nudRSI_Overbought = AddNud(pnl, 200, y, 50, 95, 75) : y += 30
        _chkVolumeConfirm = New CheckBox With {.Text = "거래량 확인", .Location = New Point(10, y),
            .AutoSize = True, .ForeColor = Color.White, .Font = lf, .Checked = True}
        pnl.Controls.Add(_chkVolumeConfirm) : y += 30
        AddLabel(pnl, "최대 포지션:", 10, y, lf)
        _nudMaxPosition = AddNud(pnl, 200, y, 1, 20, 5) : y += 30
        AddLabel(pnl, "포지션 비중(%):", 10, y, lf)
        _nudPositionSize = AddNud(pnl, 200, y, 1, 50, 15) : y += 30
        AddLabel(pnl, "손절(%):", 10, y, lf)
        _nudStopLoss = AddNud(pnl, 200, y, -20, 0, -3, 1) : y += 30
        AddLabel(pnl, "익절(%):", 10, y, lf)
        _nudTakeProfit = AddNud(pnl, 200, y, 1, 30, 5) : y += 30
        AddLabel(pnl, "트레일링(%):", 10, y, lf)
        _nudTrailingStop = AddNud(pnl, 200, y, -10, 0, -1.5, 1) : y += 30
        _chkTrailingStop = New CheckBox With {.Text = "트레일링 스톱", .Location = New Point(10, y),
            .AutoSize = True, .ForeColor = Color.White, .Font = lf, .Checked = True}
        pnl.Controls.Add(_chkTrailingStop) : y += 30
        AddLabel(pnl, "캔들 간격(초):", 10, y, lf)
        _nudCandleInterval = AddNud(pnl, 200, y, 5, 300, 10) : y += 30
        AddLabel(pnl, "최소 캔들:", 10, y, lf)
        _nudMinCandles = AddNud(pnl, 200, y, 5, 200, 30) : y += 30
        AddLabel(pnl, "매매시작:", 10, y, lf)
        _txtStartTime = New TextBox With {.Location = New Point(200, y), .Width = 80, .Text = "09:05",
            .BackColor = Color.FromArgb(40, 40, 55), .ForeColor = Color.White, .Font = lf}
        pnl.Controls.Add(_txtStartTime) : y += 30
        AddLabel(pnl, "매수금지:", 10, y, lf)
        _txtNoNewBuy = New TextBox With {.Location = New Point(200, y), .Width = 80, .Text = "14:30",
            .BackColor = Color.FromArgb(40, 40, 55), .ForeColor = Color.White, .Font = lf}
        pnl.Controls.Add(_txtNoNewBuy) : y += 30
        AddLabel(pnl, "강제청산:", 10, y, lf)
        _txtForceClose = New TextBox With {.Location = New Point(200, y), .Width = 80, .Text = "15:15",
            .BackColor = Color.FromArgb(40, 40, 55), .ForeColor = Color.White, .Font = lf}
        pnl.Controls.Add(_txtForceClose) : y += 30
        AddLabel(pnl, "매수 주문:", 10, y, lf)
        _cboBuyOrder = New ComboBox With {.Location = New Point(200, y), .Width = 120, .DropDownStyle = ComboBoxStyle.DropDownList,
            .BackColor = Color.FromArgb(40, 40, 55), .ForeColor = Color.White, .Font = lf}
        _cboBuyOrder.Items.AddRange({"시장가", "최우선매도", "현재가"})
        _cboBuyOrder.SelectedIndex = 1
        pnl.Controls.Add(_cboBuyOrder) : y += 30
        AddLabel(pnl, "매도 주문:", 10, y, lf)
        _cboSellOrder = New ComboBox With {.Location = New Point(200, y), .Width = 120, .DropDownStyle = ComboBoxStyle.DropDownList,
            .BackColor = Color.FromArgb(40, 40, 55), .ForeColor = Color.White, .Font = lf}
        _cboSellOrder.Items.AddRange({"시장가", "최우선매수", "현재가"})
        _cboSellOrder.SelectedIndex = 0
        pnl.Controls.Add(_cboSellOrder)
    End Sub

    ' ─── UI 헬퍼 ───

    Private Function MakeButton(text As String, x As Integer, y As Integer, bgColor As Color) As Button
        Dim btn As New Button With {
            .Text = text, .Location = New Point(x, y), .Size = New Size(100, 30),
            .FlatStyle = FlatStyle.Flat, .BackColor = bgColor, .ForeColor = Color.White,
            .Font = New Font("맑은 고딕", 9, FontStyle.Bold)}
        Return btn
    End Function

    Private Function MakeGrid(columns As String()) As DataGridView
        Dim dgv As New DataGridView With {
            .Dock = DockStyle.Fill, .ReadOnly = True, .AllowUserToAddRows = False,
            .BackgroundColor = Color.FromArgb(25, 25, 35), .ForeColor = Color.White,
            .GridColor = Color.FromArgb(50, 50, 60), .BorderStyle = BorderStyle.None,
            .ColumnHeadersDefaultCellStyle = New DataGridViewCellStyle With {
                .BackColor = Color.FromArgb(35, 35, 50), .ForeColor = Color.White},
            .DefaultCellStyle = New DataGridViewCellStyle With {
                .BackColor = Color.FromArgb(25, 25, 35), .ForeColor = Color.White,
                .SelectionBackColor = Color.FromArgb(50, 50, 70)},
            .RowHeadersVisible = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect}
        For Each col In columns
            dgv.Columns.Add(col, col)
        Next
        Return dgv
    End Function

    Private Sub AddLabel(pnl As Panel, text As String, x As Integer, y As Integer, f As Font)
        pnl.Controls.Add(New Label With {.Text = text, .Location = New Point(x, y),
            .AutoSize = True, .ForeColor = Color.FromArgb(180, 180, 200), .Font = f})
    End Sub

    Private Function AddNud(pnl As Panel, x As Integer, y As Integer,
                             min As Decimal, max As Decimal, val As Decimal,
                             Optional dec As Integer = 0) As NumericUpDown
        Dim nud As New NumericUpDown With {
            .Location = New Point(x, y), .Width = 80,
            .Minimum = min, .Maximum = max, .Value = val, .DecimalPlaces = dec,
            .BackColor = Color.FromArgb(40, 40, 55), .ForeColor = Color.White,
            .Font = New Font("맑은 고딕", 9)}
        pnl.Controls.Add(nud)
        Return nud
    End Function

End Class
