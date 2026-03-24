' ═══════════════════════════════════════════════════════════════
' SimTradeEngine.vb — 가변 로직 엔진 (데이터·신호·상태 전이)
' ═══════════════════════════════════════════════════════════════
' [v4.2] SimTradeForm.vb에서 분리.
'   Phase A/B/C 처리, 실시간 틱/캔들, 신호 평가, 주문 실행 등
'   자주 변경되는 비즈니스 로직이 이 파일에 집중됩니다.
'
' 불변 상수 → SimTradeConstants.vb
' UI 레이아웃 → SimTradeUI.vb
' Form 접착   → SimTradeForm.vb
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Concurrent
Imports MainApp.SimTrade
Imports [Shared]

Namespace SimTrade

    ''' <summary>
    ''' 모의매매 핵심 엔진. UI와 독립적으로 동작하며,
    ''' ISimTradeView를 통해 UI에 결과를 전달합니다.
    ''' </summary>
    Public Class SimTradeEngine

#Region "필드"

        ' ── v4.0 엔진 ──
        Private ReadOnly _settings As SimTradeSettings
        Private ReadOnly _stateManager As StateManager
        Private _candleBuilder As CandleBuilder
        Private _signalEvaluator As SignalEvaluator
        Private _filterEngine As FilterEngine
        Private _orderSimulator As OrderSimulator
        Private _adaptiveCalc As AdaptiveParamCalc

        ' ── View 참조 ──
        Private ReadOnly _view As ISimTradeView

        ' ── 런타임 플래그 (가변) ──
        Private _isRunning As Boolean = False
        Private _conditionName As String = ""
        Private _conditionIndex As Integer = -1
        Private _tradingActivated As Boolean = False
        Private _allDownloaded As Boolean = False
        Private _readyCount As Integer = 0

        ' ── 틱 쓰로틀링 (스레드 안전) ──
        Private ReadOnly _lastTickTime As New ConcurrentDictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _last7CondLogTime As New ConcurrentDictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)

        ' ── 틱 진단 ──
        Private _tickDiagCount As Integer = 0

#End Region

#Region "속성"

        Public ReadOnly Property IsRunning As Boolean
            Get
                Return _isRunning
            End Get
        End Property

        Public ReadOnly Property Settings As SimTradeSettings
            Get
                Return _settings
            End Get
        End Property

        Public ReadOnly Property Manager As StateManager
            Get
                Return _stateManager
            End Get
        End Property

        Public ReadOnly Property Simulator As OrderSimulator
            Get
                Return _orderSimulator
            End Get
        End Property

        Public Property ConditionName As String
            Get
                Return _conditionName
            End Get
            Set(value As String)
                _conditionName = value
                _settings.ConditionName = value
            End Set
        End Property

        Public Property ConditionIndex As Integer
            Get
                Return _conditionIndex
            End Get
            Set(value As Integer)
                _conditionIndex = value
                _settings.ConditionIndex = value
            End Set
        End Property

#End Region

#Region "생성자"

        Public Sub New(settings As SimTradeSettings, view As ISimTradeView)
            _settings = settings
            _view = view
            _stateManager = New StateManager()
            InitializeEngines()
        End Sub

#End Region

