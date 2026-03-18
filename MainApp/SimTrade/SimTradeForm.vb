' ═══════════════════════════════════════════════════════════════
' SimTradeForm.vb — 모의매매 전용 폼 (완전 격리)
' ═══════════════════════════════════════════════════════════════
' ★ 삭제: 이 파일 + SimTradeModels.vb 제거, vbproj 링크 제거,
'   메뉴 호출 1줄 제거 → MainApp 완전 무영향.
'
' ★ 키움 모의매매 서버에 실제 주문 (지정가/시장가만)
'   - TradeManager가 Chejan으로 체결/잔고 추적
'   - 이 폼은 "언제, 무엇을, 얼마나" 결정하는 신호 로직만 담당
'
' ★ 캔들 다운로드: StockInfoManager → Cybos 일괄 고속
'
' ★ 지표 키 매핑 (실제 IIndicator.Name):
'   SuperTrend → "ST_{period}_{mult}"  Values: Direction, Value
'   RSI        → "RSI_{period}"        Values: Value
'   Volume     → "VOL_{period}"        Values: Ratio (100%기준)
'   OBV        → "OBV_{period}"        Values: OBV, Direction
'   TickInt    → "TICKINT_{min}"        Values: TickSum, MA5, MA20
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports MainApp.SimTrade
Imports [Shared]

