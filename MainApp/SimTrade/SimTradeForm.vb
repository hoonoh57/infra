' ═══════════════════════════════════════════════════════════════
' SimTradeForm.vb — 모의매매 전용 폼 (완전 격리)
' ═══════════════════════════════════════════════════════════════
' ★ 삭제: 이 파일 + SimTradeModels.vb 제거, vbproj 링크 제거,
'   메뉴 호출 1줄 제거 → MainApp 완전 무영향.
'
' ★ 키움 모의매매 서버에 실제 주문 (지정가/시장가만)
'   - TradeManager가 Chejan으로 체결/잔고 추적
'   - 이 폼은 "언제, 무엇을, 얼마나" 결정하는 신호 로직만 담당
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

    ' ═══════════════════════════════════════
    ' 생성/소멸
    ' ═══════════════════════════════════════

    Public Sub New()
        InitializeUI()
        _tmrRefresh.Interval = 1000
        RegisterIndicators()
    End Sub

    Private Sub SimTradeForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Log("모의매매 폼 로드. 조건식을 선택한 뒤 [시작]을 누르세요.")
        Log("★ 키움 모의매매 서버 주문 — 중간가/스톱지정가 불가, 지정가/시장가만 사용")
    End Sub

    Private Sub SimTradeForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        StopSim()
    End Sub

    ' ═══════════════════════════════════════
    ' 지표 등록 (종목별 IndicatorEngine에 공통 적용)
    ' ═══════════════════════════════════════

    Private Sub RegisterIndicators(engine As IndicatorEngine)
        engine.Register(New SuperTrend_Indicator(_settings.ST_Period, _settings.ST_Multiplier))
        engine.Register(New RSI_Indicator(_settings.RSI_Period))
        engine.Register(New Volume_Indicator())
        engine.Register(New OBV_Indicator())
        engine.Register(New TickIntensity_Indicator())
    End Sub

    Private Sub RegisterIndicators()
        ' 초기화 시 호출 — 종목 추가 시 개별 엔진에 적용
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

        ' 설정 패널 → _settings 동기화
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

        ' 설정값 로그
        LogCurrentSettings()

        ' ── MessageBus 구독 ──
        MessageBus.I.On(Topics.TICK, AddressOf OnTick)
        MessageBus.I.On(Topics.ORDERBOOK, AddressOf OnOrderBook)
        MessageBus.I.On(Topics.CONDITION_HIT, AddressOf OnConditionHit)
        MessageBus.I.On(Topics.TRADE_ORDER_FILLED, AddressOf OnOrderFilled)
        MessageBus.I.On(Topics.TRADE_POSITION_UPDATED, AddressOf OnPositionUpdated)

        ' ── ① StockInfoManager에 이미 로드된 조건검색 종목 먼저 가져오기 ──
        Dim existingItems = StockInfoManager.I.GetBySource(DataSourceType.조건검색)
        If existingItems IsNot Nothing AndAlso existingItems.Count > 0 Then
            Log($"기존 조건검색 종목 {existingItems.Count}건 로드 (StockInfoManager)")
            For Each item In existingItems
                AddWatchItem(item.Code)
            Next
        End If

        ' ── ② 조건검색 시작 (실시간 편입/이탈 + 초기 결과) ──
        MessageBus.I.On(Topics.CONDITION_SEARCH_RESULT, AddressOf OnConditionSearchResult)
        MessageBus.I.Emit(Topics.CONDITION_START,
                          "name", _conditionName,
                          "index", _conditionIndex,
                          "realtime", If(_settings.UseRealtimeCondition, 1, 0))

        _tmrRefresh.Start()
        Log($"▶ 모의매매 시작 — 조건식: {_conditionName}")
        _lblStatus.Text = $"● 실행 중 | {_conditionName}"
        _lblStatus.ForeColor = Color.Lime
    End Sub

    Private Sub StopSim()
        If Not _isRunning Then Return
        _isRunning = False

        ' ── 구독 해제 ──
        MessageBus.I.Off(Topics.TICK, AddressOf OnTick)
        MessageBus.I.Off(Topics.ORDERBOOK, AddressOf OnOrderBook)
        MessageBus.I.Off(Topics.CONDITION_HIT, AddressOf OnConditionHit)
        MessageBus.I.Off(Topics.TRADE_ORDER_FILLED, AddressOf OnOrderFilled)
        MessageBus.I.Off(Topics.TRADE_POSITION_UPDATED, AddressOf OnPositionUpdated)
        MessageBus.I.Off(Topics.CONDITION_SEARCH_RESULT, AddressOf OnConditionSearchResult)

        ' ── 조건검색 중지 ──
        If _conditionIndex >= 0 Then
            MessageBus.I.Emit(Topics.CONDITION_STOP,
                              "name", _conditionName, "index", _conditionIndex)
        End If

        ' ── 실시간 해제 ──
        If _watchItems.Count > 0 Then
            Dim codes = String.Join(";", _watchItems.Keys)
            MessageBus.I.Emit(Topics.REALTIME_UNSUBSCRIBE, "codes", codes)
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
    ' 조건검색 결과 수신 — 디버그 로그 강화
    ' ═══════════════════════════════════════

    Private Sub OnConditionSearchResult(m As Msg)
        Log($"[DEBUG] CONDITION_SEARCH_RESULT 수신 — success={m.Bool("success")} message={m.Str("message")}")

        If Not m.Bool("success") Then
            Log($"[오류] 조건검색 실패: {m.Str("message")}")
            ' ★ 실패(타임아웃 등) 시 StockInfoManager 종목으로 폴백
            If _watchItems.Count = 0 Then
                Dim fallbackItems = StockInfoManager.I.GetBySource(DataSourceType.조건검색)
                If fallbackItems IsNot Nothing AndAlso fallbackItems.Count > 0 Then
                    Log($"[폴백] StockInfoManager에서 {fallbackItems.Count}종목 로드")
                    For Each item In fallbackItems
                        AddWatchItem(item.Code)
                    Next
                End If
            End If
            Return
        End If

        Dim codes = TryCast(m("codes"), String())
        If codes Is Nothing OrElse codes.Length = 0 Then
            Log("조건검색 결과: 0건")
            Return
        End If

        Log($"조건검색 초기 결과: {codes.Length}건 — {String.Join(",", codes.Take(5))}...")
        For Each code In codes
            AddWatchItem(code)
        Next
    End Sub

    Private Sub OnConditionHit(m As Msg)
        Dim code = m.Str("code")
        Dim hitType = m.Str("type")  ' "I"=편입, "D"=이탈

        If hitType = "I" Then
            If AddWatchItem(code) Then
                Log($"[편입] {code} — 조건식 실시간 편입")
            End If
        ElseIf hitType = "D" Then
            ' 이탈 시 보유 중이 아니면 감시 제거
            If _watchItems.ContainsKey(code) AndAlso Not TradeManager.I.HasPosition(code) Then
                RemoveWatchItem(code)
                Log($"[이탈] {code} — 감시 해제")
            End If
        End If
    End Sub

    Private Function AddWatchItem(code As String) As Boolean
        If _watchItems.ContainsKey(code) Then Return False
        If _watchItems.Count >= 50 Then Return False  ' 감시 상한

        Dim item As New WatchItem With {.Code = code}
        RegisterIndicators(item.Engine)
        _watchItems(code) = item

        ' 종목명 조회
        Dim si = StockInfoManager.I.GetItem(code)
        If si IsNot Nothing Then item.Name = si.Name

        ' 실시간 구독
        MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", code)
        item.IsSubscribed = True

        Log($"[감시추가] {code} {item.Name} (총 {_watchItems.Count}종목)")
        Return True
    End Function

    Private Sub RemoveWatchItem(code As String)
        If Not _watchItems.ContainsKey(code) Then Return
        _watchItems.Remove(code)
        MessageBus.I.Emit(Topics.REALTIME_UNSUBSCRIBE, "codes", code)
    End Sub

    ' ═══════════════════════════════════════
    ' 실시간 틱 수신 → 캔들 빌딩 → 신호 판단
    ' ═══════════════════════════════════════

    Private Sub OnTick(m As Msg)
        If Not _isRunning Then Return
        Dim code = m.Str("code")
        If Not _watchItems.ContainsKey(code) Then Return

        Dim item = _watchItems(code)
        Dim price = Math.Abs(CInt(m.Dbl("price")))
        Dim vol = CLng(Math.Abs(m.Dbl("volume")))
        Dim strength = m.Dbl("strength")
        Dim ask = Math.Abs(CInt(m.Dbl("ask1")))
        Dim bid = Math.Abs(CInt(m.Dbl("bid1")))

        If price <= 0 Then Return

        item.CurrentPrice = price
        If ask > 0 Then item.Ask1 = ask
        If bid > 0 Then item.Bid1 = bid
        item.Strength = strength
        item.ChangeRate = m.Dbl("changeRate")
        item.Volume = CLng(Math.Abs(m.Dbl("cumVolume")))

        ' 종목명 업데이트
        If item.Name = "" Then
            Dim si = StockInfoManager.I.GetItem(code)
            If si IsNot Nothing Then item.Name = si.Name
        End If

        ' ── 캔들 빌딩 ──
        BuildCandle(item, price, vol, DateTime.Now)

        ' ── 신호 판단 ──
        If item.Candles.Count >= _settings.MinCandlesForSignal Then
            EvaluateSignal(item)
        End If
    End Sub

    Private Sub OnOrderBook(m As Msg)
        Dim code = m.Str("code")
        If Not _watchItems.ContainsKey(code) Then Return

        Dim item = _watchItems(code)
        Dim askPrices = TryCast(m("askPrices"), Double())
        Dim bidPrices = TryCast(m("bidPrices"), Double())
        If askPrices IsNot Nothing AndAlso askPrices.Length > 0 Then
            item.Ask1 = CInt(Math.Abs(askPrices(0)))
        End If
        If bidPrices IsNot Nothing AndAlso bidPrices.Length > 0 Then
            item.Bid1 = CInt(Math.Abs(bidPrices(0)))
        End If
    End Sub

    Private Sub BuildCandle(item As WatchItem, price As Single, vol As Long, tickTime As DateTime)
        Dim interval = TimeSpan.FromSeconds(_settings.CandleIntervalSec)
        Dim candleStart = New DateTime(tickTime.Ticks - (tickTime.Ticks Mod interval.Ticks))

        If item.Candles.Count = 0 OrElse candleStart > item.CurrentCandleStart Then
            ' 새 캔들 시작
            item.CurrentCandleStart = candleStart
            item.Candles.Add(CandleItem.Create(candleStart, price))

            ' 캔들 수 제한 (최근 500개)
            If item.Candles.Count > 500 Then item.Candles.RemoveAt(0)

            ' 새 캔들이면 전체 지표 재계산
            item.Engine.CalculateAll(item.Candles)
        Else
            ' 기존 캔들 업데이트
            Dim last = item.Candles(item.Candles.Count - 1)
            last.UpdateFromTick(price, vol, tickTime)
            item.Engine.UpdateLast(item.Candles)
        End If
    End Sub

    ' ═══════════════════════════════════════
    ' ★ 핵심: 신호 판단 (실험 로직 — 이 부분만 격리된 실험)
    ' ═══════════════════════════════════════

    Private Sub EvaluateSignal(item As WatchItem)
        Dim now = DateTime.Now.TimeOfDay
        Dim hasPosition = TradeManager.I.HasPosition(item.Code)

        ' ── 시간 체크 ──
        If now < _settings.TradingStartTime Then Return
        If now >= _settings.ForceCloseTime AndAlso hasPosition Then
            DoSell(item, "장마감청산")
            Return
        End If

        ' ── 보유 중이면 매도 판단 ──
        If hasPosition Then
            EvaluateSell(item)
            Return
        End If

        ' ── 신규 매수 금지 시간 ──
        If now >= _settings.NoNewBuyAfter Then
            item.LastSignal = "매수금지시간"
            Return
        End If

        ' ── 매수 판단 ──
        EvaluateBuy(item)
    End Sub

    Private Sub EvaluateBuy(item As WatchItem)
        Dim results = item.Engine.Results
        Dim idx = item.Candles.Count - 1
        If idx < 1 Then Return

        ' ── SuperTrend 매수 신호 ──
        Dim stResults As List(Of IndicatorResult) = Nothing
        If Not results.TryGetValue("SuperTrend", stResults) Then Return
        If stResults.Count <= idx Then Return

        Dim stNow = stResults(idx)
        Dim stPrev = stResults(idx - 1)
        Dim trendNow = stNow.Val("Trend")       ' 1=상승, -1=하락
        Dim trendPrev = stPrev.Val("Trend")

        ' 조건1: SuperTrend 상승 전환 (CrossUp)
        Dim isCrossUp = (trendNow > 0 AndAlso trendPrev <= 0)
        ' 또는 이미 상승 중이고 가격이 SuperTrend 위
        Dim isAboveST = (trendNow > 0 AndAlso item.Candles(idx).Close > stNow.Val("SuperTrend"))

        If Not (isCrossUp OrElse isAboveST) Then
            item.LastSignal = "신호없음"
            Return
        End If

        ' ── RSI 과매수 필터 ──
        Dim rsiResults As List(Of IndicatorResult) = Nothing
        If results.TryGetValue("RSI", rsiResults) AndAlso rsiResults.Count > idx Then
            Dim rsiVal = rsiResults(idx).Val("RSI")
            If Not Single.IsNaN(rsiVal) AndAlso rsiVal > _settings.RSI_OverboughtLimit Then
                item.LastSignal = $"RSI과매수({rsiVal:F0})"
                Return
            End If
        End If

        ' ── 거래량 확인 ──
        If _settings.RequireVolumeConfirm Then
            Dim volResults As List(Of IndicatorResult) = Nothing
            If results.TryGetValue("Volume", volResults) AndAlso volResults.Count > idx Then
                Dim volRatio = volResults(idx).Val("VolumeRatio")
                If Not Single.IsNaN(volRatio) AndAlso volRatio < 1.0F Then
                    item.LastSignal = "거래량부족"
                    Return
                End If
            End If
        End If

        ' ── 포지션 수 체크 ──
        If TradeManager.I.PositionCount >= _settings.MaxPositionCount Then
            item.LastSignal = "최대종목초과"
            Return
        End If

        ' ── 매수 수량 계산 ──
        Dim cash = TradeManager.I.AvailableCash
        Dim equity = cash + TradeManager.I.TotalEvalAmount
        Dim maxAmount = CLng(equity * _settings.PositionSizeRate)
        If maxAmount > cash Then maxAmount = cash
        Dim price = GetBuyPrice(item)
        If price <= 0 Then Return
        Dim qty = CInt(maxAmount \ price)
        If qty <= 0 Then Return

        ' ── 매수 주문 ──
        item.LastSignal = "매수신호!"
        Dim reason = If(isCrossUp, "ST_CrossUp", "ST_Above")
        DoBuy(item, price, qty, reason)
    End Sub

    Private Sub EvaluateSell(item As WatchItem)
        Dim pos = TradeManager.I.GetPosition(item.Code)
        If pos Is Nothing OrElse pos.Quantity <= 0 Then Return

        ' 최고가 추적 (트레일링용)
        If item.CurrentPrice > item.HighSinceBuy Then
            item.HighSinceBuy = item.CurrentPrice
        End If

        Dim profitRate = pos.ProfitRate
        item.LastSignal = $"보유중({profitRate:+0.0;-0.0}%)"

        ' ── 손절 ──
        If profitRate <= _settings.StopLossRate Then
            DoSell(item, $"손절({profitRate:F1}%)")
            Return
        End If

        ' ── 익절 ──
        If profitRate >= _settings.TakeProfitRate Then
            DoSell(item, $"익절({profitRate:F1}%)")
            Return
        End If

        ' ── 트레일링 스톱 ──
        If _settings.EnableTrailingStop AndAlso item.HighSinceBuy > 0 Then
            Dim drawdown = (CDbl(item.CurrentPrice - item.HighSinceBuy) / item.HighSinceBuy) * 100
            If drawdown <= _settings.TrailingStopRate AndAlso profitRate > 0 Then
                DoSell(item, $"트레일링({drawdown:F1}%,수익{profitRate:F1}%)")
                Return
            End If
        End If

        ' ── SuperTrend 하락 전환 시 매도 ──
        Dim results = item.Engine.Results
        Dim idx = item.Candles.Count - 1
        If idx < 1 Then Return

        Dim stResults As List(Of IndicatorResult) = Nothing
        If results.TryGetValue("SuperTrend", stResults) AndAlso stResults.Count > idx Then
            Dim trendNow = stResults(idx).Val("Trend")
            Dim trendPrev = stResults(idx - 1).Val("Trend")
            If trendNow < 0 AndAlso trendPrev >= 0 Then
                DoSell(item, $"ST_CrossDown(수익{profitRate:F1}%)")
            End If
        End If
    End Sub

    ' ═══════════════════════════════════════
    ' 주문 실행 (TradeManager 경유 → 키움 모의매매 서버)
    ' ═══════════════════════════════════════

    Private Sub DoBuy(item As WatchItem, price As Integer, qty As Integer, reason As String)
        Log($"[매수] {item.Code} {item.Name} {qty}주 @{price:N0} — {reason}")

        Select Case _settings.BuyOrderType
            Case SimOrderType.Market
                TradeManager.I.BuyMarket(item.Code, qty, "SimTrade", reason)
            Case SimOrderType.LimitBestBid
                Dim p = If(item.Ask1 > 0, item.Ask1, price)
                TradeManager.I.BuyLimit(item.Code, qty, p, "SimTrade", reason)
            Case SimOrderType.LimitCurrentPrice
                TradeManager.I.BuyLimit(item.Code, qty, price, "SimTrade", reason)
        End Select

        item.HighSinceBuy = price
    End Sub

    Private Sub DoSell(item As WatchItem, reason As String)
        Dim availQty = TradeManager.I.GetAvailableQty(item.Code)
        If availQty <= 0 Then Return

        Log($"[매도] {item.Code} {item.Name} {availQty}주 — {reason}")

        Select Case _settings.SellOrderType
            Case SimOrderType.Market
                TradeManager.I.SellMarket(item.Code, availQty, "SimTrade", reason)
            Case SimOrderType.LimitBestBid
                Dim p = If(item.Bid1 > 0, item.Bid1, item.CurrentPrice)
                TradeManager.I.RequestOrder(item.Code, OrderSide.Sell, availQty, p,
                                            OrderPriceType.Limit, "SimTrade", reason)
            Case SimOrderType.LimitCurrentPrice
                TradeManager.I.RequestOrder(item.Code, OrderSide.Sell, availQty, item.CurrentPrice,
                                            OrderPriceType.Limit, "SimTrade", reason)
        End Select

        item.HighSinceBuy = 0
    End Sub

    Private Function GetBuyPrice(item As WatchItem) As Integer
        Select Case _settings.BuyOrderType
            Case SimOrderType.Market
                Return item.CurrentPrice
            Case SimOrderType.LimitBestBid
                Return If(item.Ask1 > 0, item.Ask1, item.CurrentPrice)
            Case SimOrderType.LimitCurrentPrice
                Return item.CurrentPrice
            Case Else
                Return item.CurrentPrice
        End Select
    End Function

    ' ═══════════════════════════════════════
    ' 체결/포지션 이벤트 (TradeManager가 발행, 여기서 표시)
    ' ═══════════════════════════════════════

    Private Sub OnOrderFilled(m As Msg)
        If m.Str("strategy") <> "SimTrade" Then Return
        Log($"[체결] {m.Str("side")} {m.Str("code")} {m.Int("filledQty")}주 @{m.Int("filledPrice"):N0} [{m.Str("status")}]")
    End Sub

    Private Sub OnPositionUpdated(m As Msg)
        ' UI 갱신은 타이머에서 처리
    End Sub

    ' ═══════════════════════════════════════
    ' 타이머: UI 갱신
    ' ═══════════════════════════════════════

    Private Sub OnTimerRefresh(sender As Object, e As EventArgs) Handles _tmrRefresh.Tick
        If Not _isRunning Then Return
        RefreshWatchGrid()
        RefreshPositionGrid()
        RefreshSummary()
    End Sub

    Private Sub RefreshWatchGrid()
        _dgvWatch.SuspendLayout()
        _dgvWatch.Rows.Clear()
        For Each item In _watchItems.Values.OrderBy(Function(w) w.Code)
            _dgvWatch.Rows.Add(
                item.Code, item.Name, item.CurrentPrice.ToString("N0"),
                item.ChangeRate.ToString("F2") & "%",
                item.Volume.ToString("N0"), item.Strength.ToString("F1"),
                item.Candles.Count, item.LastSignal)
        Next
        _dgvWatch.ResumeLayout()
    End Sub

    Private Sub RefreshPositionGrid()
        _dgvPositions.SuspendLayout()
        _dgvPositions.Rows.Clear()
        For Each pos In TradeManager.I.GetPositions()
            _dgvPositions.Rows.Add(
                pos.Code, pos.Name, pos.Quantity,
                pos.AvgPrice.ToString("N0"), pos.CurrentPrice.ToString("N0"),
                pos.ProfitLoss.ToString("N0"), pos.ProfitRate.ToString("F2") & "%")
        Next
        _dgvPositions.ResumeLayout()
    End Sub

    Private Sub RefreshSummary()
        Dim cash = TradeManager.I.AvailableCash
        Dim evalTotal = TradeManager.I.TotalEvalAmount
        Dim pnl = TradeManager.I.TotalProfitLoss
        _lblSummary.Text = $"현금: {cash:N0} | 평가: {evalTotal:N0} | " &
                           $"총자산: {cash + evalTotal:N0} | 평가손익: {pnl:N0} | " &
                           $"감시: {_watchItems.Count}종목 | 보유: {TradeManager.I.PositionCount}종목"
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
            _rtbLog.Text = String.Join(Environment.NewLine,
                _rtbLog.Lines.Skip(_rtbLog.Lines.Length - 1000))
        End If
        _rtbLog.SelectionStart = _rtbLog.Text.Length
        _rtbLog.ScrollToCaret()
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

        ' ── 상단 ──
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

        ' ── 탭 ──
        _tabControl = New TabControl With {.Dock = DockStyle.Fill}

        ' 감시 탭
        Dim tabWatch As New TabPage("감시종목")
        _dgvWatch = MakeGrid({"코드", "종목명", "현재가", "등락률", "거래량", "체결강도", "캔들수", "신호"})
        tabWatch.Controls.Add(_dgvWatch)
        _tabControl.TabPages.Add(tabWatch)

        ' 포지션 탭
        Dim tabPos As New TabPage("보유종목")
        _dgvPositions = MakeGrid({"코드", "종목명", "수량", "매입가", "현재가", "손익", "수익률"})
        tabPos.Controls.Add(_dgvPositions)
        _tabControl.TabPages.Add(tabPos)

        ' 매매이력 탭
        Dim tabHistory As New TabPage("매매이력")
        _dgvHistory = MakeGrid({"시간", "구분", "코드", "종목명", "수량", "가격", "손익", "사유"})
        tabHistory.Controls.Add(_dgvHistory)
        _tabControl.TabPages.Add(tabHistory)

        ' 로그 탭
        Dim tabLog As New TabPage("로그")
        _rtbLog = New RichTextBox With {.Dock = DockStyle.Fill, .ReadOnly = True,
            .BackColor = Color.FromArgb(20, 20, 30), .ForeColor = Color.FromArgb(200, 200, 200),
            .Font = New Font("Consolas", 9), .BorderStyle = BorderStyle.None}
        tabLog.Controls.Add(_rtbLog)
        _tabControl.TabPages.Add(tabLog)

        ' ★ 설정 탭
        Dim tabSettings As New TabPage("설정")
        tabSettings.BackColor = Color.FromArgb(30, 30, 42)
        tabSettings.AutoScroll = True
        _pnlSettings = New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True,
            .BackColor = Color.FromArgb(30, 30, 42), .ForeColor = Color.White, .Padding = New Padding(15)}
        BuildSettingsPanel(_pnlSettings)
        tabSettings.Controls.Add(_pnlSettings)
        _tabControl.TabPages.Add(tabSettings)

        Me.Controls.Add(_tabControl)
        Me.Controls.Add(pnlTop)

        ' 설정값 → UI 초기화
        LoadSettingsToUI()
    End Sub

    ' ═══════════════════════════════════════
    ' 설정 패널 빌드
    ' ═══════════════════════════════════════

    Private Sub BuildSettingsPanel(pnl As Panel)
        Dim y As Integer = 10
        Dim lblColor = Color.FromArgb(180, 180, 200)
        Dim grpFont = New Font("맑은 고딕", 10, FontStyle.Bold)
        Dim lblFont = New Font("맑은 고딕", 9)
        Dim valFont = New Font("맑은 고딕", 9)
        Dim colLabel As Integer = 15
        Dim colValue As Integer = 220
        Dim rowH As Integer = 32

        ' ───── 지표 설정 ─────
        y = AddGroupLabel(pnl, "▶ 지표 설정", y, grpFont, Color.FromArgb(100, 180, 255))

        AddLabel(pnl, "SuperTrend Period:", colLabel, y, lblFont, lblColor)
        _nudST_Period = AddNumeric(pnl, colValue, y, 1, 50, 10, 0, valFont) : y += rowH

        AddLabel(pnl, "SuperTrend Multiplier:", colLabel, y, lblFont, lblColor)
        _nudST_Multiplier = AddNumeric(pnl, colValue, y, 0.5D, 10D, 3D, 1, valFont) : y += rowH

        AddLabel(pnl, "RSI Period:", colLabel, y, lblFont, lblColor)
        _nudRSI_Period = AddNumeric(pnl, colValue, y, 2, 50, 14, 0, valFont) : y += rowH

        AddLabel(pnl, "RSI 과매수 한계:", colLabel, y, lblFont, lblColor)
        _nudRSI_Overbought = AddNumeric(pnl, colValue, y, 50, 100, 75, 0, valFont) : y += rowH

        _chkVolumeConfirm = AddCheckBox(pnl, "거래량 확인 사용", colLabel, y, True, valFont) : y += rowH

        ' ───── 캔들/신호 ─────
        y += 5
        y = AddGroupLabel(pnl, "▶ 캔들 / 신호", y, grpFont, Color.FromArgb(100, 180, 255))

        AddLabel(pnl, "캔들 주기 (초):", colLabel, y, lblFont, lblColor)
        _nudCandleInterval = AddNumeric(pnl, colValue, y, 3, 300, 10, 0, valFont) : y += rowH

        AddLabel(pnl, "신호 판단 최소 캔들:", colLabel, y, lblFont, lblColor)
        _nudMinCandles = AddNumeric(pnl, colValue, y, 5, 200, 30, 0, valFont) : y += rowH

        ' ───── 포지션/리스크 ─────
        y += 5
        y = AddGroupLabel(pnl, "▶ 포지션 / 리스크", y, grpFont, Color.FromArgb(255, 180, 100))

        AddLabel(pnl, "최대 보유 종목:", colLabel, y, lblFont, lblColor)
        _nudMaxPosition = AddNumeric(pnl, colValue, y, 1, 20, 5, 0, valFont) : y += rowH

        AddLabel(pnl, "종목당 비중 (%):", colLabel, y, lblFont, lblColor)
        _nudPositionSize = AddNumeric(pnl, colValue, y, 1, 100, 15, 0, valFont) : y += rowH

        AddLabel(pnl, "손절 (%):", colLabel, y, lblFont, lblColor)
        _nudStopLoss = AddNumeric(pnl, colValue, y, -30D, 0D, -3D, 1, valFont) : y += rowH

        AddLabel(pnl, "익절 (%):", colLabel, y, lblFont, lblColor)
        _nudTakeProfit = AddNumeric(pnl, colValue, y, 0D, 100D, 5D, 1, valFont) : y += rowH

        _chkTrailingStop = AddCheckBox(pnl, "트레일링 스톱 사용", colLabel, y, True, valFont) : y += rowH

        AddLabel(pnl, "트레일링 스톱 (%):", colLabel, y, lblFont, lblColor)
        _nudTrailingStop = AddNumeric(pnl, colValue, y, -20D, 0D, -1.5D, 1, valFont) : y += rowH

        ' ───── 시간 ─────
        y += 5
        y = AddGroupLabel(pnl, "▶ 매매 시간", y, grpFont, Color.FromArgb(100, 255, 150))

        AddLabel(pnl, "매매 시작 시간:", colLabel, y, lblFont, lblColor)
        _txtStartTime = AddTextBox(pnl, colValue, y, "09:05", valFont) : y += rowH

        AddLabel(pnl, "신규 매수 금지:", colLabel, y, lblFont, lblColor)
        _txtNoNewBuy = AddTextBox(pnl, colValue, y, "14:30", valFont) : y += rowH

        AddLabel(pnl, "강제 청산 시간:", colLabel, y, lblFont, lblColor)
        _txtForceClose = AddTextBox(pnl, colValue, y, "15:15", valFont) : y += rowH

        ' ───── 주문 방식 ─────
        y += 5
        y = AddGroupLabel(pnl, "▶ 주문 방식", y, grpFont, Color.FromArgb(255, 100, 100))

        AddLabel(pnl, "매수 주문:", colLabel, y, lblFont, lblColor)
        _cboBuyOrder = AddComboBox(pnl, colValue, y, {"시장가", "최우선매도호가 지정가", "현재가 지정가"}, 1, valFont) : y += rowH

        AddLabel(pnl, "매도 주문:", colLabel, y, lblFont, lblColor)
        _cboSellOrder = AddComboBox(pnl, colValue, y, {"시장가", "최우선매수호가 지정가", "현재가 지정가"}, 0, valFont) : y += rowH
    End Sub

    ' ═══════════════════════════════════════
    ' 설정 패널 UI 헬퍼
    ' ═══════════════════════════════════════

    Private Shared Function AddGroupLabel(pnl As Panel, text As String, y As Integer, fnt As Font, clr As Color) As Integer
        Dim lbl As New Label With {
            .Text = text, .Location = New Point(10, y), .AutoSize = True,
            .Font = fnt, .ForeColor = clr}
        pnl.Controls.Add(lbl)
        Return y + 28
    End Function

    Private Shared Sub AddLabel(pnl As Panel, text As String, x As Integer, y As Integer, fnt As Font, clr As Color)
        Dim lbl As New Label With {
            .Text = text, .Location = New Point(x, y + 3), .AutoSize = True,
            .Font = fnt, .ForeColor = clr}
        pnl.Controls.Add(lbl)
    End Sub

    Private Shared Function AddNumeric(pnl As Panel, x As Integer, y As Integer,
                                        min As Decimal, max As Decimal, val As Decimal,
                                        decimals As Integer, fnt As Font) As NumericUpDown
        Dim nud As New NumericUpDown With {
            .Location = New Point(x, y), .Size = New Size(100, 24),
            .Minimum = min, .Maximum = max, .Value = Math.Max(min, Math.Min(max, val)),
            .DecimalPlaces = decimals,
            .Increment = If(decimals > 0, 0.1D, 1D),
            .Font = fnt, .BackColor = Color.FromArgb(45, 45, 60), .ForeColor = Color.White,
            .BorderStyle = BorderStyle.FixedSingle}
        pnl.Controls.Add(nud)
        Return nud
    End Function

    Private Shared Function AddCheckBox(pnl As Panel, text As String, x As Integer, y As Integer,
                                         checked As Boolean, fnt As Font) As CheckBox
        Dim chk As New CheckBox With {
            .Text = text, .Location = New Point(x, y), .AutoSize = True,
            .Checked = checked, .Font = fnt, .ForeColor = Color.FromArgb(180, 180, 200)}
        pnl.Controls.Add(chk)
        Return chk
    End Function

    Private Shared Function AddTextBox(pnl As Panel, x As Integer, y As Integer,
                                        defaultText As String, fnt As Font) As TextBox
        Dim txt As New TextBox With {
            .Location = New Point(x, y), .Size = New Size(100, 24),
            .Text = defaultText, .Font = fnt,
            .BackColor = Color.FromArgb(45, 45, 60), .ForeColor = Color.White,
            .BorderStyle = BorderStyle.FixedSingle}
        pnl.Controls.Add(txt)
        Return txt
    End Function

    Private Shared Function AddComboBox(pnl As Panel, x As Integer, y As Integer,
                                         items As String(), selectedIndex As Integer, fnt As Font) As ComboBox
        Dim cbo As New ComboBox With {
            .Location = New Point(x, y), .Size = New Size(180, 24),
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Font = fnt, .BackColor = Color.FromArgb(45, 45, 60), .ForeColor = Color.White,
            .FlatStyle = FlatStyle.Flat}
        cbo.Items.AddRange(items)
        cbo.SelectedIndex = selectedIndex
        pnl.Controls.Add(cbo)
        Return cbo
    End Function

    Private Shared Function MakeGrid(columns As String()) As DataGridView
        Dim dgv As New DataGridView With {
            .Dock = DockStyle.Fill, .ReadOnly = True, .AllowUserToAddRows = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .BackgroundColor = Color.FromArgb(30, 30, 40),
            .DefaultCellStyle = New DataGridViewCellStyle With {
                .BackColor = Color.FromArgb(30, 30, 40), .ForeColor = Color.White,
                .SelectionBackColor = Color.FromArgb(50, 50, 70)},
            .ColumnHeadersDefaultCellStyle = New DataGridViewCellStyle With {
                .BackColor = Color.FromArgb(40, 40, 55), .ForeColor = Color.White},
            .EnableHeadersVisualStyles = False, .RowHeadersVisible = False,
            .BorderStyle = BorderStyle.None
        }
        For Each col In columns
            dgv.Columns.Add(col, col)
        Next
        Return dgv
    End Function

    Private Shared Function MakeButton(text As String, x As Integer, y As Integer, color As Color) As Button
        Return New Button With {
            .Text = text, .Location = New Point(x, y), .Size = New Size(100, 32),
            .FlatStyle = FlatStyle.Flat, .BackColor = color, .ForeColor = Color.White}
    End Function

End Class