#Region "엔진 초기화 (설정 적용 후 재호출 가능)"

        ''' <summary>v4.0 엔진 (재)초기화</summary>
        Public Sub InitializeEngines()
            ' 기존 핸들러 제거 (재초기화 시)
            If _candleBuilder IsNot Nothing Then
                RemoveHandler _candleBuilder.CandleCompleted, AddressOf OnCandleCompleted
                RemoveHandler _candleBuilder.CandleForceClosedOnPhaseChange, AddressOf OnPhaseChanged
            End If
            If _orderSimulator IsNot Nothing Then
                RemoveHandler _orderSimulator.OrderExecuted, AddressOf OnSimOrderExecuted
                RemoveHandler _orderSimulator.TradeCompleted, AddressOf OnSimTradeCompleted
                RemoveHandler _orderSimulator.OrderFailed, AddressOf OnSimOrderFailed
            End If

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

#End Region

#Region "시작 / 중지"

        ''' <summary>시뮬레이션 시작 — MessageBus 구독</summary>
        Public Sub Start()
            If _isRunning Then Return
            _isRunning = True
            _tradingActivated = False
            _allDownloaded = False
            _readyCount = 0
            _tickDiagCount = 0

            _candleBuilder.Clear()
            _stateManager.Clear()
            _lastTickTime.Clear()
            _last7CondLogTime.Clear()

            ' MessageBus 구독
            MessageBus.I.On(Topics.TICK, AddressOf OnTick)
            MessageBus.I.On(Topics.ORDERBOOK, AddressOf OnOrderBook)
            MessageBus.I.On(Topics.CONDITION_HIT, AddressOf OnConditionHit)
            MessageBus.I.On(Topics.TRADE_ORDER_FILLED, AddressOf OnOrderFilled)
            MessageBus.I.On(Topics.TRADE_POSITION_UPDATED, AddressOf OnPositionUpdated)
            MessageBus.I.On(Topics.CONDITION_SEARCH_RESULT, AddressOf OnConditionSearchResult)
            MessageBus.I.On(Topics.CANDLE_LOADED, AddressOf OnCandleDownloaded)

            _view.Log($"▶ 모의매매 시작 (v4.2) — 조건식: {_conditionName}")

            ' 비동기 초기 로드
            Threading.ThreadPool.QueueUserWorkItem(
                Sub()
                    Try
                        Dim existingItems = StockInfoManager.I.GetBySource(DataSourceType.조건검색)
                        If existingItems IsNot Nothing AndAlso existingItems.Count > 0 Then
                            _view.Log($"기존 조건검색 종목 {existingItems.Count}건 로드")
                            For Each item In existingItems
                                _view.SafeUI(Sub() AddWatchItem(item.Code))
                                Threading.Thread.Sleep(50)
                            Next
                        End If
                        Threading.Thread.Sleep(200)
                        MessageBus.I.Emit(Topics.CONDITION_START,
                                          "name", _conditionName,
                                          "index", _conditionIndex,
                                          "realtime", If(_settings.UseRealtimeCondition, 1, 0))
                    Catch ex As Exception
                        _view.Log($"[오류] Start 비동기 처리 실패: {ex.Message}")
                    End Try
                    Threading.Thread.Sleep(500)
                    EmitBulkSubscription()
                End Sub)
        End Sub

        ''' <summary>시뮬레이션 중지 — MessageBus 해제</summary>
        Public Sub [Stop]()
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

            _view.Log("■ 모의매매 중지")
            _view.Log($"═══ 최종 통계: {_orderSimulator.GetStatsSummary()} ═══")
        End Sub

        Private Sub EmitBulkSubscription()
            Dim codes = GetAllWatchCodes()
            If codes.Count > 0 Then
                MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE,
                                  "codes", String.Join(";", codes))
                _view.Log($"실시간 일괄 구독: {String.Join(";", codes)}")
            End If
        End Sub

        Public Function GetAllWatchCodes() As List(Of String)
            Return _stateManager.GetSnapshot().Select(Function(s) s.Code).ToList()
        End Function

#End Region

#Region "조건검색 수신"

        Private Sub OnConditionSearchResult(m As Msg)
            If Not m.Bool("success") Then
                _view.Log($"[오류] 조건검색 실패: {m.Str("message")}")
                If _stateManager.TotalCount = 0 Then
                    Dim fb = StockInfoManager.I.GetBySource(DataSourceType.조건검색)
                    If fb IsNot Nothing AndAlso fb.Count > 0 Then
                        _view.Log($"[폴백] StockInfoManager {fb.Count}종목")
                        For Each it In fb : AddWatchItem(it.Code) : Next
                    End If
                End If
                Return
            End If
            Dim codes = TryCast(m("codes"), String())
            If codes Is Nothing OrElse codes.Length = 0 Then
                _view.Log("조건검색 결과: 0건") : Return
            End If
            _view.Log($"조건검색 초기 결과: {codes.Length}건 — {String.Join(",", codes.Take(5))}...")
            For Each code In codes : AddWatchItem(code) : Next
        End Sub

        Private Sub OnConditionHit(m As Msg)
            Dim code = m.Str("code"), hitType = m.Str("type")
            If hitType = "I" Then
                If AddWatchItem(code) Then _view.Log($"[편입] {code} — 실시간 편입")
            ElseIf hitType = "D" Then
                _view.Log($"[이탈] {code} — 무시 (감시 유지)")
            End If
        End Sub

#End Region

#Region "★ 종목 추가 — StateManager + CandleBuilder 연동"

        ''' <summary>종목 감시 추가. 캐시 캔들 있으면 즉시 Ready, 없으면 다운로드 요청.</summary>
        Public Function AddWatchItem(code As String) As Boolean
            If String.IsNullOrEmpty(code) Then Return False
            If _stateManager.GetState(code) IsNot Nothing Then Return False
            If _stateManager.TotalCount >= SimTradeConst.MAX_WATCH_STOCKS Then Return False

            Dim si = StockInfoManager.I.GetItem(code)
            Dim name = If(si IsNot Nothing, si.Name, "")

            Dim state = _stateManager.AddStock(code, name)
            RegisterIndicators(state.Engine)

            Threading.ThreadPool.QueueUserWorkItem(
                Sub()
                    Try
                        ' ★ 틱 타임스탬프 로드 (메인 차트와 동일 경로)
                        Dim tickCount = LoadTickBarsFromCybos(code, state.Engine)
                        If tickCount > 0 Then
                            _view.Log($"[틱캔들] {code} {name} — {tickCount}개 타임스탬프 로드")
                        End If

                        Dim cached = StockInfoManager.I.GetCachedCandleItems(code)
                        If cached IsNot Nothing AndAlso cached.Count > 0 Then
                            SyncLock state.Candles
                                For Each c In cached
                                    state.Candles.Add(c)
                                Next
                            End SyncLock

                            _candleBuilder.InitializeFromHistory(code, state.Candles)
                            state.Engine.CalculateAll(state.Candles)

                            ' ★ 디버그: TickIntensity 계산 결과 확인
                            Dim dbgTi = SimTradeHelper.FindResult(state.Engine.Results, "TICKINT_")
                            If dbgTi IsNot Nothing AndAlso dbgTi.Count > 0 Then
                                Dim lastTi = dbgTi(dbgTi.Count - 1)
                                _view.Log($"[디버그] {code} TickIntensity 결과 {dbgTi.Count}건, 마지막 TickSum={lastTi.Val("TickSum"):F1}, MA5={lastTi.Val("MA5"):F1}")
                            Else
                                _view.Log($"[디버그] {code} TickIntensity 결과 없음")
                            End If



                            UpdateStateIndicators(state)
                            ComputeReferenceCandle(state)

                            _stateManager.TransitionTo(code, DataState.Ready)
                            Threading.Interlocked.Increment(_readyCount)
                            _view.Log($"[감시추가] {code} {name} — 캐시캔들 {state.Candles.Count}개 → Ready (총 {_stateManager.TotalCount}종목)")
                        Else
                            _stateManager.TransitionTo(code, DataState.Downloading)
                            StockInfoManager.I.AddStocks({code}, DataSourceType.조건검색, "SimTrade")
                            _view.Log($"[감시추가] {code} {name} — 캔들 요청 (총 {_stateManager.TotalCount}종목)")
                        End If

                        Threading.Thread.Sleep(50)
                        MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", code)
                    Catch ex As Exception
                        _view.Log($"[감시추가오류] {code}: {ex.Message}")
                    End Try
                End Sub)

            Return True
        End Function