Public Class SimTradeForm
    Inherits Form

    ' ─── 종목 추적 ───
    Private ReadOnly _watchItems As New Dictionary(Of String, WatchItem)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _settings As New SimTradeSettings()

    ' ─── 상태 ───
    Private _isRunning As Boolean = False
    Private _conditionName As String = ""
    Private _conditionIndex As Integer = -1

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

    ' ─── 틱 쓰로틀링 ───
    Private ReadOnly _lastTickTime As New Dictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)


    ' ═══════════════════════════════════════
    ' 생성/소멸
    ' ═══════════════════════════════════════

    Public Sub New()
        InitializeUI()
        _tmrRefresh.Interval = 1000
    End Sub

    Private Sub SimTradeForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Log("모의매매 폼 로드. 조건식을 선택한 뒤 [시작]을 누르세요.")
        Log("★ 키움 모의매매 서버 주문 — 지정가/시장가만 사용")
        Log("★ 캔들: StockInfoManager → Cybos 일괄 고속 다운로드")
    End Sub

    Private Sub SimTradeForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        StopSim()
    End Sub


    ' ═══════════════════════════════════════
    ' 지표 등록
    ' ═══════════════════════════════════════

    Private Sub RegisterIndicators(engine As IndicatorEngine)
        engine.Register(New SuperTrend_Indicator(_settings.ST_Period, _settings.ST_Multiplier))
        engine.Register(New RSI_Indicator(_settings.RSI_Period))
        engine.Register(New Volume_Indicator())
        engine.Register(New OBV_Indicator())
        engine.Register(New TickIntensity_Indicator())
    End Sub

    ''' <summary>Results 딕셔너리에서 접두사로 지표 결과를 찾는 헬퍼</summary>
    Private Shared Function FindResult(results As Dictionary(Of String, List(Of IndicatorResult)),
                                       prefix As String) As List(Of IndicatorResult)
        Dim key = results.Keys.FirstOrDefault(Function(k) k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        If key Is Nothing Then Return Nothing
        Dim list As List(Of IndicatorResult) = Nothing
        results.TryGetValue(key, list)
        Return list
    End Function


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

        MessageBus.I.On(Topics.TICK, AddressOf OnTick)
        MessageBus.I.On(Topics.ORDERBOOK, AddressOf OnOrderBook)
        MessageBus.I.On(Topics.CONDITION_HIT, AddressOf OnConditionHit)
        MessageBus.I.On(Topics.TRADE_ORDER_FILLED, AddressOf OnOrderFilled)
        MessageBus.I.On(Topics.TRADE_POSITION_UPDATED, AddressOf OnPositionUpdated)
        MessageBus.I.On(Topics.CONDITION_SEARCH_RESULT, AddressOf OnConditionSearchResult)
        MessageBus.I.On(Topics.CANDLE_LOADED, AddressOf OnCandleDownloaded)

        Log($"▶ 모의매매 시작 — 조건식: {_conditionName}")
        _lblStatus.Text = $"● 실행 중 | {_conditionName}"
        _lblStatus.ForeColor = Color.Lime
        _tmrRefresh.Start()

        Threading.ThreadPool.QueueUserWorkItem(
            Sub()
                Try
                    Dim existingItems = StockInfoManager.I.GetBySource(DataSourceType.조건검색)
                    If existingItems IsNot Nothing AndAlso existingItems.Count > 0 Then
                        Log($"기존 조건검색 종목 {existingItems.Count}건 로드 (StockInfoManager)")
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
                SafeUI(Sub() watchCodes = String.Join(";", _watchItems.Keys))
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
        If _watchItems.Count > 0 Then
            MessageBus.I.Emit(Topics.REALTIME_UNSUBSCRIBE,
                              "codes", String.Join(";", _watchItems.Keys))
        End If

        _tmrRefresh.Stop()
        _btnStart.Enabled = True
        _btnStop.Enabled = False
        _btnCondition.Enabled = True
        SetSettingsEnabled(True)
        _lblStatus.Text = "■ 중지됨"
        _lblStatus.ForeColor = Color.Gray
        Log("■ 모의매매 중지")
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
        Log("─── 현재 설정 ───")
        Log($"  SuperTrend: Period={_settings.ST_Period}, Multiplier={_settings.ST_Multiplier:F1}")
        Log($"  RSI: Period={_settings.RSI_Period}, 과매수={_settings.RSI_OverboughtLimit:F0}")
        Log($"  거래량확인={_settings.RequireVolumeConfirm}")
        Log($"  포지션: 최대={_settings.MaxPositionCount}종목, 비중={_settings.PositionSizeRate * 100:F0}%")
        Log($"  손절={_settings.StopLossRate:F1}%, 익절={_settings.TakeProfitRate:F1}%, 트레일링={_settings.TrailingStopRate:F1}%(사용={_settings.EnableTrailingStop})")
        Log($"  캔들={_settings.CandleIntervalSec}초, 최소캔들={_settings.MinCandlesForSignal}")
        Log($"  시간: 시작={_settings.TradingStartTime:hh\:mm}, 매수금지={_settings.NoNewBuyAfter:hh\:mm}, 강제청산={_settings.ForceCloseTime:hh\:mm}")
        Log($"  주문: 매수={_settings.BuyOrderType}, 매도={_settings.SellOrderType}")
        Log("──────────────")
    End Sub


    ' ═══════════════════════════════════════
    ' 조건검색 결과 수신
    ' ═══════════════════════════════════════

    Private Sub OnConditionSearchResult(m As Msg)
        Log($"[DEBUG] CONDITION_SEARCH_RESULT — success={m.Bool("success")} msg={m.Str("message")}")
        If Not m.Bool("success") Then
            Log($"[오류] 조건검색 실패: {m.Str("message")}")
            If _watchItems.Count = 0 Then
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
    ' ★ 종목 추가 — StockInfoManager Cybos 일괄 캔들
    ' ═══════════════════════════════════════

    Private Function AddWatchItem(code As String) As Boolean
        If String.IsNullOrEmpty(code) Then Return False
        If _watchItems.ContainsKey(code) Then Return False
        If _watchItems.Count >= 50 Then Return False

        Dim item As New WatchItem With {.Code = code}
        RegisterIndicators(item.Engine)
        Dim si = StockInfoManager.I.GetItem(code)
        If si IsNot Nothing Then item.Name = si.Name
        item.IsSubscribed = True
        _watchItems(code) = item

        Dim cached = StockInfoManager.I.GetCachedCandleItems(code)
        If cached IsNot Nothing AndAlso cached.Count > 0 Then
            For Each c In cached : item.Candles.Add(c) : Next
            If item.Candles.Count > 0 Then item.CurrentCandleStart = item.Candles.Last().Dt
            item.Engine.CalculateAll(item.Candles)
            Log($"[감시추가] {code} {item.Name} — 캐시캔들 {item.Candles.Count}개 (총 {_watchItems.Count}종목)")
        Else
            StockInfoManager.I.AddStock(code, DataSourceType.조건검색, "SimTrade")
            Log($"[감시추가] {code} {item.Name} — Cybos 캔들 요청 (총 {_watchItems.Count}종목)")
        End If

        Threading.ThreadPool.QueueUserWorkItem(
            Sub()
                Threading.Thread.Sleep(100)
                MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", code)
            End Sub)
        Return True
    End Function


    ' ═══════════════════════════════════════
    ' ★ 캔들 다운로드 완료
    ' ═══════════════════════════════════════

    Private Sub OnCandleDownloaded(m As Msg)
        Dim code = m.Str("code")
        If String.IsNullOrEmpty(code) Then Return
        If Not _watchItems.ContainsKey(code) Then Return
        Dim item = _watchItems(code)
        If item.Candles.Count >= _settings.MinCandlesForSignal Then Return

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
                Dim existing = New List(Of CandleItem)(item.Candles)
                item.Candles.Clear()
                item.Candles.AddRange(downloaded)
                item.Candles.AddRange(existing)
                While item.Candles.Count > 500 : item.Candles.RemoveAt(0) : End While
                item.Engine.CalculateAll(item.Candles)
                If item.Candles.Count > 0 Then item.CurrentCandleStart = item.Candles.Last().Dt
                If item.Name = "" Then
                    Dim sn = StockInfoManager.I.GetItem(code)
                    If sn IsNot Nothing Then item.Name = sn.Name
                End If
                Log($"[캔들수신] {code} {item.Name} — {downloaded.Count}개 (총 {item.Candles.Count}개)")
            End Sub)
    End Sub


    ' ═══════════════════════════════════════
    ' 틱 → 캔들 빌딩 → 신호
    ' ═══════════════════════════════════════

    Private Sub OnTick(m As Msg)
        If Not _isRunning Then Return
        Dim code = m.Str("code")
        If Not _watchItems.ContainsKey(code) Then Return
        Dim now = DateTime.Now
        If _lastTickTime.ContainsKey(code) AndAlso (now - _lastTickTime(code)).TotalMilliseconds < 200 Then Return
        _lastTickTime(code) = now

        Dim item = _watchItems(code)
        Dim price = Math.Abs(CInt(m.Dbl("price")))
        Dim vol = CLng(Math.Abs(m.Dbl("volume")))
        If price <= 0 Then Return

        item.CurrentPrice = price
        Dim ask = Math.Abs(CInt(m.Dbl("ask1")))
        Dim bid = Math.Abs(CInt(m.Dbl("bid1")))
        If ask > 0 Then item.Ask1 = ask
        If bid > 0 Then item.Bid1 = bid
        item.Strength = m.Dbl("strength")
        item.ChangeRate = m.Dbl("changeRate")
        item.Volume = CLng(Math.Abs(m.Dbl("cumVolume")))
        If item.Name = "" Then
            Dim si = StockInfoManager.I.GetItem(code)
            If si IsNot Nothing Then item.Name = si.Name
        End If

        BuildCandle(item, price, vol, now)

        If item.Candles.Count >= _settings.MinCandlesForSignal Then
            EvaluateSignal(item)
        Else
            item.LastSignal = $"캔들수집중({item.Candles.Count}/{_settings.MinCandlesForSignal})"
        End If
    End Sub

    Private Sub OnOrderBook(m As Msg)
        Dim code = m.Str("code")
        If Not _watchItems.ContainsKey(code) Then Return
        Dim item = _watchItems(code)
        Dim ap = TryCast(m("askPrices"), Double())
        Dim bp = TryCast(m("bidPrices"), Double())
        If ap IsNot Nothing AndAlso ap.Length > 0 Then item.Ask1 = CInt(Math.Abs(ap(0)))
        If bp IsNot Nothing AndAlso bp.Length > 0 Then item.Bid1 = CInt(Math.Abs(bp(0)))
    End Sub

    Private Sub BuildCandle(item As WatchItem, price As Single, vol As Long, tickTime As DateTime)
        Dim interval = TimeSpan.FromSeconds(_settings.CandleIntervalSec)
        Dim candleStart = New DateTime(tickTime.Ticks - (tickTime.Ticks Mod interval.Ticks))
        If item.Candles.Count = 0 OrElse candleStart > item.CurrentCandleStart Then
            item.CurrentCandleStart = candleStart
            item.Candles.Add(CandleItem.Create(candleStart, price))
            If item.Candles.Count > 500 Then item.Candles.RemoveAt(0)
            item.Engine.CalculateAll(item.Candles)
        Else
            Dim last = item.Candles(item.Candles.Count - 1)
            last.UpdateFromTick(price, vol, tickTime)
            item.Engine.UpdateLast(item.Candles)
        End If
    End Sub


    ' ═══════════════════════════════════════
    ' ★ 신호 판단 — 지표 키 수정 완료
    '   ST  → prefix "ST_",  Direction(1/-1), Value
    '   RSI → prefix "RSI_", Value
    '   VOL → prefix "VOL_", Ratio (100%기준, 100 이상=평균 이상)
    ' ═══════════════════════════════════════

    Private Sub EvaluateSignal(item As WatchItem)
        Dim now = DateTime.Now.TimeOfDay
        Dim hasPos = TradeManager.I.HasPosition(item.Code)

        If now < _settings.TradingStartTime Then
            item.LastSignal = "시간전" : Return
        End If
        If now >= _settings.ForceCloseTime AndAlso hasPos Then
            DoSell(item, "장마감청산") : Return
        End If
        If hasPos Then
            EvaluateSell(item) : Return
        End If
        If now >= _settings.NoNewBuyAfter Then
            item.LastSignal = "매수금지시간" : Return
        End If
        EvaluateBuy(item)
    End Sub

    Private Sub EvaluateBuy(item As WatchItem)
        Dim results = item.Engine.Results
        Dim idx = item.Candles.Count - 1
        If idx < 2 Then
            item.LastSignal = $"캔들부족({idx + 1})" : Return
        End If

        ' ── SuperTrend (키: "ST_*", 값: Direction, Value) ──
        Dim stList = FindResult(results, "ST_")
        If stList Is Nothing Then
            item.LastSignal = "ST없음" : Return
        End If
        If stList.Count <= idx Then
            item.LastSignal = "ST미산출" : Return
        End If

        Dim dirNow = CInt(stList(idx).Val("Direction"))
        Dim dirPrev = CInt(stList(idx - 1).Val("Direction"))
        Dim stValue = stList(idx).Val("Value")

        Dim isCrossUp = (dirNow > 0 AndAlso dirPrev <= 0)
        Dim isAboveST = (dirNow > 0 AndAlso item.Candles(idx).Close > stValue)

        If Not (isCrossUp OrElse isAboveST) Then
            item.LastSignal = $"ST하락(D={dirNow})" : Return
        End If

        ' ── RSI (키: "RSI_*", 값: Value) ──
        Dim rsiList = FindResult(results, "RSI_")
        If rsiList IsNot Nothing AndAlso rsiList.Count > idx Then
            Dim rsiVal = rsiList(idx).Val("Value")
            If Not Single.IsNaN(rsiVal) AndAlso rsiVal > _settings.RSI_OverboughtLimit Then
                item.LastSignal = $"RSI과매수({rsiVal:F0})" : Return
            End If
        End If

        ' ── 거래량 (키: "VOL_*", 값: Ratio — 100%기준) ──
        If _settings.RequireVolumeConfirm Then
            Dim volList = FindResult(results, "VOL_")
            If volList IsNot Nothing AndAlso volList.Count > idx Then
                Dim ratio = volList(idx).Val("Ratio")
                If Not Single.IsNaN(ratio) AndAlso ratio < 100.0F Then
                    item.LastSignal = $"거래량부족({ratio:F0}%)" : Return
                End If
            End If
        End If

        ' ── 포지션 수 ──
        If TradeManager.I.PositionCount >= _settings.MaxPositionCount Then
            item.LastSignal = "최대종목초과" : Return
        End If

        ' ── 매수 수량 ──
        Dim cash = TradeManager.I.AvailableCash
        Dim equity = cash + TradeManager.I.TotalEvalAmount
        Dim maxAmt = CLng(equity * _settings.PositionSizeRate)
        If maxAmt > cash Then maxAmt = cash
        Dim price = GetBuyPrice(item)
        If price <= 0 Then Return
        Dim qty = CInt(maxAmt \ price)
        If qty <= 0 Then
            item.LastSignal = "매수금액부족" : Return
        End If

        item.LastSignal = "★매수신호!"
        DoBuy(item, price, qty, If(isCrossUp, "ST_CrossUp", "ST_Above"))
    End Sub

    Private Sub EvaluateSell(item As WatchItem)
        Dim pos = TradeManager.I.GetPosition(item.Code)
        If pos Is Nothing OrElse pos.Quantity <= 0 Then Return

        If item.CurrentPrice > item.HighSinceBuy Then item.HighSinceBuy = item.CurrentPrice
        Dim profitRate = pos.ProfitRate
        item.LastSignal = $"보유중({profitRate:+0.0;-0.0}%)"

        If profitRate <= _settings.StopLossRate Then
            DoSell(item, $"손절({profitRate:F1}%)") : Return
        End If
        If profitRate >= _settings.TakeProfitRate Then
            DoSell(item, $"익절({profitRate:F1}%)") : Return
        End If
        If _settings.EnableTrailingStop AndAlso item.HighSinceBuy > 0 Then
            Dim dd = (CDbl(item.CurrentPrice - item.HighSinceBuy) / item.HighSinceBuy) * 100
            If dd <= _settings.TrailingStopRate AndAlso profitRate > 0 Then
                DoSell(item, $"트레일링({dd:F1}%,수익{profitRate:F1}%)") : Return
            End If
        End If

        ' ── SuperTrend 하락 전환 (키: "ST_*", 값: Direction) ──
        Dim results = item.Engine.Results
        Dim idx = item.Candles.Count - 1
        If idx < 1 Then Return
        Dim stList = FindResult(results, "ST_")
        If stList IsNot Nothing AndAlso stList.Count > idx Then
            Dim dNow = CInt(stList(idx).Val("Direction"))
            Dim dPrev = CInt(stList(idx - 1).Val("Direction"))
            If dNow < 0 AndAlso dPrev >= 0 Then
                DoSell(item, $"ST_CrossDown(수익{profitRate:F1}%)")
            End If
        End If
    End Sub


    ' ═══════════════════════════════════════
    ' 주문
    ' ═══════════════════════════════════════

    Private Sub DoBuy(item As WatchItem, price As Integer, qty As Integer, reason As String)
        Log($"[매수] {item.Code} {item.Name} {qty}주 @{price:N0} — {reason}")
        Select Case _settings.BuyOrderType
            Case SimOrderType.Market
                TradeManager.I.BuyMarket(item.Code, qty, "SimTrade", reason)
            Case SimOrderType.LimitBestBid
                TradeManager.I.BuyLimit(item.Code, qty, If(item.Ask1 > 0, item.Ask1, price), "SimTrade", reason)
            Case SimOrderType.LimitCurrentPrice
                TradeManager.I.BuyLimit(item.Code, qty, price, "SimTrade", reason)
        End Select
        item.HighSinceBuy = price
    End Sub

    Private Sub DoSell(item As WatchItem, reason As String)
        Dim qty = TradeManager.I.GetAvailableQty(item.Code)
        If qty <= 0 Then Return
        Log($"[매도] {item.Code} {item.Name} {qty}주 — {reason}")
        Select Case _settings.SellOrderType
            Case SimOrderType.Market
                TradeManager.I.SellMarket(item.Code, qty, "SimTrade", reason)
            Case SimOrderType.LimitBestBid
                TradeManager.I.RequestOrder(item.Code, OrderSide.Sell, qty,
                    If(item.Bid1 > 0, item.Bid1, item.CurrentPrice), OrderPriceType.Limit, "SimTrade", reason)
            Case SimOrderType.LimitCurrentPrice
                TradeManager.I.RequestOrder(item.Code, OrderSide.Sell, qty,
                    item.CurrentPrice, OrderPriceType.Limit, "SimTrade", reason)
        End Select
        item.HighSinceBuy = 0
    End Sub

    Private Function GetBuyPrice(item As WatchItem) As Integer
        Select Case _settings.BuyOrderType
            Case SimOrderType.LimitBestBid : Return If(item.Ask1 > 0, item.Ask1, item.CurrentPrice)
            Case SimOrderType.LimitCurrentPrice : Return item.CurrentPrice
            Case Else : Return item.CurrentPrice
        End Select
    End Function


    ' ═══════════════════════════════════════
    ' 체결/포지션
    ' ═══════════════════════════════════════

    Private Sub OnOrderFilled(m As Msg)
        If m.Str("strategy") <> "SimTrade" Then Return
        Log($"[체결] {m.Str("side")} {m.Str("code")} {m.Int("filledQty")}주 @{m.Int("filledPrice"):N0} [{m.Str("status")}]")
    End Sub

    Private Sub OnPositionUpdated(m As Msg)
    End Sub


    ' ═══════════════════════════════════════
    ' UI 갱신
    ' ═══════════════════════════════════════

    Private Sub OnTimerRefresh(sender As Object, e As EventArgs) Handles _tmrRefresh.Tick
        If Not _isRunning Then Return
        RefreshWatchGrid()
        RefreshPositionGrid()
        RefreshSummary()
    End Sub

    Private Sub RefreshWatchGrid()
        Dim items = _watchItems.Values.OrderBy(Function(w) w.Code).ToList()
        If _dgvWatch.Rows.Count <> items.Count Then
            _dgvWatch.SuspendLayout()
            _dgvWatch.Rows.Clear()
            For Each it In items
                _dgvWatch.Rows.Add(it.Code, it.Name, it.CurrentPrice.ToString("N0"),
                    it.ChangeRate.ToString("F2") & "%", it.Volume.ToString("N0"),
                    it.Strength.ToString("F1"), it.Candles.Count, it.LastSignal)
            Next
            _dgvWatch.ResumeLayout()
        Else
            For i = 0 To items.Count - 1
                Dim it = items(i), row = _dgvWatch.Rows(i)
                row.Cells(0).Value = it.Code
                row.Cells(1).Value = it.Name
                row.Cells(2).Value = it.CurrentPrice.ToString("N0")
                row.Cells(3).Value = it.ChangeRate.ToString("F2") & "%"
                row.Cells(4).Value = it.Volume.ToString("N0")
                row.Cells(5).Value = it.Strength.ToString("F1")
                row.Cells(6).Value = it.Candles.Count
                row.Cells(7).Value = it.LastSignal
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
        _lblSummary.Text = $"현금: {cash:N0} | 평가: {eval:N0} | " &
                           $"총자산: {cash + eval:N0} | 손익: {pnl:N0} | " &
                           $"감시: {_watchItems.Count} | 보유: {TradeManager.I.PositionCount}"
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
    ' UI 구성
    ' ═══════════════════════════════════════

    Private Sub InitializeUI()
        Me.Text = "모의매매 (실험용 — 격리)"
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
        _dgvWatch = MakeGrid({"코드", "종목명", "현재가", "등락률", "거래량", "체결강도", "캔들수", "신호"})
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


    ' ═══════════════════════════════════════
    ' 설정 패널
    ' ═══════════════════════════════════════

    Private Sub BuildSettingsPanel(pnl As Panel)
        Dim y = 10
        Dim lc = Color.FromArgb(180, 180, 200)
        Dim gf = New Font("맑은 고딕", 10, FontStyle.Bold)
        Dim lf = New Font("맑은 고딕", 9)
        Dim vf = New Font("맑은 고딕", 9)
        Dim cL = 15, cV = 220, rH = 32

        y = AddGroupLabel(pnl, "▶ 지표 설정", y, gf, Color.FromArgb(100, 180, 255))
        AddLabel(pnl, "SuperTrend Period:", cL, y, lf, lc)
        _nudST_Period = AddNumeric(pnl, cV, y, 1, 50, 10, 0, vf) : y += rH
        AddLabel(pnl, "SuperTrend Multiplier:", cL, y, lf, lc)
        _nudST_Multiplier = AddNumeric(pnl, cV, y, 0.5D, 10D, 3D, 1, vf) : y += rH
        AddLabel(pnl, "RSI Period:", cL, y, lf, lc)
        _nudRSI_Period = AddNumeric(pnl, cV, y, 2, 50, 14, 0, vf) : y += rH
        AddLabel(pnl, "RSI 과매수 한계:", cL, y, lf, lc)
        _nudRSI_Overbought = AddNumeric(pnl, cV, y, 50, 95, 75, 0, vf) : y += rH
        AddLabel(pnl, "거래량 확인:", cL, y, lf, lc)
        _chkVolumeConfirm = New CheckBox With {.Location = New Point(cV, y), .Checked = True,
            .AutoSize = True, .ForeColor = Color.White}
        pnl.Controls.Add(_chkVolumeConfirm) : y += rH

        y += 10
        y = AddGroupLabel(pnl, "▶ 캔들 / 신호", y, gf, Color.FromArgb(100, 180, 255))
        AddLabel(pnl, "캔들 주기 (초):", cL, y, lf, lc)
        _nudCandleInterval = AddNumeric(pnl, cV, y, 5, 300, 60, 0, vf) : y += rH
        AddLabel(pnl, "최소 캔들 수:", cL, y, lf, lc)
        _nudMinCandles = AddNumeric(pnl, cV, y, 5, 200, 30, 0, vf) : y += rH

        y += 10
        y = AddGroupLabel(pnl, "▶ 포지션 / 리스크", y, gf, Color.FromArgb(255, 180, 100))
        AddLabel(pnl, "최대 보유 종목:", cL, y, lf, lc)
        _nudMaxPosition = AddNumeric(pnl, cV, y, 1, 20, 5, 0, vf) : y += rH
        AddLabel(pnl, "종목당 비중 (%):", cL, y, lf, lc)
        _nudPositionSize = AddNumeric(pnl, cV, y, 1, 50, 15, 0, vf) : y += rH
        AddLabel(pnl, "손절률 (%):", cL, y, lf, lc)
        _nudStopLoss = AddNumeric(pnl, cV, y, -20D, 0D, -3D, 1, vf) : y += rH
        AddLabel(pnl, "익절률 (%):", cL, y, lf, lc)
        _nudTakeProfit = AddNumeric(pnl, cV, y, 1, 50, 5, 1, vf) : y += rH
        AddLabel(pnl, "트레일링 스톱:", cL, y, lf, lc)
        _chkTrailingStop = New CheckBox With {.Location = New Point(cV, y), .Checked = True,
            .AutoSize = True, .ForeColor = Color.White}
        pnl.Controls.Add(_chkTrailingStop) : y += rH
        AddLabel(pnl, "트레일링 (%):", cL, y, lf, lc)
        _nudTrailingStop = AddNumeric(pnl, cV, y, -10D, 0D, -1.5D, 1, vf) : y += rH

        y += 10
        y = AddGroupLabel(pnl, "▶ 매매 시간", y, gf, Color.FromArgb(180, 255, 100))
        AddLabel(pnl, "매매 시작:", cL, y, lf, lc)
        _txtStartTime = MakeTextBox(cV, y, "09:05", vf) : pnl.Controls.Add(_txtStartTime) : y += rH
        AddLabel(pnl, "신규매수 금지:", cL, y, lf, lc)
        _txtNoNewBuy = MakeTextBox(cV, y, "14:30", vf) : pnl.Controls.Add(_txtNoNewBuy) : y += rH
        AddLabel(pnl, "강제 청산:", cL, y, lf, lc)
        _txtForceClose = MakeTextBox(cV, y, "15:15", vf) : pnl.Controls.Add(_txtForceClose) : y += rH

        y += 10
        y = AddGroupLabel(pnl, "▶ 주문 방식", y, gf, Color.FromArgb(255, 100, 180))
        AddLabel(pnl, "매수 주문:", cL, y, lf, lc)
        _cboBuyOrder = MakeCombo(cV, y, {"시장가", "최우선매도호가 지정가", "현재가 지정가"}, 1, vf)
        pnl.Controls.Add(_cboBuyOrder) : y += rH
        AddLabel(pnl, "매도 주문:", cL, y, lf, lc)
        _cboSellOrder = MakeCombo(cV, y, {"시장가", "최우선매수호가 지정가", "현재가 지정가"}, 0, vf)
        pnl.Controls.Add(_cboSellOrder)
    End Sub


    ' ═══════════════════════════════════════
    ' UI 헬퍼
    ' ═══════════════════════════════════════

    Private Function AddGroupLabel(pnl As Panel, text As String, y As Integer,
                                   font As Font, fc As Color) As Integer
        pnl.Controls.Add(New Label With {.Text = text, .Location = New Point(10, y),
            .AutoSize = True, .Font = font, .ForeColor = fc})
        Return y + 28
    End Function

    Private Sub AddLabel(pnl As Panel, text As String, x As Integer, y As Integer,
                         font As Font, fc As Color)
        pnl.Controls.Add(New Label With {.Text = text, .Location = New Point(x, y + 2),
            .AutoSize = True, .Font = font, .ForeColor = fc})
    End Sub

    Private Function AddNumeric(pnl As Panel, x As Integer, y As Integer,
                                mn As Decimal, mx As Decimal, v As Decimal,
                                dec As Integer, font As Font) As NumericUpDown
        Dim nud As New NumericUpDown With {.Location = New Point(x, y), .Size = New Size(100, 24),
            .Minimum = mn, .Maximum = mx, .Value = v, .DecimalPlaces = dec, .Font = font,
            .BackColor = Color.FromArgb(45, 45, 60), .ForeColor = Color.White}
        If dec > 0 Then nud.Increment = 0.1D
        pnl.Controls.Add(nud) : Return nud
    End Function

    Private Function MakeTextBox(x As Integer, y As Integer, text As String, font As Font) As TextBox
        Return New TextBox With {.Location = New Point(x, y), .Size = New Size(80, 24),
            .Text = text, .Font = font, .BackColor = Color.FromArgb(45, 45, 60), .ForeColor = Color.White}
    End Function

    Private Function MakeCombo(x As Integer, y As Integer, items As String(),
                               selIdx As Integer, font As Font) As ComboBox
        Dim cbo As New ComboBox With {.Location = New Point(x, y), .Size = New Size(160, 24),
            .DropDownStyle = ComboBoxStyle.DropDownList, .Font = font,
            .BackColor = Color.FromArgb(45, 45, 60), .ForeColor = Color.White}
        cbo.Items.AddRange(items) : cbo.SelectedIndex = selIdx : Return cbo
    End Function

    Private Function MakeButton(text As String, x As Integer, y As Integer, bg As Color) As Button
        Return New Button With {.Text = text, .Location = New Point(x, y),
            .Size = New Size(100, 32), .FlatStyle = FlatStyle.Flat,
            .BackColor = bg, .ForeColor = Color.White, .Font = New Font("맑은 고딕", 9, FontStyle.Bold)}
    End Function

    Private Function MakeGrid(columns As String()) As DataGridView
        Dim dgv As New DataGridView With {
            .Dock = DockStyle.Fill, .ReadOnly = True, .AllowUserToAddRows = False,
            .AllowUserToDeleteRows = False, .AllowUserToResizeRows = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .BackgroundColor = Color.FromArgb(25, 25, 35),
            .GridColor = Color.FromArgb(50, 50, 65),
            .DefaultCellStyle = New DataGridViewCellStyle With {
                .BackColor = Color.FromArgb(30, 30, 42), .ForeColor = Color.White,
                .SelectionBackColor = Color.FromArgb(60, 60, 80),
                .Font = New Font("맑은 고딕", 9)},
            .ColumnHeadersDefaultCellStyle = New DataGridViewCellStyle With {
                .BackColor = Color.FromArgb(40, 40, 55),
                .ForeColor = Color.FromArgb(200, 200, 220),
                .Font = New Font("맑은 고딕", 9, FontStyle.Bold)},
            .EnableHeadersVisualStyles = False, .RowHeadersVisible = False,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .BorderStyle = BorderStyle.None}
        For Each col In columns : dgv.Columns.Add(col, col) : Next
        Return dgv
    End Function

End Class