#End Region

#Region "지표 등록"

        ''' <summary>지표 등록 (기존 5종 + MACD + JMA)</summary>
        Private Sub RegisterIndicators(engine As IndicatorEngine)
            engine.Register(New SuperTrend_Indicator(_settings.ST_Period, _settings.ST_Multiplier))
            engine.Register(New RSI_Indicator(_settings.RSI_Period))
            engine.Register(New Volume_Indicator())
            engine.Register(New OBV_Indicator())
            engine.Register(New TickIntensity_Indicator())
            Try
                engine.Register(New MACD_Indicator(_settings.MACD_Fast, _settings.MACD_Slow, _settings.MACD_Signal))
            Catch ex As Exception
                _view.Log($"[경고] MACD 지표 등록 실패: {ex.Message}")
            End Try
            Try
                engine.Register(New JMA_Indicator(_settings.JMA_Period, _settings.JMA_Phase, _settings.JMA_Power))
            Catch ex As Exception
                _view.Log($"[경고] JMA 지표 등록 실패: {ex.Message}")
            End Try
        End Sub

#End Region

#Region "★ 캔들 다운로드 완료"

        Private Sub OnCandleDownloaded(m As Msg)
            Dim code = m.Str("code")
            If String.IsNullOrEmpty(code) Then Return
            Dim state = _stateManager.GetState(code)
            If state Is Nothing Then Return
            If state.State >= DataState.Ready Then Return

            Dim rows = m.DictList("rows")
            If rows Is Nothing OrElse rows.Count = 0 Then
                _view.Log($"[캔들수신] {code} — 없음")
                Return
            End If

            Dim downloaded As New List(Of CandleItem)()
            For Each row In rows
                Try
                    Dim c As New CandleItem()

                    Dim dateStr = ""
                    Dim timeStr = ""
                    Dim dtStr = ""
                    If row.ContainsKey("date") Then dateStr = row("date").Trim()
                    If row.ContainsKey("time") Then timeStr = row("time").Trim()
                    If row.ContainsKey("dt") Then dtStr = row("dt").Trim()

                    If dateStr <> "" AndAlso timeStr <> "" Then
                        Dim combined = dateStr & timeStr.PadLeft(4, "0"c)
                        If Not DateTime.TryParseExact(combined, "yyyyMMddHHmm",
                            Globalization.CultureInfo.InvariantCulture,
                            Globalization.DateTimeStyles.None, c.Dt) Then
                            DateTime.TryParseExact(combined, "yyyyMMddHHmmss",
                                Globalization.CultureInfo.InvariantCulture,
                                Globalization.DateTimeStyles.None, c.Dt)
                        End If
                    ElseIf dtStr <> "" Then
                        If Not DateTime.TryParse(dtStr, c.Dt) Then
                            If Not DateTime.TryParseExact(dtStr, "yyyyMMddHHmmss",
                                Globalization.CultureInfo.InvariantCulture,
                                Globalization.DateTimeStyles.None, c.Dt) Then
                                DateTime.TryParseExact(dtStr, "yyyyMMddHHmm",
                                    Globalization.CultureInfo.InvariantCulture,
                                    Globalization.DateTimeStyles.None, c.Dt)
                            End If
                        End If
                    End If

                    If row.ContainsKey("open") Then Single.TryParse(row("open"), c.Open)
                    If row.ContainsKey("high") Then Single.TryParse(row("high"), c.High)
                    If row.ContainsKey("low") Then Single.TryParse(row("low"), c.Low)
                    If row.ContainsKey("close") Then Single.TryParse(row("close"), c.Close)
                    If row.ContainsKey("volume") Then Long.TryParse(row("volume"), c.Volume)
                    downloaded.Add(c)
                Catch ex As Exception
                    _view.Log($"[캔들파싱오류] {code}: {ex.Message}")
                End Try
            Next
            If downloaded.Count = 0 Then Return
            downloaded.Sort(Function(a, b) a.Dt.CompareTo(b.Dt))

            Threading.ThreadPool.QueueUserWorkItem(
                Sub()
                    Try
                        ' ★ 틱 타임스탬프 로드 (메인 차트와 동일 경로)
                        Dim tickCount = LoadTickBarsFromCybos(code, state.Engine)
                        If tickCount > 0 Then
                            _view.Log($"[틱캔들] {code} {state.Name} — {tickCount}개 타임스탬프 로드")
                        End If

                        SyncLock state.Candles
                            Dim existing = New List(Of CandleItem)(state.Candles)
                            state.Candles.Clear()
                            state.Candles.AddRange(downloaded)
                            state.Candles.AddRange(existing)
                            While state.Candles.Count > SimTradeConst.MAX_CANDLES
                                state.Candles.RemoveAt(0)
                            End While
                        End SyncLock

                        _candleBuilder.InitializeFromHistory(code, state.Candles)
                        state.Engine.CalculateAll(state.Candles)
                        UpdateStateIndicators(state)
                        ComputeReferenceCandle(state)

                        If state.Name = "" Then
                            Dim sn = StockInfoManager.I.GetItem(code)
                            If sn IsNot Nothing Then state.Name = sn.Name
                        End If

                        _stateManager.TransitionTo(code, DataState.Ready)
                        Threading.Interlocked.Increment(_readyCount)
                        _view.Log($"[캔들수신] {code} {state.Name} — {downloaded.Count}개 (총 {state.Candles.Count}개) → Ready")
                    Catch ex As Exception
                        _view.Log($"[캔들처리오류] {code}: {ex.Message}")
                    End Try
                End Sub)
        End Sub


#End Region

#Region "★ 틱 수신 → CandleBuilder → 신호"

        Private Sub OnTick(m As Msg)
            If Not _isRunning Then Return
            Dim code = m.Str("code")
            Dim state = _stateManager.GetState(code)
            If state Is Nothing Then Return

            ' ★ 쓰로틀링 (ConcurrentDictionary — 스레드 안전)
            Dim now = DateTime.Now
            Dim lastTime As DateTime = DateTime.MinValue
            If _lastTickTime.TryGetValue(code, lastTime) Then
                If (now - lastTime).TotalMilliseconds < SimTradeConst.TICK_THROTTLE_MS Then Return
            End If
            _lastTickTime(code) = now

            Dim price = Math.Abs(CInt(m.Dbl("price")))
            Dim vol = CLng(Math.Abs(m.Dbl("volume")))
            If price <= 0 Then Return

            ' ★ 틱 진단 (처음 N건)
            Dim diagNum = Threading.Interlocked.Increment(_tickDiagCount)
            If diagNum <= SimTradeConst.TICK_DIAG_COUNT Then
                _view.Log($"[틱진단#{diagNum}] {code} price={price} state={state.State} candles={state.Candles.Count}")
            End If

            ' 시세 갱신
            Dim ask = Math.Abs(CInt(m.Dbl("ask1")))
            Dim bid = Math.Abs(CInt(m.Dbl("bid1")))
            Dim cumVol = CLng(Math.Abs(m.Dbl("cumVolume")))
            Dim changeRate = m.Dbl("changeRate")
            _stateManager.UpdatePrice(code, price, cumVol,
                If(ask > 0, ask, state.Ask1), If(bid > 0, bid, state.Bid1), changeRate)

            state.Strength = m.Dbl("strength")
            If state.Name = "" Then
                Dim si = StockInfoManager.I.GetItem(code)
                If si IsNot Nothing Then state.Name = si.Name
            End If

            ' ★ CandleBuilder + 증분 지표 (SyncLock 보호)
            SyncLock state.Candles
                _candleBuilder.OnTick(code, price, vol, now, state.Candles)
                If state.Candles.Count > 0 Then
                    Try
                        state.Engine.UpdateLast(state.Candles)
                    Catch ex As Exception
                        _view.Log($"[증분지표오류] {code}: {ex.Message}")
                    End Try
                End If
            End SyncLock
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

#End Region

#Region "★ 캔들 완성 → 지표 전체 계산 + 신호"

        Private Sub OnCandleCompleted(code As String, candle As CandleItem, candles As List(Of CandleItem))
            Dim state = _stateManager.GetState(code)
            If state Is Nothing Then Return

            ' ★ v4.1 수정: >= 조건 (첫 도달 이후에도 주기적 CalculateAll 허용)
            If candles.Count >= _settings.MinCandlesForSignal Then
                Try
                    SyncLock state.Candles
                        state.Engine.CalculateAll(state.Candles)
                    End SyncLock
                Catch ex As Exception
                    _view.Log($"[지표계산오류] {code}: {ex.Message}")
                End Try
            End If

            UpdateStateIndicators(state)

            If candle.NormalizedTickSum > state.DayMaxTickSum Then
                state.DayMaxTickSum = candle.NormalizedTickSum
            End If

            If state.Candles.Count >= _settings.MinCandlesForSignal Then
                Try
                    _view.SafeUI(Sub() EvaluateSignal(state))
                Catch ex As Exception
                    _view.Log($"[신호평가오류] {code}: {ex.Message}")
                End Try
            Else
                state.LastSignal = $"캔들수집중({state.Candles.Count}/{_settings.MinCandlesForSignal})"
            End If
        End Sub

        Private Sub OnPhaseChanged(code As String, oldSec As Integer, newSec As Integer)
            _view.Log($"[구간전환] {code} — {oldSec}초→{newSec}초")
        End Sub

#End Region

#Region "★ 신호 판단 — 7조건"

        ''' <summary>매수/매도 신호 평가 (가변 로직의 핵심)</summary>
        Public Sub EvaluateSignal(state As StockState)
            Dim now = DateTime.Now.TimeOfDay

            If now < _settings.TradingStartTime Then
                state.LastSignal = "시간전" : Return
            End If

            ' ── 보유 중 → 매도 판단 ──
            If state.HasPosition Then
                ' P8: 장마감 강제청산
                If now >= _settings.ForceCloseTime Then
                    Dim forceResult As New SellSignalResult()
                    forceResult.ShouldSell = True
                    forceResult.Priority = "P8"
                    forceResult.Reason = $"장마감강제청산(수익{state.CurrentPnLRate:F1}%)"
                    _orderSimulator.ExecuteSell(state, forceResult)
                    state.LastSignal = forceResult.Reason
                    _view.Log($"[매도-P8] {state.Code} {state.Name} — {forceResult.Reason}")
                    Return
                End If

                ' P0~P7 판단
                Dim sellResult = _signalEvaluator.EvaluateSell(state)
                state.LastSignal = sellResult.Reason
                If sellResult.ShouldSell Then
                    _orderSimulator.ExecuteSell(state, sellResult)
                    _view.Log($"[매도-{sellResult.Priority}] {state.Code} {state.Name} — {sellResult.Reason}")
                End If
                Return
            End If

            ' ── 매수 금지 시간 ──
            If now >= _settings.NoNewBuyAfter Then
                state.LastSignal = "매수금지시간" : Return
            End If

            ' ── 필터 ──
            Dim filterResult = _filterEngine.Evaluate(state)
            If Not filterResult.Passed Then
                state.LastSignal = $"필터차단:{filterResult.BlockedBy}"
                Return
            End If
            For Each warn In filterResult.ObserveWarnings
                _view.Log($"[필터경고] {state.Code} {warn.FilterId}: {warn.Detail}")
            Next

            ' ── 7조건 매수 판단 ──
            Dim holdingCount = TradeManager.I.PositionCount
            Dim cash = TradeManager.I.AvailableCash
            Dim equity = cash + TradeManager.I.TotalEvalAmount

            Dim buyResult = _signalEvaluator.EvaluateBuy(state, holdingCount, cash, equity)
            state.LastSignal = buyResult.Reason

            Log7ConditionDetail(state, buyResult)

            If buyResult.ShouldBuy Then
                _orderSimulator.ExecuteBuy(state, buyResult)
                _view.Log($"[매수-{buyResult.Profile}] {state.Code} {state.Name} " &
                          $"{buyResult.SuggestedQty}주 @{buyResult.SuggestedPrice:N0} — {buyResult.Reason}")
            End If
        End Sub

        ''' <summary>7조건 상세 로그 (가변 — 조건 구성 변경 시 수정)</summary>
        Private Sub Log7ConditionDetail(state As StockState, result As BuySignalResult)
            If state.HasPosition Then Return

            ' 불필요한 상태면 스킵
            Dim reason = result.Reason
            If reason.StartsWith("시간전") OrElse reason.StartsWith("매수금지") OrElse
               reason.StartsWith("쿨다운") OrElse reason.StartsWith("캔들수집중") OrElse
               reason.StartsWith("최대종목") OrElse reason.StartsWith("보유중") OrElse
               reason.StartsWith("제외") Then
                Return
            End If

            ' ★ 종목당 N초에 1회 (ConcurrentDictionary)
            Dim now = DateTime.Now
            Dim lastLog As DateTime = DateTime.MinValue
            If _last7CondLogTime.TryGetValue(state.Code, lastLog) Then
                If (now - lastLog).TotalSeconds < SimTradeConst.LOG_7COND_THROTTLE_SEC Then Return
            End If
            _last7CondLogTime(state.Code) = now

            ' ★ 최소 충족 기준
            If result.ConditionsMet < SimTradeConst.MIN_CONDITIONS_FOR_LOG Then Return

            Dim met = result.ConditionsMet
            _view.Log($"[7조건] {state.Code} {state.Name} [{met}/7]{SimTradeHelper.CondGrade(met)} " &
                $"ST{SimTradeHelper.CondIcon(result.C1_ST)} " &
                $"JMA{SimTradeHelper.CondIcon(result.C2_JMA)} " &
                $"Tick{SimTradeHelper.CondIcon(result.C3_TickSum)} " &
                $"OBV{SimTradeHelper.CondIcon(result.C4_OBV)} " &
                $"동시{SimTradeHelper.CondIcon(result.C5_Confirm)} " &
                $"MACD{SimTradeHelper.CondIcon(result.C6_MACD)} " &
                $"Vol{SimTradeHelper.CondIcon(result.C7_Volume)}")

            If result.RejectReasons.Count > 0 AndAlso met < 7 Then
                _view.Log($"  → {String.Join(", ", result.RejectReasons.Take(3))}")
            End If
        End Sub

#End Region

#Region "StockState ← IndicatorEngine 동기화"

        ''' <summary>지표 최신값 → StockState 반영 (가변 — 지표 추가 시 수정)</summary>
        ''' <summary>지표 최신값 → StockState 반영 (가변 — 지표 추가 시 수정)</summary>
        Public Sub UpdateStateIndicators(state As StockState)
            Dim results = state.Engine?.Results
            If results Is Nothing Then Return

            Dim idx = state.Candles.Count - 1
            If idx < 1 Then Return

            ' ST
            Dim stList = SimTradeHelper.FindResult(results, "ST_")
            If stList IsNot Nothing AndAlso stList.Count > idx Then
                state.ST_Direction = stList(idx).Val("Direction")
            End If

            ' JMA (Up/Down NaN 패턴으로 Direction 판정)
            Dim jmaList = SimTradeHelper.FindResult(results, "JMA_")
            If jmaList IsNot Nothing AndAlso jmaList.Count > idx Then
                ' 현재 봉 Direction
                Dim curUp = jmaList(idx).Val("Up")
                Dim curDown = jmaList(idx).Val("Down")
                If Not Single.IsNaN(curUp) AndAlso Single.IsNaN(curDown) Then
                    state.JMA_Direction = 1
                ElseIf Single.IsNaN(curUp) AndAlso Not Single.IsNaN(curDown) Then
                    state.JMA_Direction = -1
                ElseIf Not Single.IsNaN(curUp) AndAlso Not Single.IsNaN(curDown) Then
                    state.JMA_Direction = 1   ' 전환점
                Else
                    state.JMA_Direction = 0
                End If

                ' 이전 봉 Direction
                If idx > 0 AndAlso jmaList.Count > idx - 1 Then
                    Dim prevUp = jmaList(idx - 1).Val("Up")
                    Dim prevDown = jmaList(idx - 1).Val("Down")
                    If Not Single.IsNaN(prevUp) AndAlso Single.IsNaN(prevDown) Then
                        state.JMA_PrevDirection = 1
                    ElseIf Single.IsNaN(prevUp) AndAlso Not Single.IsNaN(prevDown) Then
                        state.JMA_PrevDirection = -1
                    ElseIf Not Single.IsNaN(prevUp) AndAlso Not Single.IsNaN(prevDown) Then
                        state.JMA_PrevDirection = 1
                    Else
                        state.JMA_PrevDirection = 0
                    End If
                End If
            End If

            ' TickIntensity
            Dim tiList = SimTradeHelper.FindResult(results, "TICKINT_")
            If tiList IsNot Nothing AndAlso tiList.Count > idx Then
                Dim rawTickSum = tiList(idx).Val("TickSum")
                Dim rawMA5 = tiList(idx).Val("MA5")
                Dim rawMA20 = tiList(idx).Val("MA20")

                Dim intervalSec = 60
                If idx < state.Candles.Count AndAlso state.Candles(idx).IntervalSec > 0 Then
                    intervalSec = state.Candles(idx).IntervalSec
                Else
                    intervalSec = _candleBuilder.GetCurrentIntervalSec()
                End If

                state.TickSum_Normalized = SimTradeHelper.NormalizeTickSum(rawTickSum, intervalSec)
                state.TickMA5_Normalized = SimTradeHelper.NormalizeTickSum(rawMA5, intervalSec)
                state.TickMA20_Normalized = SimTradeHelper.NormalizeTickSum(rawMA20, intervalSec)
            End If

            ' TickBar 카운트 저장
            Dim tickInd2 = state.Engine.GetAll().OfType(Of TickIntensity_Indicator)().FirstOrDefault()
            If tickInd2 IsNot Nothing Then
                state.TickBarCount = tickInd2.TickBarCount
            End If


            ' OBV
            Dim obvList = SimTradeHelper.FindResult(results, "OBV_")
            If obvList IsNot Nothing AndAlso obvList.Count > idx Then
                state.OBV_Direction = obvList(idx).Val("Direction")
            End If

            ' RSI
            Dim rsiList = SimTradeHelper.FindResult(results, "RSI_")
            If rsiList IsNot Nothing AndAlso rsiList.Count > idx Then
                state.RSI_Value = rsiList(idx).Val("Value")
            End If

            ' MACD
            Dim macdList = SimTradeHelper.FindResult(results, "MACD_")
            If macdList IsNot Nothing AndAlso macdList.Count > idx Then
                state.MACD_Histogram = macdList(idx).Val("Histogram")
            End If

            ' Volume
            Dim volList = SimTradeHelper.FindResult(results, "VOL_")
            If volList IsNot Nothing AndAlso volList.Count > idx Then
                state.Volume_Ratio = volList(idx).Val("Ratio")
            End If

            ' StateManager 지표 갱신
            _stateManager.UpdateIndicators(state.Code,
                state.ST_Direction, state.JMA_Direction, state.JMA_PrevDirection,
                state.TickSum_Normalized, state.TickMA5_Normalized, state.TickMA20_Normalized,
                state.OBV_Direction, state.RSI_Value,
                state.MACD_Histogram, state.Volume_Ratio)
        End Sub


#End Region

#Region "체결/포지션 이벤트"

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

            _view.Log($"[체결] {side} {code} {filledQty}주 @{filledPrice:N0} [{m.Str("status")}]")
        End Sub

        Private Sub OnPositionUpdated(m As Msg)
            ' StateManager가 이미 관리
        End Sub

        Private Sub OnSimOrderExecuted(order As SimOrder)
            ' 로그는 EvaluateSignal에서 이미 출력
        End Sub

        Private Sub OnSimTradeCompleted(record As TradeRecord)
            _view.Log($"[매매완결] {record.Code} {record.Name} 순손익={record.NetProfit:N0}({record.NetProfitRate:F2}%) " &
                $"사유={record.SellReason} 비용={record.TotalCost:N0} 보유={record.HoldingBars}봉")
            _view.Log($"  ▸ 누적: {_orderSimulator.GetStatsSummary()}")
            _view.AddHistoryRow(record)
        End Sub

        Private Sub OnSimOrderFailed(code As String, reason As String)
            _view.Log($"[주문실패] {code} — {reason}")
        End Sub

#End Region

#Region "헬퍼"

        ''' <summary>기준봉 산출 (가변 — 기준봉 로직 변경 시 수정)</summary>
        Private Sub ComputeReferenceCandle(state As StockState)
            If Not _settings.TICKINT_UseReferenceCandle Then Return
            Dim rc = _adaptiveCalc.CalcReferenceCandle(state.Candles)
            If rc.IsValid Then
                state.HasReferenceCandle = True
                state.ReferenceCandleHigh = rc.High
                state.ReferenceCandleTickSum = rc.TickSum
                state.ReferenceCandleVolume = rc.Volume
                state.ReferenceCandleDate = rc.CandleDate
            End If
        End Sub

        ''' <summary>현재 설정 로그 출력</summary>
        Public Sub LogCurrentSettings()
            _view.Log("─── 현재 설정 (v4.2) ───")
            _view.Log($"  SuperTrend: Period={_settings.ST_Period}, Multiplier={_settings.ST_Multiplier:F1}")
            _view.Log($"  RSI: Period={_settings.RSI_Period}, 모멘텀하한={_settings.RSI_MomentumLower:F0}, 과매수={_settings.RSI_OverboughtLimit:F0}")
            _view.Log($"  MACD: {_settings.MACD_Fast}/{_settings.MACD_Slow}/{_settings.MACD_Signal}, AllPositive={_settings.MACD_RequireAllPositive}")
            _view.Log($"  TickIntensity: 임계={_settings.TICKINT_Threshold:F1}, 정규화={_settings.TICKINT_NormalizeToMinute}, 기준봉={_settings.TICKINT_UseReferenceCandle}")
            _view.Log($"  포지션: 최대={_settings.MaxPositionCount}종목, 비중={_settings.PositionSizeRate * 100:F0}%")
            _view.Log($"  손절={_settings.StopLossRate:F1}%, 익절={_settings.TakeProfitRate:F1}%, 트레일링={_settings.TrailingStopRate:F1}%(강화={_settings.TightenedTrailingRate:F1}%)")
            _view.Log($"  GracePeriod: {_settings.GracePeriod_Bars}봉, 악화{_settings.GracePeriod_ExitConditions}개시 청산")
            _view.Log($"  캔들: 개장={_settings.CandleInterval_Open}초→초반={_settings.CandleInterval_EarlyMorning}초→정상={_settings.CandleInterval_Normal}초")
            _view.Log($"  프로파일: {_settings.ActiveProfileMode}, Adaptive={_settings.AdaptiveMode}")
            _view.Log("──────────────")
        End Sub

        ''' <summary>
        ''' cybos API로 당일 틱 타임스탬프를 요청하여 TickIntensity_Indicator.SetTickBars에 전달.
        ''' 메인 차트(FastChartControl)와 동일한 데이터 경로를 사용.
        ''' 백그라운드 스레드에서 호출 가능 (DoEvents 미사용).
        ''' </summary>
        Public Shared Function LoadTickBarsFromCybos(code As String, engine As IndicatorEngine, Optional timeoutMs As Integer = 15000) As Integer
            Dim tickResponse As Msg = Nothing
            Dim tickCompleted As Boolean = False

            Dim handler As Action(Of Msg) =
                Sub(m As Msg)
                    If m Is Nothing Then Return
                    If Not String.Equals(SharedUtil.NormalizeChartCode(m.Str("code")),
                                         SharedUtil.NormalizeChartCode(code),
                                         StringComparison.OrdinalIgnoreCase) Then Return
                    tickResponse = m.Clone()
                    tickCompleted = True
                End Sub

            MessageBus.I.On(Topics.TICK_CANDLE_LOADED, handler)
            Try
                MessageBus.I.Emit(Topics.TICK_CANDLE_REQUEST,
                                  "code", code,
                                  "provider", "cybos",
                                  "count", 5000,
                                  "tickUnit", RuntimeChartSettings.DefaultTickUnit,
                                  "timeframe", RuntimeChartSettings.TickTimeframe(RuntimeChartSettings.DefaultTickUnit))

                Dim sw = Environment.TickCount
                While Not tickCompleted AndAlso Environment.TickCount - sw < timeoutMs
                    Threading.Thread.Sleep(50)
                End While

                If Not tickCompleted OrElse tickResponse Is Nothing Then Return 0

                Dim rows = tickResponse.DictList("rows")
                If rows Is Nothing OrElse rows.Count = 0 Then Return 0

                Dim tickBars As New List(Of DateTime)(rows.Count)
                For Each row In rows
                    Dim dtVal = ""
                    Dim tmVal = ""
                    If row.ContainsKey("dt") Then dtVal = row("dt")
                    If row.ContainsKey("date") Then dtVal = row("date")
                    If row.ContainsKey("time") Then tmVal = row("time")
                    Dim parsed = DateTime.MinValue
                    If dtVal <> "" AndAlso tmVal <> "" Then
                        Dim combined = dtVal & tmVal.PadLeft(6, "0"c)
                        DateTime.TryParseExact(combined, "yyyyMMddHHmmss",
                            Globalization.CultureInfo.InvariantCulture,
                            Globalization.DateTimeStyles.None, parsed)
                    ElseIf dtVal <> "" Then
                        DateTime.TryParse(dtVal, parsed)
                    End If
                    If parsed <> DateTime.MinValue Then tickBars.Add(parsed)
                Next

                If tickBars.Count = 0 Then Return 0
                tickBars.Sort()

                Dim tickInd = engine.GetAll().OfType(Of TickIntensity_Indicator)().FirstOrDefault()
                If tickInd IsNot Nothing Then
                    tickInd.SetTickBars(tickBars)
                    Return tickBars.Count
                End If
                Return 0
            Finally
                MessageBus.I.Off(Topics.TICK_CANDLE_LOADED, handler)
            End Try
        End Function

#End Region

    End Class

End Namespace