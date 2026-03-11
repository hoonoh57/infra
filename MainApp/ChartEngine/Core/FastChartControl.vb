' ═══════════════════════════════════════════════════════════════════════════════
' FastChartControl.vb — SkiaSharp 기반 고성능 실시간 주식 차트 컨트롤
' ═══════════════════════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Data
Imports System.Windows.Forms
Imports [Shared]
Imports SkiaSharp
Imports SkiaSharp.Views.Desktop
Imports MainApp.Models
Imports MainApp.Services

Public Class FastChartControl
    Inherits UserControl

    ' ──────────────────── 상수 ────────────────────
    Private Const FRAME_INTERVAL_MS As Integer = 16
    Private Const MARGIN_LEFT As Single = 10
    Private Const MARGIN_RIGHT As Single = 80
    Private Const MARGIN_TOP As Single = 6
    Private Const MARGIN_BOTTOM As Single = 24
    Private Const VOLUME_RATIO As Single = 0.15F
    Private Const PANEL_SEPARATOR_H As Single = 2
    Private Const AXIS_FONT_SIZE As Single = 11
    Private Const CROSSHAIR_LABEL_H As Single = 18
    Private Const SIGNAL_ARROW_SIZE As Single = 10

    ' ──────────────────── 색상 팔레트 ────────────────────
    Private Shared ReadOnly ColBackground As New SKColor(24, 26, 32)
    Private Shared ReadOnly ColGrid As New SKColor(40, 44, 52)
    Private Shared ReadOnly ColAxisText As New SKColor(140, 148, 160)
    Private Shared ReadOnly ColCrosshair As New SKColor(100, 110, 130, 180)
    Private Shared ReadOnly ColCrosshairLabel As New SKColor(55, 60, 72)
    Private Shared ReadOnly ColCrosshairText As New SKColor(220, 225, 235)
    Private Shared ReadOnly ColBullCandle As New SKColor(234, 57, 67)
    Private Shared ReadOnly ColBearCandle As New SKColor(46, 134, 222)
    Private Shared ReadOnly ColBullVolume As New SKColor(234, 57, 67, 90)
    Private Shared ReadOnly ColBearVolume As New SKColor(46, 134, 222, 90)
    Private Shared ReadOnly ColBuySignal As New SKColor(255, 80, 80)
    Private Shared ReadOnly ColSellSignal As New SKColor(50, 150, 255)
    Private Shared ReadOnly ColPanelBorder As New SKColor(50, 55, 65)
    Private Shared ReadOnly ColCurrentPrice As New SKColor(255, 193, 7, 200)
    Private Shared ReadOnly ColPrevClose As New SKColor(128, 128, 128, 100)

    Private Shared ReadOnly IndicatorColors As SKColor() = {
        New SKColor(255, 193, 7), New SKColor(0, 188, 212),
        New SKColor(233, 30, 99), New SKColor(76, 175, 80),
        New SKColor(255, 152, 0), New SKColor(171, 71, 188),
        New SKColor(255, 255, 255), New SKColor(139, 195, 74)
    }

    ' ──────────────────── 재사용 Paint 객체 ────────────────────
    Private ReadOnly _paintBullBody As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = ColBullCandle, .IsAntialias = False}
    Private ReadOnly _paintBearBody As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = ColBearCandle, .IsAntialias = False}
    Private ReadOnly _paintBullWick As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = ColBullCandle, .StrokeWidth = 1, .IsAntialias = False}
    Private ReadOnly _paintBearWick As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = ColBearCandle, .StrokeWidth = 1, .IsAntialias = False}
    Private ReadOnly _paintGrid As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = ColGrid, .StrokeWidth = 1, .IsAntialias = False}
    Private ReadOnly _paintAxisText As New SKPaint With {.Color = ColAxisText, .TextSize = AXIS_FONT_SIZE, .IsAntialias = True, .Typeface = SKTypeface.FromFamilyName("Consolas")}
    Private ReadOnly _paintCrosshair As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = ColCrosshair, .StrokeWidth = 1, .IsAntialias = False, .PathEffect = SKPathEffect.CreateDash({4, 3}, 0)}
    Private ReadOnly _paintCrosshairLabel As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = ColCrosshairLabel, .IsAntialias = False}
    Private ReadOnly _paintCrosshairText As New SKPaint With {.Color = ColCrosshairText, .TextSize = AXIS_FONT_SIZE, .IsAntialias = True, .Typeface = SKTypeface.FromFamilyName("Consolas")}
    Private ReadOnly _paintCurrentLine As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = ColCurrentPrice, .StrokeWidth = 1, .IsAntialias = False, .PathEffect = SKPathEffect.CreateDash({6, 3}, 0)}
    Private ReadOnly _paintCurrentLabel As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = ColCurrentPrice, .IsAntialias = False}
    Private ReadOnly _paintCurrentText As New SKPaint With {.Color = New SKColor(0, 0, 0), .TextSize = AXIS_FONT_SIZE, .IsAntialias = True, .Typeface = SKTypeface.FromFamilyName("Consolas")}
    Private ReadOnly _paintSignalBuy As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = ColBuySignal, .IsAntialias = True}
    Private ReadOnly _paintSignalSell As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = ColSellSignal, .IsAntialias = True}
    Private ReadOnly _paintPanelBorder As New SKPaint With {.Style = SKPaintStyle.Stroke, .Color = ColPanelBorder, .StrokeWidth = 1}
    Private ReadOnly _paintVolBull As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = ColBullVolume, .IsAntialias = False}
    Private ReadOnly _paintVolBear As New SKPaint With {.Style = SKPaintStyle.Fill, .Color = ColBearVolume, .IsAntialias = False}
    Private ReadOnly _reusePath As New SKPath()
    Private ReadOnly _indicatorPaints As New Dictionary(Of String, SKPaint)

    ' ──────────────────── 데이터 ────────────────────
    Private _candles As New List(Of CandleItem)
    Private ReadOnly _signals As New List(Of StrategySignal)
    Private ReadOnly _appliedStrategies As New List(Of StrategyDefinition)
    Private _stockCode As String = ""
    Private _stockName As String = ""
    Private _prevClose As Single = 0
    Private _chartType As String = "minute"
    Private _requestedCount As Integer = RuntimeChartSettings.DefaultChartOpenCount
    Private _lastTickCandleRequestCount As Integer = 0
    Private _tickCandleRetryCount As Integer = 0
    Private _tickAuxRequested As Boolean = False
    Private _programAuxRequested As Boolean = False
    Private _programRtSubscribed As Boolean = False
    Private _sectorAuxRequested As Boolean = False

    ' ──────────────────── 엔진 ────────────────────
    Private ReadOnly _indicatorEngine As New IndicatorEngine()
    Private ReadOnly _strategyEngine As New StrategyEngine()
    Private _chartHost As IChartHost

    ' ──────────────────── 뷰 상태 ────────────────────
    Private ReadOnly _vs As New ChartViewState()

    ' ──────────────────── 레이아웃 영역 ────────────────────
    Private _mainRect As SKRect
    Private _volumeRect As SKRect
    Private _panelRects As New List(Of SKRect)
    Private _totalWidth As Single
    Private _totalHeight As Single

    ' ──────────────────── 가격 범위 ────────────────────
    Private _priceHigh As Single
    Private _priceLow As Single
    Private _volumeMax As Long

    ' ──────────────────── 마우스/입력 ────────────────────
    Private _isDragging As Boolean = False
    Private _isDraggingPrice As Boolean = False
    Private _dragStartX As Integer
    Private _dragStartY As Integer
    Private _dragStartIndex As Integer
    Private _manualMaxP As Single = 0
    Private _manualMinP As Single = 0
    Private _isAutoScaleY As Boolean = True
    Private _mouseInside As Boolean = False
    Private _lastMouseX As Single = 0
    Private _lastMouseY As Single = 0

    ' ──────────────────── 자동 재생 (Simulation) ────────────────────
    Private _isAutoRolling As Boolean = False
    Private WithEvents _autoRollTimer As Timer
    Private Class LegendHitItem
        Public Property Name As String
        Public Property Rect As SKRect
    End Class
    Private ReadOnly _legendHits As New List(Of LegendHitItem)
    Private _selectedIndicatorName As String = ""

    ' ──────────────────── 설정 옵션 ────────────────────
    Private _showCurrentPriceLine As Boolean = True
    Private _showPrevCloseLine As Boolean = True
    Private _showViLine As Boolean = False
    Private _showDayChangeLines As Boolean = True

    ' ──────────────────── 프레임 쓰로틀 ────────────────────
    Private _frameTimer As Timer
    Private _needsRepaint As Boolean = True

    ' ──────────────────── SK 컨트롤 ────────────────────
    Private WithEvents _skControl As SKControl

    ' ──────────────────── 패널 정보 ────────────────────
    Private _panelIndicators As New List(Of List(Of String))
    Private _panelRanges As New List(Of Tuple(Of Single, Single))
    Private _panelLeftRanges As New List(Of Tuple(Of Single, Single))

    Public Sub New()
        SetStyle(ControlStyles.Selectable, True)
        DoubleBuffered = True

        _skControl = New SKControl()
        _skControl.Dock = DockStyle.Fill
        Controls.Add(_skControl)

        _frameTimer = New Timer()
        _frameTimer.Interval = FRAME_INTERVAL_MS
        _frameTimer.Enabled = True
        AddHandler _frameTimer.Tick, AddressOf OnFrameTimer

        _autoRollTimer = New Timer()
        _autoRollTimer.Interval = 1000 ' 1초당 1봉
        AddHandler _autoRollTimer.Tick, AddressOf OnAutoRollTick

        AddHandler _skControl.MouseMove, AddressOf OnGLMouseMove
        AddHandler _skControl.MouseDown, AddressOf OnGLMouseDown
        AddHandler _skControl.MouseUp, AddressOf OnGLMouseUp
        AddHandler _skControl.MouseWheel, AddressOf OnGLMouseWheel
        AddHandler _skControl.MouseEnter, AddressOf OnGLMouseEnter
        AddHandler _skControl.MouseLeave, AddressOf OnGLMouseLeave
        AddHandler _skControl.KeyDown, AddressOf OnGLKeyDown

        MessageBus.I.On(Topics.CANDLE_LOADED, AddressOf OnCandleLoaded)
        MessageBus.I.On(Topics.TICK_CANDLE_LOADED, AddressOf OnTickCandleLoaded)
        MessageBus.I.On(Topics.CANDLE_PERIOD_LOADED, AddressOf OnCandlePeriodLoaded)
        MessageBus.I.On(Topics.TICK, AddressOf OnTick)
        MessageBus.I.On(Topics.PROGRAM_TRADE, AddressOf OnProgramTrade)
        MessageBus.I.On(Topics.PROGRAM_TRADE_RESULT, AddressOf OnProgramTrade)
        MessageBus.I.On(Topics.TRADE_STRENGTH, AddressOf OnTradeStrength)
        MessageBus.I.On(Topics.SECTOR_STOCKS_RESULT, AddressOf OnSectorStocksResult)
        MessageBus.I.On(Topics.STRATEGY_SIGNAL, AddressOf OnStrategySignal)
    End Sub

    Public Sub SetHost(host As IChartHost)
        _chartHost = host
    End Sub

    Public Sub SetStock(stockCode As String, Optional chartType As String = "minute", Optional count As Integer = 0)
        Dim prevCode = _stockCode
        If Not String.IsNullOrWhiteSpace(prevCode) AndAlso
           Not String.Equals(prevCode, stockCode, StringComparison.OrdinalIgnoreCase) Then
            MessageBus.I.Emit("program.trade.rt.unsubscribe",
                              "code", prevCode,
                              "provider", RuntimeChartSettings.MarketDataProvider)
        End If

        _stockCode = stockCode
        _chartType = chartType
        _requestedCount = If(count > 0, count, RuntimeChartSettings.DefaultChartOpenCount)
        _tickCandleRetryCount = 0
        _tickAuxRequested = False
        _programAuxRequested = False
        _programRtSubscribed = False
        _sectorAuxRequested = False
        If _chartHost IsNot Nothing Then
            _stockName = _chartHost.GetStockName(stockCode)
        Else
            _stockName = stockCode
        End If

        _candles.Clear()
        _signals.Clear()
        _vs.StartIndex = 0
        _needsRepaint = True

        If _chartHost IsNot Nothing Then
            _chartHost.RequestCandles(stockCode, chartType, count)
            _chartHost.SubscribeRealtime(stockCode)
        End If
        _needsRepaint = True
    End Sub

    Private Sub OnAutoRollTick(sender As Object, e As EventArgs)
        If _candles.Count = 0 Then Return
        _vs.StartIndex += 1
        ' 끝에 도달하면 정지
        If _vs.StartIndex > _candles.Count - _vs.VisibleCount Then
            _vs.StartIndex = _candles.Count - _vs.VisibleCount
            StopSimulation()
        End If
        _needsRepaint = True
    End Sub

    Private Sub StopSimulation()
        _isAutoRolling = False
        _autoRollTimer.Stop()
    End Sub

    Private Sub StartSimulation()
        _isAutoRolling = True
        _autoRollTimer.Start()
    End Sub

    Public Sub LoadCandles(candles As List(Of CandleItem), Optional prevClose As Single = 0)
        If candles IsNot Nothing Then
            _candles = candles
        End If

        If prevClose > 0 Then _prevClose = prevClose
        ReCalculate()
    End Sub

    Public Sub ReCalculate()
        If _candles Is Nothing Then Return
        _indicatorEngine.CalculateAll(_candles)
        RebuildPanelLayout()
        _needsRepaint = True
    End Sub

    Public Sub UpdateTick(price As Single, volume As Long, tickTime As DateTime)
        If _candles.Count = 0 Then Return

        Dim barTime = AlignTickToCurrentBar(tickTime)
        Dim lastCandle = _candles(_candles.Count - 1)

        If lastCandle.Dt = DateTime.MinValue Then
            lastCandle.Dt = barTime
        End If

        If ShouldStartNewRealtimeBar(lastCandle.Dt, barTime) Then
            AddCandle(CandleItem.Create(barTime, price))
            _candles(_candles.Count - 1).UpdateFromTick(price, volume, tickTime)
        Else
            lastCandle.UpdateFromTick(price, volume, tickTime)
            _indicatorEngine.UpdateLast(_candles)
        End If

        EvaluateStrategies()
        _needsRepaint = True
    End Sub

    Public Sub AddCandle(c As CandleItem)
        _candles.Add(c)
        If _vs.StartIndex >= _candles.Count - _vs.VisibleCount - 2 Then
            _vs.StartIndex = Math.Max(0, _candles.Count - _vs.VisibleCount)
        End If
        _indicatorEngine.CalculateAll(_candles)
        _needsRepaint = True
    End Sub

    Public Sub AddIndicator(ind As IIndicator)
        _indicatorEngine.Register(ind)
        If _candles.Count > 0 Then
            _indicatorEngine.CalculateAll(_candles)
            RequestAuxiliaryIndicatorData()
        End If
        RebuildPanelLayout()
        _needsRepaint = True
    End Sub

    Public Sub RemoveIndicator(name As String)
        _indicatorEngine.Remove(name)
        RebuildPanelLayout()
        _needsRepaint = True
    End Sub

    Public Sub AddStrategy(strat As IStrategy)
        _strategyEngine.Register(strat)
    End Sub

    Public Sub RemoveStrategy(name As String)
        _strategyEngine.Remove(name)
    End Sub

    Public ReadOnly Property ViewState As ChartViewState
        Get
            Return _vs
        End Get
    End Property

    Public ReadOnly Property CandleCount As Integer
        Get
            Return _candles.Count
        End Get
    End Property

    Public ReadOnly Property CurrentStockCode As String
        Get
            Return _stockCode
        End Get
    End Property

    Private Sub OnCandleLoaded(m As Msg)
        If m.Has("provider") Then
            If Not RuntimeChartSettings.IsMarketDataProvider(m.Str("provider")) Then Return
        End If
        Dim code = If(m.Has("stockCode"), m.Str("stockCode"), m.Str("code"))
        If code <> _stockCode Then Return

        Dim list As New List(Of CandleItem)
        Dim strengthRows As New List(Of (Dt As DateTime, Strength As Single))

        If m.Has("rows") Then
            Dim rows = m.DictList("rows")
            For Each row In rows
                Dim item As New CandleItem()
                item.Dt = ParseCandleDateTime(row)
                If item.Dt = DateTime.MinValue Then Continue For

                item.Open = RowNum(row, "open", "시가")
                item.High = RowNum(row, "high", "고가")
                item.Low = RowNum(row, "low", "저가")
                item.Close = RowNum(row, "close", "현재가")
                item.Volume = CLng(RowNum(row, "volume", "거래량"))
                list.Add(item)

                Dim sVal = RowNum(row, "strength", "체결강도")
                If sVal > 0 Then
                    strengthRows.Add((item.Dt, sVal))
                End If
            Next
        Else
            Dim dates = m.Arr(Of Object)("dates")
            Dim opens = m.Arr(Of Object)("opens")
            Dim highs = m.Arr(Of Object)("highs")
            Dim lows = m.Arr(Of Object)("lows")
            Dim closes = m.Arr(Of Object)("closes")
            Dim volumes = m.Arr(Of Object)("volumes")

            If dates IsNot Nothing AndAlso dates.Length > 0 Then
                For i As Integer = 0 To dates.Length - 1
                    Dim dt = SharedUtil.ToDateTime(dates(i))
                    If dt = DateTime.MinValue Then Continue For

                    list.Add(New CandleItem With {
                        .Dt = dt,
                        .Open = SharedUtil.SafeDouble(opens(i).ToString()),
                        .High = SharedUtil.SafeDouble(highs(i).ToString()),
                        .Low = SharedUtil.SafeDouble(lows(i).ToString()),
                        .Close = SharedUtil.SafeDouble(closes(i).ToString()),
                        .Volume = SharedUtil.SafeLong(volumes(i).ToString())
                    })
                Next
            End If
        End If

        If list.Count = 0 Then Return

        Dim pc As Single = 0
        If m.Has("prevClose") Then pc = m.Sng("prevClose")

        If InvokeRequired Then
            BeginInvoke(Sub()
                            LoadCandles(list, pc)
                            If strengthRows.Count > 0 Then
                                For Each ind In _indicatorEngine.GetAll()
                                    Dim tsInd = TryCast(ind, TradeStrength_Indicator)
                                    If tsInd IsNot Nothing Then
                                        tsInd.SetData(strengthRows)
                                    End If
                                Next
                            End If
                            RequestAuxiliaryIndicatorData()
                        End Sub)
        Else
            LoadCandles(list, pc)
            If strengthRows.Count > 0 Then
                For Each ind In _indicatorEngine.GetAll()
                    Dim tsInd = TryCast(ind, TradeStrength_Indicator)
                    If tsInd IsNot Nothing Then
                        tsInd.SetData(strengthRows)
                    End If
                Next
            End If
            RequestAuxiliaryIndicatorData()
        End If
    End Sub

    Private Sub OnCandlePeriodLoaded(m As Msg)
        If m.Has("provider") Then
            If Not RuntimeChartSettings.IsMarketDataProvider(m.Str("provider")) Then Return
        End If
        Dim code = If(m.Has("stockCode"), m.Str("stockCode"), m.Str("code"))
        If code <> _stockCode Then Return
        Dim tf = m.Str("timeframe", "")
        If String.IsNullOrWhiteSpace(tf) OrElse Not tf.StartsWith("T", StringComparison.OrdinalIgnoreCase) Then Return
        ApplyTickRowsFromMsg(m)
    End Sub

    Private Sub OnTickCandleLoaded(m As Msg)
        Dim code = If(m.Has("stockCode"), m.Str("stockCode"), m.Str("code"))
        If code <> _stockCode Then Return
        ApplyTickRowsFromMsg(m)
    End Sub

    Private Sub ApplyTickRowsFromMsg(m As Msg)
        If m Is Nothing OrElse Not m.Has("rows") Then Return
        If m.Has("success") AndAlso Not m.Bool("success") Then
            ' 타 브릿지(예: Kiwoom) 미지원 응답은 실패 재시도 대상으로 보지 않는다.
            Return
        End If
        If m.Has("provider") AndAlso String.Equals(m.Str("provider"), "kiwoom", StringComparison.OrdinalIgnoreCase) Then
            Return
        End If
        Dim rows = m.DictList("rows")
        If rows Is Nothing OrElse rows.Count = 0 Then
            If _tickCandleRetryCount < RuntimeChartSettings.TickRetryMax Then
                _tickCandleRetryCount += 1
                Dim reqCnt = If(_lastTickCandleRequestCount > 0, _lastTickCandleRequestCount, RuntimeChartSettings.TickRequestMinCount)
                Dim tickUnit = RuntimeChartSettings.DefaultTickUnit
                System.Threading.ThreadPool.QueueUserWorkItem(Sub()
                                                                  System.Threading.Thread.Sleep(RuntimeChartSettings.TickRetryDelayMs)
                                                                  Dim stopTime = GetTickStopTime()
                                                                  MessageBus.I.Emit(Topics.TICK_CANDLE_REQUEST,
                                                                                    "code", _stockCode,
                                                                                    "provider", RuntimeChartSettings.MarketDataProvider,
                                                                                    "count", reqCnt,
                                                                                    "tickUnit", tickUnit,
                                                                                    "timeframe", RuntimeChartSettings.TickTimeframe(tickUnit),
                                                                                    "stopTime", stopTime)
                                                              End Sub)
            End If
            Return
        End If
        _tickAuxRequested = True
        _tickCandleRetryCount = 0

        Dim tickBars As New List(Of DateTime)
        For Each row In rows
            Dim dt = ParseCandleDateTime(row)
            If dt = DateTime.MinValue Then Continue For
            tickBars.Add(dt)
        Next
        If tickBars.Count = 0 Then Return

        For Each ind In _indicatorEngine.GetAll()
            Dim tickInd = TryCast(ind, TickIntensity_Indicator)
            If tickInd IsNot Nothing Then
                tickInd.SetTickBars(tickBars)
            End If
        Next

        If _candles Is Nothing OrElse _candles.Count = 0 Then Return
        If InvokeRequired Then
            BeginInvoke(Sub()
                            _indicatorEngine.CalculateAll(_candles)
                            _needsRepaint = True
                        End Sub)
        Else
            _indicatorEngine.CalculateAll(_candles)
            _needsRepaint = True
        End If
    End Sub

    Private Sub OnTick(m As Msg)
        Dim code = If(m.Has("stockCode"), m.Str("stockCode"), m.Str("code"))
        If code <> _stockCode Then Return

        Dim price = Math.Abs(m.Sng("price"))
        Dim vol As Long = 0
        If m.Has("volume") Then vol = Math.Abs(m.Lng("volume"))
        Dim fallbackDt = DateTime.Now
        Dim tickTime As DateTime = ParseMsgDateTime(m, fallbackDt)

        For Each ind In _indicatorEngine.GetAll()
            Dim tickInd = TryCast(ind, TickIntensity_Indicator)
            If tickInd IsNot Nothing Then
                tickInd.AddTick(tickTime)
            End If
        Next

        Dim tickStrength As Single = Single.NaN
        If m.Has("strength") Then tickStrength = m.Sng("strength")
        If Single.IsNaN(tickStrength) AndAlso m.Has("체결강도") Then tickStrength = CSng(SharedUtil.SafeDouble(m.Str("체결강도"), True))
        If Not Single.IsNaN(tickStrength) Then
            For Each ind In _indicatorEngine.GetAll()
                Dim tsInd = TryCast(ind, TradeStrength_Indicator)
                If tsInd IsNot Nothing Then
                    tsInd.AddData(tickTime, tickStrength)
                End If
            Next
        End If

        Dim tickNetBuy As Single = Single.NaN
        If m.Has("netBuy") Then tickNetBuy = m.Sng("netBuy")
        If Single.IsNaN(tickNetBuy) AndAlso m.Has("programNetBuy") Then tickNetBuy = m.Sng("programNetBuy")
        If Single.IsNaN(tickNetBuy) AndAlso m.Has("순매수") Then tickNetBuy = CSng(SharedUtil.SafeDouble(m.Str("순매수"), True))
        If Not Single.IsNaN(tickNetBuy) Then
            For Each ind In _indicatorEngine.GetAll()
                Dim progInd = TryCast(ind, ProgramTrade_Indicator)
                If progInd IsNot Nothing Then
                    progInd.AddData(tickTime, tickNetBuy)
                End If
            Next
        End If

        If InvokeRequired Then
            BeginInvoke(Sub() UpdateTick(price, vol, tickTime))
        Else
            UpdateTick(price, vol, tickTime)
        End If
    End Sub

    Private Sub OnProgramTrade(m As Msg)
        Dim code = If(m.Has("stockCode"), m.Str("stockCode"), m.Str("code"))
        If code <> _stockCode Then Return

        Dim rawCnt = m.Int("rawRowCount", -1)
        Dim rawFirstDt = m.Str("rawFirstDt", "")
        Dim rawFirstNet = m.Str("rawFirstNet", "")
        Dim rawLastDt = m.Str("rawLastDt", "")
        Dim rawLastNet = m.Str("rawLastNet", "")
        Dim providerObject = m.Str("providerObject", "")
        Dim isIntraday = m.Str("isIntraday", "")
        Dim rawFirstToken = m.Str("rawFirstToken", "")
        Dim rawLastToken = m.Str("rawLastToken", "")
        Dim probeError = m.Str("probeError", "")
        Dim probeTrace = m.Str("probeTrace", "")
        If rawCnt >= 0 Then
            AppLogger.I.Info($"ProgramTrade source-range: {code} rows:{rawCnt} first[{rawFirstDt},{rawFirstNet}] last[{rawLastDt},{rawLastNet}] obj:{providerObject} intraday:{isIntraday} raw[{rawFirstToken}..{rawLastToken}] probeErr:{probeError}", "Data")
            If Not String.IsNullOrWhiteSpace(probeTrace) Then
                AppLogger.I.Info($"ProgramTrade probe-trace: {code} {probeTrace}", "Data")
            End If
        End If

        Dim hasAdded As Boolean = False
        Dim addedCount As Integer = 0
        Dim firstAddedDt As DateTime = DateTime.MinValue
        Dim firstAddedNet As Single = Single.NaN
        Dim lastAddedDt As DateTime = DateTime.MinValue
        Dim lastAddedNet As Single = Single.NaN
        If m.Has("rows") Then
            Dim rows = m.DictList("rows")
            If rows IsNot Nothing Then
                Dim fallbackDtRows = If(_candles IsNot Nothing AndAlso _candles.Count > 0, _candles(_candles.Count - 1).Dt, DateTime.Now)
                For Each row In rows
                    If row Is Nothing Then Continue For
                    Dim netBuyRow As Single = Single.NaN
                    If row.ContainsKey("netBuy") Then netBuyRow = CSng(SharedUtil.SafeDouble(row("netBuy"), True))
                    If Single.IsNaN(netBuyRow) AndAlso row.ContainsKey("value") Then netBuyRow = CSng(SharedUtil.SafeDouble(row("value"), True))
                    If Single.IsNaN(netBuyRow) AndAlso row.ContainsKey("net") Then netBuyRow = CSng(SharedUtil.SafeDouble(row("net"), True))
                    If Single.IsNaN(netBuyRow) AndAlso row.ContainsKey("순매수") Then netBuyRow = CSng(SharedUtil.SafeDouble(row("순매수"), True))
                    If Single.IsNaN(netBuyRow) Then Continue For

                    Dim dtRow = ParseCandleDateTime(row)
                    If Not RowHasDatePart(row) Then
                        dtRow = NormalizeTimeOnlyDate(dtRow, fallbackDtRows)
                    End If
                    If dtRow = DateTime.MinValue Then dtRow = fallbackDtRows
                    dtRow = AlignToCandleRangeDate(dtRow)

                    If addedCount = 0 Then
                        firstAddedDt = dtRow
                        firstAddedNet = netBuyRow
                        lastAddedDt = dtRow
                        lastAddedNet = netBuyRow
                    Else
                        If dtRow < firstAddedDt Then
                            firstAddedDt = dtRow
                            firstAddedNet = netBuyRow
                        End If
                        If dtRow > lastAddedDt Then
                            lastAddedDt = dtRow
                            lastAddedNet = netBuyRow
                        End If
                    End If
                    addedCount += 1

                    For Each ind In _indicatorEngine.GetAll()
                        Dim progInd = TryCast(ind, ProgramTrade_Indicator)
                        If progInd IsNot Nothing Then
                            progInd.AddData(dtRow, netBuyRow)
                            hasAdded = True
                        End If
                    Next
                Next
            End If
        End If
        If addedCount > 0 Then
            AppLogger.I.Info($"ProgramTrade parsed-range: {code} rows:{addedCount} first[{FormatDebugDateTime(firstAddedDt)},{FormatDebugSingle(firstAddedNet)}] last[{FormatDebugDateTime(lastAddedDt)},{FormatDebugSingle(lastAddedNet)}]", "Data")
        End If

        Dim netBuy As Single = Single.NaN
        If m.Has("netBuy") Then netBuy = m.Sng("netBuy")
        If Single.IsNaN(netBuy) AndAlso m.Has("value") Then netBuy = m.Sng("value")
        If Single.IsNaN(netBuy) AndAlso m.Has("net") Then netBuy = m.Sng("net")
        If Single.IsNaN(netBuy) AndAlso m.Has("순매수") Then netBuy = CSng(SharedUtil.SafeDouble(m.Str("순매수"), True))
        If Single.IsNaN(netBuy) AndAlso Not hasAdded Then Return

        Dim fallbackDt = If(_candles IsNot Nothing AndAlso _candles.Count > 0, _candles(_candles.Count - 1).Dt, DateTime.Now)
        Dim dt As DateTime = ParseMsgDateTime(m, fallbackDt)
        dt = AlignToCandleRangeDate(dt)

        If Not Single.IsNaN(netBuy) Then
            For Each ind In _indicatorEngine.GetAll()
                Dim progInd = TryCast(ind, ProgramTrade_Indicator)
                If progInd IsNot Nothing Then
                    progInd.AddData(dt, netBuy)
                End If
            Next
        End If

        If _candles Is Nothing OrElse _candles.Count = 0 Then Return
        If InvokeRequired Then
            BeginInvoke(Sub()
                            If hasAdded Then
                                _indicatorEngine.CalculateAll(_candles)
                            Else
                                _indicatorEngine.UpdateLast(_candles)
                            End If
                            LogProgramTradeSyncState(code, addedCount, firstAddedDt, firstAddedNet, lastAddedDt, lastAddedNet)
                            _needsRepaint = True
                        End Sub)
        Else
            If hasAdded Then
                _indicatorEngine.CalculateAll(_candles)
            Else
                _indicatorEngine.UpdateLast(_candles)
            End If
            LogProgramTradeSyncState(code, addedCount, firstAddedDt, firstAddedNet, lastAddedDt, lastAddedNet)
            _needsRepaint = True
        End If
    End Sub

    Private Sub OnTradeStrength(m As Msg)
        Dim code = If(m.Has("stockCode"), m.Str("stockCode"), m.Str("code"))
        If code <> _stockCode Then Return

        Dim hasAdded As Boolean = False
        If m.Has("rows") Then
            Dim rows = m.DictList("rows")
            If rows IsNot Nothing Then
                Dim fallbackDtRows = If(_candles IsNot Nothing AndAlso _candles.Count > 0, _candles(_candles.Count - 1).Dt, DateTime.Now)
                For Each row In rows
                    If row Is Nothing Then Continue For
                    Dim strengthRow As Single = Single.NaN
                    If row.ContainsKey("strength") Then strengthRow = CSng(SharedUtil.SafeDouble(row("strength"), True))
                    If Single.IsNaN(strengthRow) AndAlso row.ContainsKey("value") Then strengthRow = CSng(SharedUtil.SafeDouble(row("value"), True))
                    If Single.IsNaN(strengthRow) AndAlso row.ContainsKey("체결강도") Then strengthRow = CSng(SharedUtil.SafeDouble(row("체결강도"), True))
                    If Single.IsNaN(strengthRow) Then Continue For

                    Dim dtRow = ParseCandleDateTime(row)
                    If Not RowHasDatePart(row) Then
                        dtRow = NormalizeTimeOnlyDate(dtRow, fallbackDtRows)
                    End If
                    If dtRow = DateTime.MinValue Then dtRow = fallbackDtRows

                    For Each ind In _indicatorEngine.GetAll()
                        Dim tsInd = TryCast(ind, TradeStrength_Indicator)
                        If tsInd IsNot Nothing Then
                            tsInd.AddData(dtRow, strengthRow)
                            hasAdded = True
                        End If
                    Next
                Next
            End If
        End If

        Dim strength As Single = Single.NaN
        If m.Has("strength") Then strength = m.Sng("strength")
        If Single.IsNaN(strength) AndAlso m.Has("value") Then strength = m.Sng("value")
        If Single.IsNaN(strength) AndAlso m.Has("체결강도") Then strength = CSng(SharedUtil.SafeDouble(m.Str("체결강도"), True))
        If Single.IsNaN(strength) AndAlso Not hasAdded Then Return

        Dim fallbackDt = If(_candles IsNot Nothing AndAlso _candles.Count > 0, _candles(_candles.Count - 1).Dt, DateTime.Now)
        Dim dt As DateTime = ParseMsgDateTime(m, fallbackDt)

        If Not Single.IsNaN(strength) Then
            For Each ind In _indicatorEngine.GetAll()
                Dim tsInd = TryCast(ind, TradeStrength_Indicator)
                If tsInd IsNot Nothing Then
                    tsInd.AddData(dt, strength)
                End If
            Next
        End If

        If _candles Is Nothing OrElse _candles.Count = 0 Then Return
        If InvokeRequired Then
            BeginInvoke(Sub()
                            If hasAdded Then
                                _indicatorEngine.CalculateAll(_candles)
                            Else
                                _indicatorEngine.UpdateLast(_candles)
                            End If
                            _needsRepaint = True
                        End Sub)
        Else
            If hasAdded Then
                _indicatorEngine.CalculateAll(_candles)
            Else
                _indicatorEngine.UpdateLast(_candles)
            End If
            _needsRepaint = True
        End If
    End Sub

    Private Sub OnSectorStocksResult(m As Msg)
        If String.IsNullOrWhiteSpace(_stockCode) Then Return
        If Not m.Has("rows") Then Return

        Dim rows = m.DictList("rows")
        If rows Is Nothing OrElse rows.Count = 0 Then Return

        Dim rank As Integer = 0
        Dim sectorChange As Single = 0
        For i As Integer = 0 To rows.Count - 1
            Dim row = rows(i)
            If row Is Nothing Then Continue For

            Dim rowCode As String = ""
            If row.ContainsKey("code") Then rowCode = row("code")
            If rowCode = "" AndAlso row.ContainsKey("종목코드") Then rowCode = row("종목코드")
            rowCode = SharedUtil.NormalizeCode(rowCode)
            If rowCode <> _stockCode Then Continue For

            rank = i + 1
            If row.ContainsKey("등락률") Then sectorChange = CSng(SharedUtil.SafeDouble(row("등락률"), True))
            Exit For
        Next

        If rank <= 0 Then Return
        Dim totalStocks = rows.Count

        For Each ind In _indicatorEngine.GetAll()
            Dim secInd = TryCast(ind, SectorLeader_Indicator)
            If secInd IsNot Nothing Then
                secInd.UpdateSnapshot(1, 1, rank, totalStocks, sectorChange)
            End If
        Next

        If _candles Is Nothing OrElse _candles.Count = 0 Then Return
        If InvokeRequired Then
            BeginInvoke(Sub()
                            _indicatorEngine.UpdateLast(_candles)
                            _needsRepaint = True
                        End Sub)
        Else
            _indicatorEngine.UpdateLast(_candles)
            _needsRepaint = True
        End If
    End Sub

    Private Sub RequestAuxiliaryIndicatorData()
        If String.IsNullOrWhiteSpace(_stockCode) Then Return
        Dim candleCount = If(_candles Is Nothing, 0, _candles.Count)
        If candleCount <= 0 Then Return

        Dim needTickIntensity = _indicatorEngine.GetAll().Any(Function(i) TypeOf i Is TickIntensity_Indicator)
        Dim needProgram = _indicatorEngine.GetAll().Any(Function(i) TypeOf i Is ProgramTrade_Indicator)
        Dim needSector = _indicatorEngine.GetAll().Any(Function(i) TypeOf i Is SectorLeader_Indicator)

        If needTickIntensity AndAlso Not _tickAuxRequested Then
            Dim tickUnit = RuntimeChartSettings.DefaultTickUnit
            Dim firstDt = _candles(0).Dt
            Dim lastDt = _candles(_candles.Count - 1).Dt
            Dim spanMin = Math.Max(1, CInt((lastDt - firstDt).TotalMinutes) + 1)
            Dim estCount = Math.Max(RuntimeChartSettings.TickRequestMinCount,
                                    Math.Min(RuntimeChartSettings.TickRequestMaxCount, spanMin * RuntimeChartSettings.TickRequestMultiplier))
            Dim tickCount = estCount
            _lastTickCandleRequestCount = tickCount
            _tickCandleRetryCount = 0
            _tickAuxRequested = True
            MessageBus.I.Emit(Topics.TICK_CANDLE_REQUEST,
                              "code", _stockCode,
                              "provider", RuntimeChartSettings.MarketDataProvider,
                              "count", tickCount,
                              "tickUnit", tickUnit,
                              "timeframe", RuntimeChartSettings.TickTimeframe(tickUnit),
                              "stopTime", GetTickStopTime())
        End If

        If needProgram AndAlso Not _programAuxRequested Then
            Dim reqCount = Math.Max(20, Math.Min(RuntimeChartSettings.ProgramTradeRequestCount, candleCount * 2))
            _programAuxRequested = True
            Dim baseDate = _candles(_candles.Count - 1).Dt.ToString("yyyyMMdd")
            MessageBus.I.Emit(Topics.PROGRAM_TRADE_REQUEST,
                              "code", _stockCode,
                              "provider", RuntimeChartSettings.MarketDataProvider,
                              "count", reqCount,
                              "stopTime", GetTickStopTime(),
                              "baseDate", baseDate)
            If Not _programRtSubscribed Then
                _programRtSubscribed = True
                MessageBus.I.Emit("program.trade.rt.subscribe",
                                  "code", _stockCode,
                                  "provider", RuntimeChartSettings.MarketDataProvider)
            End If
        End If

        If needSector AndAlso Not _sectorAuxRequested Then
            Dim sectorCode = GuessSectorCode()
            If sectorCode <> "" Then
                _sectorAuxRequested = True
                MessageBus.I.Emit(Topics.SECTOR_STOCKS_REQUEST, "sectorCode", sectorCode)
            End If
        End If
    End Sub

    Private Function GuessSectorCode() As String
        Dim item = StockInfoManager.I.GetItem(_stockCode)
        If item Is Nothing Then Return ""
        If String.IsNullOrWhiteSpace(item.SourceDetail) Then Return ""

        Dim tokens = item.SourceDetail.Split(","c)
        For Each t In tokens
            Dim s = t.Trim()
            If s = "" Then Continue For
            If s.All(Function(ch) Char.IsDigit(ch)) Then Return s
        Next
        Return ""
    End Function

    Private Function GetTickStopTime() As String
        If _candles Is Nothing OrElse _candles.Count = 0 Then
            Dim fallback = TradingCalendar.ResolveStopTime(Nothing)
            Return fallback.ToString("yyyyMMddHHmmss")
        End If

        Dim dtList As New List(Of DateTime)(_candles.Count)
        For Each c In _candles
            If c Is Nothing Then Continue For
            If c.Dt = DateTime.MinValue Then Continue For
            dtList.Add(c.Dt)
        Next
        If dtList.Count = 0 Then
            Dim fallback = TradingCalendar.ResolveStopTime(Nothing)
            Return fallback.ToString("yyyyMMddHHmmss")
        End If

        Dim normalized = TradingCalendar.ResolveStopTime(dtList)
        Return normalized.ToString("yyyyMMddHHmmss")
    End Function

    Private Shared Function NormalizeTimeOnlyDate(parsed As DateTime, fallback As DateTime) As DateTime
        If parsed = DateTime.MinValue Then Return DateTime.MinValue
        Dim isTodayOnly = (parsed.Date = DateTime.Today)
        If Not isTodayOnly Then Return parsed
        Dim baseDate = If(fallback = DateTime.MinValue, DateTime.Today, fallback.Date)
        Return New DateTime(baseDate.Year, baseDate.Month, baseDate.Day, parsed.Hour, parsed.Minute, parsed.Second)
    End Function

    Private Function AlignToCandleRangeDate(dt As DateTime) As DateTime
        If dt = DateTime.MinValue Then Return dt
        If _candles Is Nothing OrElse _candles.Count = 0 Then Return dt

        Dim firstDt = _candles(0).Dt
        Dim lastDt = _candles(_candles.Count - 1).Dt
        If firstDt = DateTime.MinValue OrElse lastDt = DateTime.MinValue Then Return dt

        If dt.Date < firstDt.Date OrElse dt.Date > lastDt.Date Then
            Return New DateTime(lastDt.Year, lastDt.Month, lastDt.Day, dt.Hour, dt.Minute, dt.Second)
        End If
        Return dt
    End Function

    Private Sub LogProgramTradeSyncState(code As String,
                                         addedCount As Integer,
                                         firstAddedDt As DateTime,
                                         firstAddedNet As Single,
                                         lastAddedDt As DateTime,
                                         lastAddedNet As Single)
        Dim progResults As List(Of IndicatorResult) = Nothing
        If Not _indicatorEngine.Results.TryGetValue("PROG_TRADE", progResults) OrElse progResults Is Nothing Then
            AppLogger.I.Info($"ProgramTrade sync-range: {code} results:0 sourceRows:{addedCount}", "Data")
            Return
        End If

        Dim resultCount = progResults.Count
        Dim firstVal As Single = Single.NaN
        Dim lastVal As Single = Single.NaN
        Dim firstDt As DateTime = DateTime.MinValue
        Dim lastDt As DateTime = DateTime.MinValue
        If resultCount > 0 Then
            firstVal = progResults(0).Val("NetBuy")
            lastVal = progResults(resultCount - 1).Val("NetBuy")
            If _candles IsNot Nothing AndAlso _candles.Count > 0 Then
                firstDt = _candles(0).Dt
                lastDt = _candles(_candles.Count - 1).Dt
            End If
        End If

        Dim firstValidIdx As Integer = -1
        Dim lastValidIdx As Integer = -1
        Dim validCount As Integer = 0
        For i As Integer = 0 To resultCount - 1
            Dim v = progResults(i).Val("NetBuy")
            If Single.IsNaN(v) Then Continue For
            validCount += 1
            If firstValidIdx < 0 Then firstValidIdx = i
            lastValidIdx = i
        Next

        Dim firstValidText As String = "n/a"
        Dim lastValidText As String = "n/a"
        If firstValidIdx >= 0 AndAlso firstValidIdx < resultCount Then
            Dim dtText = If(_candles IsNot Nothing AndAlso firstValidIdx < _candles.Count, FormatDebugDateTime(_candles(firstValidIdx).Dt), $"idx:{firstValidIdx}")
            firstValidText = $"{dtText},{FormatDebugSingle(progResults(firstValidIdx).Val("NetBuy"))}"
        End If
        If lastValidIdx >= 0 AndAlso lastValidIdx < resultCount Then
            Dim dtText = If(_candles IsNot Nothing AndAlso lastValidIdx < _candles.Count, FormatDebugDateTime(_candles(lastValidIdx).Dt), $"idx:{lastValidIdx}")
            lastValidText = $"{dtText},{FormatDebugSingle(progResults(lastValidIdx).Val("NetBuy"))}"
        End If

        AppLogger.I.Info($"ProgramTrade sync-range: {code} sourceRows:{addedCount} sourceFirst[{FormatDebugDateTime(firstAddedDt)},{FormatDebugSingle(firstAddedNet)}] sourceLast[{FormatDebugDateTime(lastAddedDt)},{FormatDebugSingle(lastAddedNet)}] arrFirst[{FormatDebugDateTime(firstDt)},{FormatDebugSingle(firstVal)}] arrLast[{FormatDebugDateTime(lastDt)},{FormatDebugSingle(lastVal)}] valid:{validCount}/{resultCount} firstValid[{firstValidText}] lastValid[{lastValidText}]", "Data")
    End Sub

    Private Shared Function FormatDebugDateTime(dt As DateTime) As String
        If dt = DateTime.MinValue Then Return "MinValue"
        Return dt.ToString("yyyy-MM-dd HH:mm:ss")
    End Function

    Private Shared Function FormatDebugSingle(v As Single) As String
        If Single.IsNaN(v) Then Return "NaN"
        If Single.IsInfinity(v) Then Return "INF"
        Return v.ToString("0.######")
    End Function

    Public Sub RefreshChartData()
        If String.IsNullOrWhiteSpace(_stockCode) Then Return

        _tickAuxRequested = False
        _programAuxRequested = False
        _programRtSubscribed = False
        _sectorAuxRequested = False
        If _chartHost IsNot Nothing Then
            _chartHost.RequestCandles(_stockCode, _chartType, _requestedCount)
        End If
        RequestAuxiliaryIndicatorData()
        _needsRepaint = True
    End Sub

    Private Sub OnStrategySignal(m As Msg)
        Dim code = m.Str("stockCode")
        If code <> _stockCode Then Return

        Dim sig As New StrategySignal With {
            .StockCode = code,
            .StrategyName = m.Str("strategy"),
            .Price = m.Sng("price"),
            .Reason = m.Str("reason"),
            .Timestamp = If(m.Has("time"), m.Dt("time"), DateTime.Now)
        }

        Dim sigType = m.Str("signal").ToUpper()
        Select Case sigType
            Case "BUY" : sig.SignalType = SignalType.Buy
            Case "SELL" : sig.SignalType = SignalType.Sell
            Case "STRONGBUY" : sig.SignalType = SignalType.StrongBuy
            Case "STRONGSELL" : sig.SignalType = SignalType.StrongSell
            Case Else : Return
        End Select

        If InvokeRequired Then
            BeginInvoke(Sub()
                            _signals.Add(sig)
                            _needsRepaint = True
                        End Sub)
        Else
            _signals.Add(sig)
            _needsRepaint = True
        End If
    End Sub

    Private Sub CalculateLayout()
        _totalWidth = _skControl.Width
        _totalHeight = _skControl.Height
        If _totalWidth < 1 OrElse _totalHeight < 1 Then Return

        Dim cL = MARGIN_LEFT
        Dim cR = _totalWidth - MARGIN_RIGHT
        Dim cT = MARGIN_TOP
        Dim cB = _totalHeight - MARGIN_BOTTOM
        Dim cH = cB - cT

        Dim panelCount = _panelIndicators.Count
        Dim panelTotalH As Single = 0
        If panelCount > 0 Then
            panelTotalH = cH * _vs.PanelHeightRatio * panelCount
            Dim maxPanelH = cH * 0.65F
            If panelTotalH > maxPanelH Then panelTotalH = maxPanelH
        End If

        Dim mainH = cH - panelTotalH
        Dim volumeH = mainH * VOLUME_RATIO
        mainH -= volumeH

        _mainRect = New SKRect(cL, cT, cR, cT + mainH)
        _volumeRect = New SKRect(cL, _mainRect.Bottom, cR, _mainRect.Bottom + volumeH)

        _panelRects.Clear()
        Dim pY = _volumeRect.Bottom
        Dim sPH As Single = 0
        If panelCount > 0 Then
            sPH = panelTotalH / panelCount
        End If

        For i As Integer = 0 To panelCount - 1
            _panelRects.Add(New SKRect(cL, pY + PANEL_SEPARATOR_H, cR, pY + sPH))
            pY += sPH
        Next
    End Sub

    Private Sub RebuildPanelLayout()
        _panelIndicators.Clear()
        Dim pm As New Dictionary(Of Integer, List(Of String))
        For Each ind In _indicatorEngine.GetAll()
            If ind.PanelIndex > 0 Then
                If Not pm.ContainsKey(ind.PanelIndex) Then
                    pm(ind.PanelIndex) = New List(Of String)
                End If
                pm(ind.PanelIndex).Add(ind.Name)
            End If
        Next
        For Each kv In pm.OrderBy(Function(x) x.Key)
            _panelIndicators.Add(kv.Value)
        Next
    End Sub

    Private Sub CalculatePriceRange()
        _priceHigh = Single.MinValue
        _priceLow = Single.MaxValue
        _volumeMax = 0

        Dim s = Math.Max(0, _vs.StartIndex)
        Dim en = Math.Min(_candles.Count - 1, _vs.EndIndex)
        If s > en Then
            _priceHigh = 100
            _priceLow = 0
            Return
        End If

        For i As Integer = s To en
            Dim c = _candles(i)
            If c.High > 0 Then
                If c.High > _priceHigh Then _priceHigh = c.High
                If c.Low < _priceLow Then _priceLow = c.Low
            End If
            If c.Volume > _volumeMax Then _volumeMax = c.Volume
        Next

        For Each ind In _indicatorEngine.GetAll()
            If ind.PanelIndex > 0 Then Continue For
            Dim results As List(Of IndicatorResult) = Nothing
            If Not _indicatorEngine.Results.TryGetValue(ind.Name, results) Then Continue For

            Dim maxI = Math.Min(en, results.Count - 1)
            For i As Integer = s To maxI
                Dim r = results(i)
                If r Is Nothing OrElse r.Values Is Nothing Then Continue For
                For Each kv In r.Values
                    If Not IsOverlayPriceValueKey(kv.Key) Then Continue For
                    Dim v = kv.Value
                    If Single.IsNaN(v) OrElse v <= 0 Then Continue For
                    If v > _priceHigh Then _priceHigh = v
                    If v < _priceLow Then _priceLow = v
                Next
            Next
        Next

        ' 만약 유효한 가격을 찾지 못한 경우 (데이터 오류 등) 기본값 설정
        If _priceHigh = Single.MinValue OrElse _priceLow = Single.MaxValue Then
            _priceHigh = 100
            _priceLow = 0
        End If

        Dim margin = (_priceHigh - _priceLow) * 0.05F
        If margin < 1 Then margin = 1

        If _isAutoScaleY OrElse _manualMaxP = 0 OrElse _manualMinP = 0 Then
            _priceHigh += margin
            _priceLow -= margin
            _manualMaxP = _priceHigh
            _manualMinP = _priceLow
        Else
            _priceHigh = _manualMaxP
            _priceLow = _manualMinP
        End If

        If _volumeMax = 0 Then _volumeMax = 1
    End Sub

    Private Shared Function ParseCandleDateTime(row As Dictionary(Of String, String)) As DateTime
        If row Is Nothing Then Return DateTime.MinValue

        Dim dt As DateTime = DateTime.MinValue
        If row.ContainsKey("dt") Then dt = SharedUtil.ToDateTime(row("dt"))
        If dt = DateTime.MinValue AndAlso row.ContainsKey("date") Then dt = SharedUtil.ToDateTime(row("date"))

        Dim tm As String = ""
        If row.ContainsKey("time") Then tm = row("time")
        If tm = "" AndAlso row.ContainsKey("hhmm") Then tm = row("hhmm")
        If tm = "" AndAlso row.ContainsKey("체결시간") Then tm = row("체결시간")
        If tm = "" AndAlso row.ContainsKey("시간") Then tm = row("시간")

        If dt <> DateTime.MinValue AndAlso dt.TimeOfDay.TotalSeconds > 0 Then Return dt
        If String.IsNullOrWhiteSpace(tm) Then Return dt

        Dim digits = NormalizeHHmmssDigits(tm)
        If digits.Length < 6 Then Return dt

        Dim hh As Integer
        Dim mm As Integer
        Dim ss As Integer
        If Not Integer.TryParse(digits.Substring(0, 2), hh) Then Return dt
        If Not Integer.TryParse(digits.Substring(2, 2), mm) Then Return dt
        If Not Integer.TryParse(digits.Substring(4, 2), ss) Then Return dt
        hh = Math.Max(0, Math.Min(23, hh))
        mm = Math.Max(0, Math.Min(59, mm))
        ss = Math.Max(0, Math.Min(59, ss))

        Dim baseDate = If(dt = DateTime.MinValue, DateTime.Today, dt.Date)
        Return New DateTime(baseDate.Year, baseDate.Month, baseDate.Day, hh, mm, ss)
    End Function

    Private Shared Function ParseMsgDateTime(m As Msg, fallback As DateTime) As DateTime
        If m Is Nothing Then Return fallback

        Dim keys As String() = {"dt", "datetime", "dateTime", "time", "체결시간", "시간", "hhmm", "hhmmss"}
        For Each key In keys
            If Not m.Has(key) Then Continue For
            Dim raw = m.Str(key)
            If String.IsNullOrWhiteSpace(raw) Then Continue For

            Dim parsed = SharedUtil.ToDateTime(raw)
            If parsed <> DateTime.MinValue Then
                Dim isTimeOnlyDigits As Boolean = True
                For Each ch In raw
                    If Not Char.IsDigit(ch) Then
                        isTimeOnlyDigits = False
                        Exit For
                    End If
                Next
                If parsed.Date = DateTime.Today AndAlso fallback <> DateTime.MinValue AndAlso isTimeOnlyDigits AndAlso raw.Length <= 6 Then
                    Return New DateTime(fallback.Year, fallback.Month, fallback.Day, parsed.Hour, parsed.Minute, parsed.Second)
                End If
                Return parsed
            End If

            Dim digits = NormalizeHHmmssDigits(raw)
            If digits.Length = 6 Then
                Dim hh As Integer
                Dim mm As Integer
                Dim ss As Integer
                If Integer.TryParse(digits.Substring(0, 2), hh) AndAlso
                   Integer.TryParse(digits.Substring(2, 2), mm) AndAlso
                   Integer.TryParse(digits.Substring(4, 2), ss) Then
                    hh = Math.Max(0, Math.Min(23, hh))
                    mm = Math.Max(0, Math.Min(59, mm))
                    ss = Math.Max(0, Math.Min(59, ss))
                    Dim baseDate = If(fallback = DateTime.MinValue, DateTime.Today, fallback.Date)
                    Return New DateTime(baseDate.Year, baseDate.Month, baseDate.Day, hh, mm, ss)
                End If
            End If
        Next

        Return fallback
    End Function

    Private Function AlignTickToCurrentBar(tickTime As DateTime) As DateTime
        If tickTime = DateTime.MinValue Then tickTime = DateTime.Now

        Dim tf = RuntimeChartSettings.NormalizeMinuteTimeframe(RuntimeChartSettings.DefaultCandleTimeframe)
        If String.IsNullOrWhiteSpace(tf) OrElse Not tf.StartsWith("m", StringComparison.OrdinalIgnoreCase) Then
            Return New DateTime(tickTime.Year, tickTime.Month, tickTime.Day, tickTime.Hour, tickTime.Minute, 0)
        End If

        Dim minuteUnit As Integer = 1
        If tf.Length > 1 Then Integer.TryParse(tf.Substring(1), minuteUnit)
        If minuteUnit <= 0 Then minuteUnit = 1

        Dim bucketMinute = (tickTime.Minute \ minuteUnit) * minuteUnit
        Return New DateTime(tickTime.Year, tickTime.Month, tickTime.Day, tickTime.Hour, bucketMinute, 0)
    End Function

    Private Shared Function ShouldStartNewRealtimeBar(lastBarTime As DateTime, currentBarTime As DateTime) As Boolean
        If lastBarTime = DateTime.MinValue Then Return False
        Return currentBarTime > lastBarTime
    End Function

    Private Shared Function NormalizeHHmmssDigits(raw As String) As String
        Dim digits = New String(If(raw, "").Where(Function(ch) Char.IsDigit(ch)).ToArray())
        If digits.Length = 0 Then Return ""
        If digits.Length <= 2 Then
            Return digits.PadLeft(2, "0"c) & "0000"
        End If
        If digits.Length = 3 OrElse digits.Length = 4 Then
            Return digits.PadLeft(4, "0"c) & "00"
        End If
        If digits.Length = 5 Then
            Return digits.PadLeft(6, "0"c)
        End If
        Return digits.Substring(0, 6)
    End Function

    Friend Function IndexToX(candleIndex As Integer) As Single
        Return _mainRect.Left + (candleIndex - _vs.StartIndex) * (_vs.CandleWidth + _vs.Gap) + _vs.CandleWidth / 2
    End Function

    Private Function XToIndex(x As Single) As Integer
        Return _vs.StartIndex + CInt(Math.Floor((x - _mainRect.Left) / (_vs.CandleWidth + _vs.Gap)))
    End Function

    Friend Function PriceToY(price As Single) As Single
        If _priceHigh = _priceLow Then Return _mainRect.MidY
        Return _mainRect.Top + (_priceHigh - price) / (_priceHigh - _priceLow) * _mainRect.Height
    End Function

    Private Function YToPrice(y As Single) As Single
        If _mainRect.Height = 0 Then Return 0
        Return _priceHigh - (y - _mainRect.Top) / _mainRect.Height * (_priceHigh - _priceLow)
    End Function

    Private Function VolumeToY(vol As Long) As Single
        If _volumeMax = 0 Then Return _volumeRect.Bottom
        Return _volumeRect.Bottom - CSng(vol) / _volumeMax * _volumeRect.Height
    End Function

    Private Function PanelValueToY(value As Single, panelIdx As Integer, pMin As Single, pMax As Single) As Single
        If panelIdx >= _panelRects.Count Then Return 0
        Dim rect = _panelRects(panelIdx)
        If pMax = pMin Then Return rect.MidY
        Return rect.Top + (pMax - value) / (pMax - pMin) * rect.Height
    End Function

    Private Sub OnFrameTimer(sender As Object, e As EventArgs)
        If Not _needsRepaint Then Return
        _needsRepaint = False
        _skControl.Invalidate()
    End Sub

    Private Sub SKControl_PaintSurface(sender As Object, e As SKPaintSurfaceEventArgs) Handles _skControl.PaintSurface
        Dim canvas = e.Surface.Canvas
        canvas.Clear(ColBackground)
        If _candles.Count = 0 Then
            DrawEmptyMessage(canvas)
            Return
        End If

        CalculateLayout()
        CalculatePriceRange()

        DrawGrid(canvas)
        DrawVolume(canvas)
        DrawCandles(canvas)
        DrawOverlayIndicators(canvas)
        DrawPanels(canvas)

        If _showCurrentPriceLine Then DrawCurrentPriceLine(canvas)
        If _showViLine Then DrawViLine(canvas)
        If _showPrevCloseLine Then DrawPrevCloseLine(canvas)

        DrawSignals(canvas)
        DrawAxisY(canvas)
        DrawAxisX(canvas)

        If _mouseInside AndAlso _vs.ShowCrosshair Then
            DrawCrosshair(canvas)
        End If

        DrawStockInfo(canvas)
        DrawLegends(canvas)
    End Sub

    Private Sub DrawEmptyMessage(canvas As SKCanvas)
        Using paint As New SKPaint()
            paint.Color = ColAxisText
            paint.TextSize = 16
            paint.IsAntialias = True
            paint.TextAlign = SKTextAlign.Center
            paint.Typeface = SKTypeface.FromFamilyName("맑은 고딕")

            Dim msg = ""
            If String.IsNullOrEmpty(_stockCode) Then
                msg = "종목을 선택하세요"
            Else
                msg = $"{_stockCode} 데이터 로딩 중..."
            End If
            canvas.DrawText(msg, _totalWidth / 2, _totalHeight / 2, paint)
        End Using
    End Sub

    Private Sub DrawGrid(canvas As SKCanvas)
        Dim priceRange = _priceHigh - _priceLow
        Dim gridStep = CalculateNiceStep(priceRange, 7)
        Dim p As Single = CSng(Math.Ceiling(_priceLow / gridStep) * gridStep)
        While p < _priceHigh
            Dim y = PriceToY(p)
            If y >= _mainRect.Top AndAlso y <= _mainRect.Bottom Then
                canvas.DrawLine(_mainRect.Left, y, _mainRect.Right, y, _paintGrid)
            End If
            p += CSng(gridStep)
        End While

        Dim s = Math.Max(0, _vs.StartIndex)
        Dim endI = Math.Min(_candles.Count - 1, _vs.EndIndex)
        If endI >= s Then
            Dim minuteStep = GetAxisMinuteStep(s, endI)
            For i As Integer = s To endI
                If i < 0 OrElse i >= _candles.Count Then Continue For
                Dim dt = _candles(i).Dt
                If dt = DateTime.MinValue Then Continue For
                If Not ShouldDrawAxisTick(i, dt, minuteStep, s) Then Continue For
                Dim x = IndexToX(i)
                If x >= _mainRect.Left AndAlso x <= _mainRect.Right Then
                    canvas.DrawLine(x, _mainRect.Top, x, _totalHeight - MARGIN_BOTTOM, _paintGrid)
                End If
            Next
        End If

        If _showDayChangeLines AndAlso endI > s Then
            Using dayPaint As New SKPaint()
                dayPaint.Style = SKPaintStyle.Stroke
                dayPaint.Color = New SKColor(120, 130, 155, 150)
                dayPaint.StrokeWidth = 1
                dayPaint.PathEffect = SKPathEffect.CreateDash({3, 3}, 0)
                For i As Integer = Math.Max(1, s) To endI
                    If i >= _candles.Count Then Exit For
                    Dim prevDt = _candles(i - 1).Dt
                    Dim curDt = _candles(i).Dt
                    If prevDt = DateTime.MinValue OrElse curDt = DateTime.MinValue Then Continue For
                    If prevDt.Date = curDt.Date Then Continue For
                    Dim x = IndexToX(i)
                    If x >= _mainRect.Left AndAlso x <= _mainRect.Right Then
                        canvas.DrawLine(x, _mainRect.Top, x, _totalHeight - MARGIN_BOTTOM, dayPaint)
                    End If
                Next
            End Using
        End If
    End Sub

    Private Shared Function CalculateNiceStep(range As Single, targetLines As Integer) As Double
        If range <= 0 OrElse targetLines <= 0 Then Return 1
        Dim rawStep = range / targetLines
        Dim magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)))
        Dim normalized = rawStep / magnitude
        Dim niceNorm As Double
        If normalized <= 1 Then
            niceNorm = 1
        ElseIf normalized <= 2 Then
            niceNorm = 2
        ElseIf normalized <= 5 Then
            niceNorm = 5
        Else
            niceNorm = 10
        End If
        Return niceNorm * magnitude
    End Function

    Private Sub DrawVolume(canvas As SKCanvas)
        Dim s = Math.Max(0, _vs.StartIndex)
        Dim en = Math.Min(_candles.Count - 1, _vs.EndIndex)
        For i As Integer = s To en
            Dim c = _candles(i)
            Dim x = IndexToX(i)
            Dim halfW = _vs.CandleWidth / 2 - 0.5F
            Dim yTop = VolumeToY(c.Volume)
            Dim yBot = _volumeRect.Bottom
            Dim paint = If(c.Close >= c.Open, _paintVolBull, _paintVolBear)
            canvas.DrawRect(x - halfW, yTop, _vs.CandleWidth - 1, yBot - yTop, paint)
        Next
    End Sub

    Private Sub DrawCandles(canvas As SKCanvas)
        Dim s = Math.Max(0, _vs.StartIndex)
        Dim en = Math.Min(_candles.Count - 1, _vs.EndIndex)
        For i As Integer = s To en
            Dim c = _candles(i)
            Dim x = IndexToX(i)
            Dim halfW = _vs.CandleWidth / 2 - 0.5F
            Dim isBull = (c.Close >= c.Open)
            Dim bodyTop = PriceToY(If(isBull, c.Close, c.Open))
            Dim bodyBot = PriceToY(If(isBull, c.Open, c.Close))
            If bodyBot - bodyTop < 1 Then bodyBot = bodyTop + 1
            Dim wickPaint = If(isBull, _paintBullWick, _paintBearWick)

            canvas.DrawLine(x, PriceToY(c.High), x, PriceToY(c.Low), wickPaint)
            If _vs.CandleWidth >= 3 Then
                Dim bodyPaint = If(isBull, _paintBullBody, _paintBearBody)
                canvas.DrawRect(x - halfW, bodyTop, _vs.CandleWidth - 1, bodyBot - bodyTop, bodyPaint)
            Else
                canvas.DrawLine(x, bodyTop, x, bodyBot, wickPaint)
            End If
        Next
    End Sub

    Private Sub DrawOverlayIndicators(canvas As SKCanvas)
        Dim colorIdx = 0
        Dim s = Math.Max(0, _vs.StartIndex)
        Dim en = Math.Min(_candles.Count - 1, _vs.EndIndex)
        For Each ind In _indicatorEngine.GetAll()
            If ind.PanelIndex > 0 Then Continue For
            Dim results As List(Of IndicatorResult) = Nothing
            If Not _indicatorEngine.Results.TryGetValue(ind.Name, results) Then Continue For
            If results Is Nothing OrElse results.Count = 0 Then Continue For

            Dim sampleR = results.FirstOrDefault(Function(r) r IsNot Nothing AndAlso r.Values IsNot Nothing AndAlso r.Values.Count > 0)
            If sampleR Is Nothing Then Continue For

            For Each valueKey In sampleR.Values.Keys
                If Not IsOverlayPriceValueKey(valueKey) Then Continue For
                Dim paint = GetIndicatorPaint(ind.Name & "_" & valueKey, colorIdx)
                colorIdx += 1
                _reusePath.Reset()
                Dim started = False
                Dim maxI = Math.Min(en, results.Count - 1)
                For i As Integer = s To maxI
                    Dim r = results(i)
                    If r Is Nothing OrElse r.Values Is Nothing Then Continue For
                    If Not r.Values.ContainsKey(valueKey) Then Continue For
                    Dim v = r.Values(valueKey)
                    If Single.IsNaN(v) OrElse v <= 0 Then
                        started = False
                        Continue For
                    End If
                    Dim px = IndexToX(i)
                    Dim py = PriceToY(v)
                    If Not started Then
                        _reusePath.MoveTo(px, py)
                        started = True
                    Else
                        _reusePath.LineTo(px, py)
                    End If
                Next
                If started Then canvas.DrawPath(_reusePath, paint)
            Next
        Next
    End Sub

    Private Shared Function IsOverlayPriceValueKey(valueKey As String) As Boolean
        If String.IsNullOrWhiteSpace(valueKey) Then Return False

        Select Case valueKey.ToUpperInvariant()
            Case "VALUE", "UP", "DOWN", "MIDDLE",
                 "UPPER", "LOWER",
                 "UPPER1", "UPPER2", "LOWER1", "LOWER2",
                 "UPPERBAND", "LOWERBAND"
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Function GetIndicatorPaint(key As String, colorIndex As Integer) As SKPaint
        If _indicatorPaints.ContainsKey(key) Then Return _indicatorPaints(key)
        Dim c = IndicatorColors(colorIndex Mod IndicatorColors.Length)
        Dim p As New SKPaint()
        p.Style = SKPaintStyle.Stroke
        p.Color = c
        p.StrokeWidth = 1.5F
        p.IsAntialias = True
        _indicatorPaints(key) = p
        Return p
    End Function

    Private Sub DrawPanels(canvas As SKCanvas)
        _panelRanges.Clear()
        _panelLeftRanges.Clear()
        For panelIdx As Integer = 0 To _panelIndicators.Count - 1
            If panelIdx >= _panelRects.Count Then Exit For
            Dim rect = _panelRects(panelIdx)
            Dim indNames = _panelIndicators(panelIdx)
            canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Top, _paintPanelBorder)

            Dim pMin As Single = Single.MaxValue
            Dim pMax As Single = Single.MinValue
            Dim leftMin As Single = Single.MaxValue
            Dim leftMax As Single = Single.MinValue
            Dim hasLeftAxis As Boolean = False
            Dim s = Math.Max(0, _vs.StartIndex)
            Dim en = Math.Min(_candles.Count - 1, _vs.EndIndex)

            For Each indName In indNames
                Dim results As List(Of IndicatorResult) = Nothing
                If Not _indicatorEngine.Results.TryGetValue(indName, results) Then Continue For
                Dim maxI = Math.Min(en, results.Count - 1)
                For i As Integer = s To maxI
                    Dim r = results(i)
                    If r Is Nothing OrElse r.Values Is Nothing Then Continue For
                    For Each kv In r.Values
                        Dim v = kv.Value
                        If Single.IsNaN(v) Then Continue For
                        If IsPanelLeftAxisKey(indName, kv.Key) Then
                            hasLeftAxis = True
                            If v < leftMin Then leftMin = v
                            If v > leftMax Then leftMax = v
                            Continue For
                        End If
                        If v < pMin Then pMin = v
                        If v > pMax Then pMax = v
                    Next
                Next
            Next

            ' 서브 패널 유효 범위 보정
            If pMin = Single.MaxValue Then pMin = 0
            If pMax = Single.MinValue Then pMax = 100

            Dim mg = (pMax - pMin) * 0.1F
            If mg < 0.01F Then mg = 1
            pMin -= mg
            pMax += mg
            _panelRanges.Add(Tuple.Create(pMin, pMax))

            If hasLeftAxis AndAlso leftMin <> Single.MaxValue AndAlso leftMax <> Single.MinValue Then
                Dim lmg = (leftMax - leftMin) * 0.1F
                If lmg < 0.01F Then lmg = 1
                leftMin -= lmg
                leftMax += lmg
                _panelLeftRanges.Add(Tuple.Create(leftMin, leftMax))
            Else
                _panelLeftRanges.Add(Tuple.Create(Single.NaN, Single.NaN))
            End If

            DrawPanelReferenceLine(canvas, rect, pMin, pMax, 0, panelIdx)

            Dim cIdx = panelIdx * 3
            For Each indName In indNames
                Dim results As List(Of IndicatorResult) = Nothing
                If Not _indicatorEngine.Results.TryGetValue(indName, results) Then Continue For
                Dim sr = results.FirstOrDefault(Function(r) r IsNot Nothing AndAlso r.Values IsNot Nothing AndAlso r.Values.Count > 0)
                If sr Is Nothing Then Continue For
                For Each vk In sr.Values.Keys
                    Dim paint = GetIndicatorPaint(indName & "_P_" & vk, cIdx)
                    cIdx += 1
                    Dim isHist = (vk.ToUpper().Contains("HIST") OrElse vk.ToUpper().Contains("BAR"))
                    If indName.StartsWith("TICKINT_", StringComparison.OrdinalIgnoreCase) AndAlso
                       vk.Equals("TickSum", StringComparison.OrdinalIgnoreCase) Then
                        isHist = True
                    End If
                    Dim isLeftAxis = IsPanelLeftAxisKey(indName, vk)
                    If isHist Then
                        DrawPanelHistogram(canvas, results, s, en, vk, rect, pMin, pMax, panelIdx)
                    Else
                        If isLeftAxis AndAlso panelIdx < _panelLeftRanges.Count Then
                            Dim lr = _panelLeftRanges(panelIdx)
                            If Not Single.IsNaN(lr.Item1) AndAlso Not Single.IsNaN(lr.Item2) Then
                                DrawPanelLine(canvas, results, s, en, vk, paint, lr.Item1, lr.Item2, panelIdx)
                            Else
                                DrawPanelLine(canvas, results, s, en, vk, paint, pMin, pMax, panelIdx)
                            End If
                        Else
                            DrawPanelLine(canvas, results, s, en, vk, paint, pMin, pMax, panelIdx)
                        End If
                    End If
                Next
            Next

            Using lp As New SKPaint()
                lp.Color = ColAxisText
                lp.TextSize = 10
                lp.IsAntialias = True
                canvas.DrawText(String.Join(", ", indNames), rect.Left + 4, rect.Top + 14, lp)
            End Using
            DrawPanelAxisY(canvas, rect, pMin, pMax)
            If panelIdx < _panelLeftRanges.Count Then
                Dim lr = _panelLeftRanges(panelIdx)
                If Not Single.IsNaN(lr.Item1) AndAlso Not Single.IsNaN(lr.Item2) Then
                    DrawPanelAxisYLeft(canvas, rect, lr.Item1, lr.Item2)
                End If
            End If
        Next
    End Sub

    Private Sub DrawLegends(canvas As SKCanvas)
        _legendHits.Clear()
        Dim startX As Single = _mainRect.Left + 8
        Dim startY As Single = _mainRect.Top + 45 ' 주식 정보 아래 

        ' 보이기/숨기기 가능하게 개별 지표 범례 그림
        For Each ind In _indicatorEngine.GetAll()
            DrawLegendItem(canvas, ind.Name, ind.DisplayName, startX, startY)
            startY += 18
        Next

        ' 전략 범례 추가
        For Each strat In _appliedStrategies
            Dim stratKey = "STRAT_" & strat.Name
            DrawLegendItem(canvas, stratKey, "[전략] " & strat.Name, startX, startY, True)
            startY += 18
        Next
    End Sub

    Private Sub DrawLegendItem(canvas As SKCanvas, name As String, label As String, x As Single, y As Single, Optional isStrategy As Boolean = False)
        Dim isSelected = (name = _selectedIndicatorName)
        Using p As New SKPaint()
            p.Color = If(isSelected, SKColors.White, ColAxisText)
            p.TextSize = 11
            p.IsAntialias = True
            p.FakeBoldText = isSelected

            Dim displayLabel = label
            If isStrategy Then
                ' 전략의 경우 상태 표시
                Dim stratName = name.Substring(6)
                Dim strat = _appliedStrategies.FirstOrDefault(Function(s) s.Name = stratName)
                If strat IsNot Nothing Then
                    displayLabel &= If(strat.IsActive, $" [{strat.Mode}]", " [OFF]")
                End If
            End If

            Dim tw = p.MeasureText(displayLabel)
            Dim rect = New SKRect(x, y - 12, x + tw + 10, y + 4)

            If isSelected Then
                Using bgP As New SKPaint With {.Color = New SKColor(60, 60, 70, 180), .Style = SKPaintStyle.Fill}
                    canvas.DrawRect(rect, bgP)
                End Using
            End If

            canvas.DrawText(displayLabel, x + 5, y, p)
            _legendHits.Add(New LegendHitItem With {.Name = name, .Rect = rect})
        End Using
    End Sub

    Private Sub DrawPanelLine(canvas As SKCanvas, results As List(Of IndicatorResult), s As Integer, en As Integer, vk As String, paint As SKPaint, pMin As Single, pMax As Single, pIdx As Integer)
        _reusePath.Reset()
        Dim started = False
        Dim maxI = Math.Min(en, results.Count - 1)
        For i As Integer = s To maxI
            Dim r = results(i)
            If r Is Nothing OrElse r.Values Is Nothing Then Continue For
            If Not r.Values.ContainsKey(vk) Then Continue For
            Dim v = r.Values(vk)
            ' 메인 패널(0)에서 0값은 무효값으로 간주하여 선 끊기
            If Single.IsNaN(v) OrElse (pIdx = 0 AndAlso v <= 0) Then
                started = False
                Continue For
            End If
            Dim x = IndexToX(i)
            Dim y = PanelValueToY(v, pIdx, pMin, pMax)
            If Not started Then
                _reusePath.MoveTo(x, y)
                started = True
            Else
                _reusePath.LineTo(x, y)
            End If
        Next
        If started Then canvas.DrawPath(_reusePath, paint)
    End Sub

    Private Sub DrawPanelHistogram(canvas As SKCanvas, results As List(Of IndicatorResult), s As Integer, en As Integer, vk As String, rect As SKRect, pMin As Single, pMax As Single, pIdx As Integer)
        Dim zeroY = PanelValueToY(0, pIdx, pMin, pMax)
        Dim maxI = Math.Min(en, results.Count - 1)
        For i As Integer = s To maxI
            Dim r = results(i)
            If r Is Nothing OrElse r.Values Is Nothing Then Continue For
            If Not r.Values.ContainsKey(vk) Then Continue For
            Dim v = r.Values(vk)
            If Single.IsNaN(v) Then Continue For
            Dim x = IndexToX(i)
            Dim y = PanelValueToY(v, pIdx, pMin, pMax)
            Dim halfW = _vs.CandleWidth / 2 - 0.5F
            Using bp As New SKPaint()
                bp.Style = SKPaintStyle.Fill
                bp.IsAntialias = False
                bp.Color = If(v >= 0, New SKColor(234, 57, 67, 160), New SKColor(46, 134, 222, 160))
                If v >= 0 Then
                    canvas.DrawRect(x - halfW, y, _vs.CandleWidth - 1, zeroY - y, bp)
                Else
                    canvas.DrawRect(x - halfW, zeroY, _vs.CandleWidth - 1, y - zeroY, bp)
                End If
            End Using
        Next
    End Sub

    Private Sub DrawPanelReferenceLine(canvas As SKCanvas, rect As SKRect, pMin As Single, pMax As Single, refVal As Single, pIdx As Integer)
        If refVal < pMin OrElse refVal > pMax Then Return
        Dim y = PanelValueToY(refVal, pIdx, pMin, pMax)
        Using p As New SKPaint()
            p.Style = SKPaintStyle.Stroke
            p.Color = New SKColor(80, 85, 95)
            p.StrokeWidth = 1
            p.PathEffect = SKPathEffect.CreateDash({2, 3}, 0)
            canvas.DrawLine(rect.Left, y, rect.Right, y, p)
        End Using
    End Sub

    Private Sub DrawPanelAxisY(canvas As SKCanvas, rect As SKRect, pMin As Single, pMax As Single)
        Dim x = rect.Right + 4
        _paintAxisText.TextSize = 9
        canvas.DrawText(FormatAxisPrice(pMax), x, rect.Top + 10, _paintAxisText)
        canvas.DrawText(FormatAxisPrice((pMin + pMax) / 2), x, rect.MidY + 4, _paintAxisText)
        canvas.DrawText(FormatAxisPrice(pMin), x, rect.Bottom - 2, _paintAxisText)
        _paintAxisText.TextSize = AXIS_FONT_SIZE
    End Sub

    Private Sub DrawPanelAxisYLeft(canvas As SKCanvas, rect As SKRect, pMin As Single, pMax As Single)
        Dim x = rect.Left + 4
        _paintAxisText.TextSize = 9
        canvas.DrawText(FormatAxisPrice(pMax), x, rect.Top + 10, _paintAxisText)
        canvas.DrawText(FormatAxisPrice((pMin + pMax) / 2), x, rect.MidY + 4, _paintAxisText)
        canvas.DrawText(FormatAxisPrice(pMin), x, rect.Bottom - 2, _paintAxisText)
        _paintAxisText.TextSize = AXIS_FONT_SIZE
    End Sub

    Private Shared Function IsTickIntensityRatioKey(indName As String, valueKey As String) As Boolean
        If String.IsNullOrWhiteSpace(indName) OrElse String.IsNullOrWhiteSpace(valueKey) Then Return False
        Return indName.StartsWith("TICKINT_", StringComparison.OrdinalIgnoreCase) AndAlso
               valueKey.Equals("Ratio", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function IsProgramNetBuyLeftAxisKey(indName As String, valueKey As String) As Boolean
        If String.IsNullOrWhiteSpace(indName) OrElse String.IsNullOrWhiteSpace(valueKey) Then Return False
        Return indName.Equals("PROG_TRADE", StringComparison.OrdinalIgnoreCase) AndAlso
               valueKey.Equals("NetBuy", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function IsPanelLeftAxisKey(indName As String, valueKey As String) As Boolean
        Return IsTickIntensityRatioKey(indName, valueKey) OrElse
               IsProgramNetBuyLeftAxisKey(indName, valueKey)
    End Function

    Private Sub DrawCurrentPriceLine(canvas As SKCanvas)
        If _candles.Count = 0 Then Return
        Dim last = _candles.Last()
        Dim py = PriceToY(last.Close)

        If py >= _mainRect.Top AndAlso py <= _mainRect.Bottom Then
            Using pp As New SKPaint()
                pp.Style = SKPaintStyle.Stroke
                pp.Color = If(last.Close >= _prevClose, ColBullCandle, ColBearCandle)
                pp.StrokeWidth = 1
                pp.PathEffect = SKPathEffect.CreateDash({2, 2}, 0)
                canvas.DrawLine(_mainRect.Left, py, _mainRect.Right, py, pp)

                ' 가격 레이블
                Dim txt = FormatAxisPrice(last.Close)
                Dim tw = _paintCrosshairText.MeasureText(txt)
                canvas.DrawRect(_mainRect.Right, py - 9, tw + 10, 18, pp)
                Using tp As New SKPaint()
                    tp.Color = SKColors.White
                    tp.TextSize = 11
                    tp.IsAntialias = True
                    canvas.DrawText(txt, _mainRect.Right + 5, py + 4, tp)
                End Using
            End Using
        End If
    End Sub

    Private Sub DrawPrevCloseLine(canvas As SKCanvas)
        If _prevClose <= 0 Then Return
        Dim py = PriceToY(_prevClose)
        If py >= _mainRect.Top AndAlso py <= _mainRect.Bottom Then
            Using pp As New SKPaint()
                pp.Style = SKPaintStyle.Stroke
                pp.Color = ColPrevClose
                pp.StrokeWidth = 1
                pp.PathEffect = SKPathEffect.CreateDash({3, 5}, 0)
                canvas.DrawLine(_mainRect.Left, py, _mainRect.Right, py, pp)
            End Using
        End If
    End Sub

    Private Sub DrawViLine(canvas As SKCanvas)
        If _prevClose <= 0 Then Return
        Dim viUp = _prevClose * 1.1F
        Dim viDown = _prevClose * 0.9F
        DrawDashLine(canvas, viUp, New SKColor(255, 100, 100, 150), "VI UP")
        DrawDashLine(canvas, viDown, New SKColor(100, 100, 255, 150), "VI DOWN")
    End Sub

    Private Sub DrawDashLine(canvas As SKCanvas, price As Single, color As SKColor, label As String)
        Dim py = PriceToY(price)
        If py < _mainRect.Top OrElse py > _mainRect.Bottom Then Return

        Using pp As New SKPaint()
            pp.Style = SKPaintStyle.Stroke
            pp.Color = color
            pp.StrokeWidth = 1
            pp.PathEffect = SKPathEffect.CreateDash({4, 4}, 0)
            canvas.DrawLine(_mainRect.Left, py, _mainRect.Right, py, pp)

            Using tp As New SKPaint()
                tp.Color = color
                tp.TextSize = 9
                tp.IsAntialias = True
                canvas.DrawText(label, _mainRect.Left + 5, py - 3, tp)
            End Using
        End Using
    End Sub

    Private Sub DrawSignals(canvas As SKCanvas)
        If _signals.Count = 0 Then Return
        Dim s = Math.Max(0, _vs.StartIndex)
        Dim en = Math.Min(_candles.Count - 1, _vs.EndIndex)
        For Each sig In _signals
            Dim idx = FindCandleIndex(sig.Timestamp)
            If idx < s OrElse idx > en Then Continue For
            Dim x = IndexToX(idx)
            Dim c = _candles(idx)
            Select Case sig.SignalType
                Case SignalType.Buy, SignalType.StrongBuy
                    DrawArrowUp(canvas, x, PriceToY(c.Low) + SIGNAL_ARROW_SIZE + 4, _paintSignalBuy, sig.SignalType = SignalType.StrongBuy)
                Case SignalType.Sell, SignalType.StrongSell
                    DrawArrowDown(canvas, x, PriceToY(c.High) - SIGNAL_ARROW_SIZE - 4, _paintSignalSell, sig.SignalType = SignalType.StrongSell)
            End Select
        Next
    End Sub

    Private Sub DrawArrowUp(canvas As SKCanvas, cx As Single, cy As Single, paint As SKPaint, isStrong As Boolean)
        Dim sz = If(isStrong, SIGNAL_ARROW_SIZE * 1.4F, SIGNAL_ARROW_SIZE)
        _reusePath.Reset()
        _reusePath.MoveTo(cx, cy - sz)
        _reusePath.LineTo(cx - sz * 0.6F, cy)
        _reusePath.LineTo(cx + sz * 0.6F, cy)
        _reusePath.Close()
        canvas.DrawPath(_reusePath, paint)
    End Sub

    Private Sub DrawArrowDown(canvas As SKCanvas, cx As Single, cy As Single, paint As SKPaint, isStrong As Boolean)
        Dim sz = If(isStrong, SIGNAL_ARROW_SIZE * 1.4F, SIGNAL_ARROW_SIZE)
        _reusePath.Reset()
        _reusePath.MoveTo(cx, cy + sz)
        _reusePath.LineTo(cx - sz * 0.6F, cy)
        _reusePath.LineTo(cx + sz * 0.6F, cy)
        _reusePath.Close()
        canvas.DrawPath(_reusePath, paint)
    End Sub

    Private Function FindCandleIndex(ts As DateTime) As Integer
        Dim lo = 0
        Dim hi = _candles.Count - 1
        While lo <= hi
            Dim mid = (lo + hi) \ 2
            If _candles(mid).Dt < ts Then
                lo = mid + 1
            ElseIf _candles(mid).Dt > ts Then
                hi = mid - 1
            Else
                Return mid
            End If
        End While
        If lo >= _candles.Count Then Return _candles.Count - 1
        If lo = 0 Then Return 0
        If Math.Abs((_candles(lo).Dt - ts).TotalSeconds) < Math.Abs((_candles(lo - 1).Dt - ts).TotalSeconds) Then
            Return lo
        Else
            Return lo - 1
        End If
    End Function

    Private Sub DrawAxisY(canvas As SKCanvas)
        Dim gridStep = CSng(CalculateNiceStep(_priceHigh - _priceLow, 7))
        _paintAxisText.TextAlign = SKTextAlign.Left
        Dim x = _mainRect.Right + 6
        Dim p = CSng(Math.Ceiling(_priceLow / gridStep) * gridStep)
        While p < _priceHigh
            Dim y = PriceToY(p)
            If y >= _mainRect.Top + 10 AndAlso y <= _mainRect.Bottom - 5 Then
                canvas.DrawText(FormatAxisPrice(p), x, y + 4, _paintAxisText)
            End If
            p += gridStep
        End While
    End Sub

    Private Sub DrawAxisX(canvas As SKCanvas)
        _paintAxisText.TextAlign = SKTextAlign.Center
        Dim y = _totalHeight - MARGIN_BOTTOM + 14
        Dim s = Math.Max(0, _vs.StartIndex)
        Dim endI = Math.Min(_candles.Count - 1, _vs.EndIndex)
        If endI < s Then
            _paintAxisText.TextAlign = SKTextAlign.Left
            Return
        End If

        Dim minuteStep = GetAxisMinuteStep(s, endI)
        Dim minPixelGap As Single = 56.0F
        Dim lastDrawX As Single = Single.MinValue
        Dim lastDate As DateTime = DateTime.MinValue

        For i As Integer = s To endI
            If i < 0 OrElse i >= _candles.Count Then Continue For
            Dim c = _candles(i)
            If c.Dt = DateTime.MinValue Then Continue For
            If Not ShouldDrawAxisTick(i, c.Dt, minuteStep, s) Then Continue For
            Dim x = IndexToX(i)
            If x < _mainRect.Left OrElse x > _mainRect.Right Then Continue For
            If lastDrawX <> Single.MinValue AndAlso (x - lastDrawX) < minPixelGap Then Continue For
            Dim label As String

            ' 시간 부분이 00:00:00인 경우(일봉 등) 날짜 우선 표시
            If c.Dt.TimeOfDay.TotalSeconds = 0 Then
                label = c.Dt.ToString("MM/dd")
            Else
                ' 이전 표시된 레이블과 날짜가 달라졌거나 첫 번째 레이블인 경우 날짜 포함
                If lastDate = DateTime.MinValue OrElse c.Dt.Date <> lastDate.Date Then
                    label = c.Dt.ToString("MM-dd HH:mm")
                Else
                    label = c.Dt.ToString("HH:mm")
                End If
            End If

            canvas.DrawText(label, x, y, _paintAxisText)
            lastDate = c.Dt
            lastDrawX = x
        Next
        _paintAxisText.TextAlign = SKTextAlign.Left
    End Sub

    Private Function GetAxisMinuteStep(startIdx As Integer, endIdx As Integer) As Integer
        Dim visibleCount = Math.Max(1, endIdx - startIdx + 1)
        Dim targetLabels = 8
        Dim rough = Math.Max(1, CInt(Math.Ceiling(visibleCount / CDbl(targetLabels))))
        Dim steps As Integer() = {1, 2, 3, 5, 10, 15, 30, 60, 120, 180, 240}
        For Each st In steps
            If rough <= st Then Return st
        Next
        Return 240
    End Function

    Private Function ShouldDrawAxisTick(idx As Integer, dt As DateTime, minuteStep As Integer, startIdx As Integer) As Boolean
        If idx = startIdx Then Return True
        If idx > 0 AndAlso idx < _candles.Count Then
            Dim prev = _candles(idx - 1).Dt
            If prev <> DateTime.MinValue AndAlso prev.Date <> dt.Date Then Return True
        End If
        If dt.Second <> 0 Then Return False
        If minuteStep <= 60 Then
            Return (dt.Minute Mod minuteStep) = 0
        End If
        Dim totalMinutes = dt.Hour * 60 + dt.Minute
        Return (totalMinutes Mod minuteStep) = 0
    End Function

    Private Sub DrawCrosshair(canvas As SKCanvas)
        Dim mx = _vs.CrosshairX
        Dim my = _vs.CrosshairY
        If mx < _mainRect.Left OrElse mx > _mainRect.Right Then Return
        If my < _mainRect.Top OrElse my > _totalHeight - MARGIN_BOTTOM Then Return

        canvas.DrawLine(mx, _mainRect.Top, mx, _totalHeight - MARGIN_BOTTOM, _paintCrosshair)
        If my <= _mainRect.Bottom Then
            canvas.DrawLine(_mainRect.Left, my, _mainRect.Right, my, _paintCrosshair)
            DrawCrosshairYLabel(canvas, my, FormatAxisPrice(YToPrice(my)))
        ElseIf my <= _volumeRect.Bottom Then
            canvas.DrawLine(_volumeRect.Left, my, _volumeRect.Right, my, _paintCrosshair)
            DrawCrosshairYLabel(canvas, my, FormatAxisPrice(YToVolume(my)))
        Else
            For pIdx As Integer = 0 To _panelRects.Count - 1
                Dim rect = _panelRects(pIdx)
                If my < rect.Top OrElse my > rect.Bottom Then Continue For
                canvas.DrawLine(rect.Left, my, rect.Right, my, _paintCrosshair)
                If pIdx < _panelRanges.Count Then
                    Dim pMin = _panelRanges(pIdx).Item1
                    Dim pMax = _panelRanges(pIdx).Item2
                    DrawCrosshairYLabel(canvas, my, FormatAxisPrice(PanelYToValue(my, pMin, pMax, rect)))
                End If
                Exit For
            Next
        End If

        Dim idx = XToIndex(mx)
        If idx >= 0 AndAlso idx < _candles.Count Then
            Dim c = _candles(idx)
            Dim timeTxt = c.Dt.ToString("MM/dd HH:mm")
            Dim ttw = _paintCrosshairText.MeasureText(timeTxt)
            Dim tly = _totalHeight - MARGIN_BOTTOM
            canvas.DrawRect(mx - ttw / 2 - 5, tly, ttw + 10, CROSSHAIR_LABEL_H, _paintCrosshairLabel)
            canvas.DrawText(timeTxt, mx - ttw / 2, tly + 14, _paintCrosshairText)
            DrawCandleInfo(canvas, idx)
        End If
    End Sub

    Private Sub DrawCrosshairYLabel(canvas As SKCanvas, y As Single, text As String)
        Dim tw = _paintCrosshairText.MeasureText(text)
        canvas.DrawRect(_mainRect.Right, y - CROSSHAIR_LABEL_H / 2, tw + 10, CROSSHAIR_LABEL_H, _paintCrosshairLabel)
        canvas.DrawText(text, _mainRect.Right + 5, y + 4, _paintCrosshairText)
    End Sub

    Private Function YToVolume(y As Single) As Single
        If _volumeRect.Height <= 0 Then Return 0
        Dim ratio = (_volumeRect.Bottom - y) / _volumeRect.Height
        ratio = Math.Max(0, Math.Min(1, ratio))
        Return CSng(_volumeMax * ratio)
    End Function

    Private Shared Function PanelYToValue(y As Single, pMin As Single, pMax As Single, rect As SKRect) As Single
        If rect.Height <= 0 Then Return pMin
        Dim ratio = (y - rect.Top) / rect.Height
        ratio = Math.Max(0, Math.Min(1, ratio))
        Return pMax - ratio * (pMax - pMin)
    End Function

    Private Sub DrawCandleInfo(canvas As SKCanvas, idx As Integer)
        If idx < 0 OrElse idx >= _candles.Count Then Return
        Dim c = _candles(idx)
        Dim info = $"O {c.Open:N0}  H {c.High:N0}  L {c.Low:N0}  C {c.Close:N0}  V {c.Volume:N0}"
        Dim x = _mainRect.Left + 8
        Dim y = _mainRect.Top + 14
        Dim tw = _paintCrosshairText.MeasureText(info)
        Using bgP As New SKPaint()
            bgP.Style = SKPaintStyle.Fill
            bgP.Color = New SKColor(24, 26, 32, 200)
            canvas.DrawRect(x - 4, y - 12, tw + 8, 16, bgP)
        End Using

        Using tp As New SKPaint()
            tp.Color = If(c.Close >= c.Open, ColBullCandle, ColBearCandle)
            tp.TextSize = 11
            tp.IsAntialias = True
            tp.Typeface = SKTypeface.FromFamilyName("Consolas")
            canvas.DrawText(info, x, y, tp)
        End Using
    End Sub

    Private Sub DrawStockInfo(canvas As SKCanvas)
        If String.IsNullOrEmpty(_stockCode) Then Return
        Dim txt = $"{_stockCode}  {_stockName}"
        If _candles.Count > 0 Then
            Dim last = _candles(_candles.Count - 1)
            Dim change = If(_prevClose > 0, last.Close - _prevClose, 0)
            Dim changeRate = If(_prevClose > 0, change / _prevClose * 100, 0)
            Dim sign = If(change >= 0, "+", "")
            txt &= $"   {last.Close:N0}  {sign}{change:N0} ({sign}{changeRate:F2}%)"
        End If

        Using p As New SKPaint()
            p.Color = SKColors.White
            p.TextSize = 13
            p.IsAntialias = True
            p.Typeface = SKTypeface.FromFamilyName("맑은 고딕")
            p.FakeBoldText = True
            canvas.DrawText(txt, _mainRect.Left + 8, _mainRect.Top + 28, p)
        End Using
    End Sub

    Private Sub OnGLMouseMove(sender As Object, e As MouseEventArgs)
        _vs.CrosshairX = e.X
        _vs.CrosshairY = e.Y

        If _isDragging Then
            Dim dx = e.X - _dragStartX
            Dim candleShift = CInt(dx / (_vs.CandleWidth + _vs.Gap))
            _vs.StartIndex = Math.Max(0, Math.Min(_dragStartIndex - candleShift, _candles.Count - _vs.VisibleCount))
        ElseIf _isDraggingPrice Then
            Dim dy = e.Y - _dragStartY
            Dim range = _manualMaxP - _manualMinP
            Dim delta = dy * (range / _mainRect.Height)
            _manualMaxP += delta
            _manualMinP += delta
            _dragStartY = e.Y
        End If

        _lastMouseX = e.X
        _lastMouseY = e.Y
        _needsRepaint = True
    End Sub

    Private Sub OnGLMouseDown(sender As Object, e As MouseEventArgs)
        _skControl.Focus()
        _lastMouseX = e.X
        _lastMouseY = e.Y

        ' 범례 클릭 감지
        Dim hit = _legendHits.FirstOrDefault(Function(h) h.Rect.Contains(e.X, e.Y))
        If hit IsNot Nothing Then
            _selectedIndicatorName = hit.Name
            _needsRepaint = True
            If e.Button = MouseButtons.Right Then
                ShowContextMenu(e.Location)
            End If
            Return
        End If

        If e.Button = MouseButtons.Left Then
            If e.X > _mainRect.Right Then
                _isDraggingPrice = True
                _isAutoScaleY = False
                _dragStartY = e.Y
            Else
                _isDragging = True
                _dragStartX = e.X
                _dragStartIndex = _vs.StartIndex
            End If
        ElseIf e.Button = MouseButtons.Right Then
            ShowContextMenu(e.Location)
        End If
    End Sub

    Private Sub OnGLMouseUp(sender As Object, e As MouseEventArgs)
        If e.Button = MouseButtons.Left Then
            _isDragging = False
            _isDraggingPrice = False
            If Math.Abs(e.X - _dragStartX) < 3 Then
                OnChartClick(e.X, e.Y)
            End If
        End If
    End Sub

    Private Sub OnGLDoubleClick(sender As Object, e As MouseEventArgs) Handles _skControl.MouseDoubleClick
        Dim hit = _legendHits.FirstOrDefault(Function(h) h.Rect.Contains(e.X, e.Y))
        If hit IsNot Nothing Then
            RaiseEvent IndicatorSettingRequested(Me, EventArgs.Empty)
            Return
        End If

        ' 화면 더블 클릭 시 오토 스케일 복구
        _isAutoScaleY = True
        _needsRepaint = True
    End Sub

    Private Sub OnGLMouseWheel(sender As Object, e As MouseEventArgs)
        Dim zoom = If(e.Delta > 0, 1.2F, 0.8F)
        _vs.CandleWidth *= zoom
        If _vs.CandleWidth < 1 Then _vs.CandleWidth = 1
        If _vs.CandleWidth > 50 Then _vs.CandleWidth = 50

        If _mainRect.Width <= 0 OrElse Single.IsNaN(_mainRect.Width) OrElse Single.IsInfinity(_mainRect.Width) Then
            Return
        End If

        ' 마우스 위치 기준 줌 유지
        Dim mouseIdx = XToIndex(e.X)
        Dim ratio As Double = (e.X - _mainRect.Left) / _mainRect.Width
        If Double.IsNaN(ratio) OrElse Double.IsInfinity(ratio) Then ratio = 0.5
        ratio = Math.Max(0.0, Math.Min(1.0, ratio))

        Dim denom As Double = _vs.CandleWidth + _vs.Gap
        If denom <= 0 OrElse Double.IsNaN(denom) OrElse Double.IsInfinity(denom) Then
            denom = 1.0
        End If

        Dim visibleD As Double = _mainRect.Width / denom
        If Double.IsNaN(visibleD) OrElse Double.IsInfinity(visibleD) Then
            visibleD = 1.0
        End If

        Dim newVisibleCount = Math.Max(1, CInt(Math.Truncate(visibleD)))
        Dim leftCount = CLng(Math.Truncate(CDbl(newVisibleCount) * ratio))
        Dim maxStart = Math.Max(0, _candles.Count - newVisibleCount)
        Dim desiredStart As Long = CLng(mouseIdx) - leftCount
        If desiredStart < 0 Then desiredStart = 0
        If desiredStart > maxStart Then desiredStart = maxStart

        _vs.VisibleCount = newVisibleCount
        _vs.StartIndex = CInt(desiredStart)

        _needsRepaint = True
    End Sub

    Private Sub OnGLMouseEnter(sender As Object, e As EventArgs)
        _mouseInside = True
        _needsRepaint = True
    End Sub

    Private Sub OnGLMouseLeave(sender As Object, e As EventArgs)
        _mouseInside = False
        _isDragging = False
        _isDraggingPrice = False
        _needsRepaint = True
    End Sub

    Private Sub OnGLKeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.KeyCode
            Case Keys.Left
                _vs.StartIndex = Math.Max(0, _vs.StartIndex - 1)
            Case Keys.Right
                _vs.StartIndex = Math.Min(Math.Max(0, _candles.Count - _vs.VisibleCount), _vs.StartIndex + 1)
            Case Keys.Home
                _vs.StartIndex = 0
            Case Keys.End
                _vs.StartIndex = Math.Max(0, _candles.Count - _vs.VisibleCount)
            Case Keys.Add, Keys.Oemplus
                _vs.CandleWidth *= 1.2F
                _vs.VisibleCount = CInt(_mainRect.Width / (_vs.CandleWidth + _vs.Gap))
            Case Keys.Subtract, Keys.OemMinus
                _vs.CandleWidth *= 0.8F
                _vs.VisibleCount = CInt(_mainRect.Width / (_vs.CandleWidth + _vs.Gap))
            Case Keys.C
                _vs.ShowCrosshair = Not _vs.ShowCrosshair
            Case Keys.T
                If e.Control AndAlso _candles.Count > 0 Then
                    ' 당일 데이터만 보기
                    Dim lastDate = _candles.Last().Dt.Date
                    Dim firstIdx = _candles.FindIndex(Function(c) c.Dt.Date = lastDate)
                    If firstIdx >= 0 Then
                        _vs.StartIndex = firstIdx
                        _vs.VisibleCount = _candles.Count - firstIdx
                        _vs.CandleWidth = Math.Max(1.0F, (_mainRect.Width / (_vs.VisibleCount + 2)) - _vs.Gap)
                    End If
                End If
            Case Keys.A
                If e.Control AndAlso _candles.Count > 0 Then
                    ' 전체 보기 
                    _vs.StartIndex = 0
                    _vs.VisibleCount = _candles.Count
                    _vs.CandleWidth = Math.Max(0.5F, (_mainRect.Width / (_vs.VisibleCount + 5)) - _vs.Gap)
                End If
            Case Keys.Space
                If _isAutoRolling Then StopSimulation() Else StartSimulation()
            Case Keys.Up
                If Not _isAutoScaleY Then
                    Dim range = _manualMaxP - _manualMinP
                    _manualMaxP += range * 0.1F
                    _manualMinP += range * 0.1F
                End If
            Case Keys.Down
                If Not _isAutoScaleY Then
                    Dim range = _manualMaxP - _manualMinP
                    _manualMaxP -= range * 0.1F
                    _manualMinP -= range * 0.1F
                End If
        End Select
        _needsRepaint = True
        e.Handled = True
    End Sub



    Public Sub AddIndicatorByName(name As String)
        Dim ind As IIndicator = Nothing
        Select Case name
            Case "MA" : ind = New MA_Indicator()
            Case "Bollinger" : ind = New Bollinger_Indicator()
            Case "Volume" : ind = New Volume_Indicator()
            Case "MACD" : ind = New MACD_Indicator()
            Case "RSI" : ind = New RSI_Indicator()
            Case "SuperTrend" : ind = New SuperTrend_Indicator()
            Case "JMA" : ind = New JMA_Indicator()
            Case "VWAP" : ind = New VWAP_Indicator()
            Case "OBV" : ind = New OBV_Indicator()
            Case "Disparity" : ind = New Disparity_Indicator()
            Case "TickIntensity" : ind = New TickIntensity_Indicator()
            Case "TradeStrength" : ind = New TradeStrength_Indicator()
            Case "CumTradeAmount" : ind = New CumTradeAmount_Indicator()
            Case "ProgramTrade" : ind = New ProgramTrade_Indicator()
            Case "SectorLeader" : ind = New SectorLeader_Indicator()
        End Select

        If ind IsNot Nothing Then AddIndicator(ind)
    End Sub

    Private Sub ShowContextMenu(pt As Point)
        Dim menu As New ContextMenuStrip()

        Dim addMenu = New ToolStripMenuItem("지표삽입")
        addMenu.DropDownItems.Add("이동평균 (MA)", Nothing, Sub() AddIndicatorByName("MA"))
        addMenu.DropDownItems.Add("볼린저 밴드", Nothing, Sub() AddIndicatorByName("Bollinger"))
        addMenu.DropDownItems.Add("거래량 지표", Nothing, Sub() AddIndicatorByName("Volume"))
        addMenu.DropDownItems.Add("MACD", Nothing, Sub() AddIndicatorByName("MACD"))
        addMenu.DropDownItems.Add("RSI", Nothing, Sub() AddIndicatorByName("RSI"))
        addMenu.DropDownItems.Add("SuperTrend", Nothing, Sub() AddIndicatorByName("SuperTrend"))

        Dim subMenu = New ToolStripMenuItem("기타 지표")
        subMenu.DropDownItems.Add("JMA (Jurik MA)", Nothing, Sub() AddIndicatorByName("JMA"))
        subMenu.DropDownItems.Add("VWAP (거래중점평균)", Nothing, Sub() AddIndicatorByName("VWAP"))
        subMenu.DropDownItems.Add("OBV (거래량지표)", Nothing, Sub() AddIndicatorByName("OBV"))
        subMenu.DropDownItems.Add("이격도 (Disparity)", Nothing, Sub() AddIndicatorByName("Disparity"))
        subMenu.DropDownItems.Add("틱강도 (Intensity)", Nothing, Sub() AddIndicatorByName("TickIntensity"))
        subMenu.DropDownItems.Add("체결강도 (Strength)", Nothing, Sub() AddIndicatorByName("TradeStrength"))
        subMenu.DropDownItems.Add("누적체결대금 (CumAmt)", Nothing, Sub() AddIndicatorByName("CumTradeAmount"))
        subMenu.DropDownItems.Add("프로그램매매 (Program)", Nothing, Sub() AddIndicatorByName("ProgramTrade"))
        subMenu.DropDownItems.Add("업종주도주 (Sector)", Nothing, Sub() AddIndicatorByName("SectorLeader"))
        addMenu.DropDownItems.Add(subMenu)

        menu.Items.Add(addMenu)

        If Not String.IsNullOrEmpty(_selectedIndicatorName) Then
            Dim miEdit = New ToolStripMenuItem($"'{_selectedIndicatorName}' 설정 수정", Nothing, Sub()
                                                                                                 RaiseEvent IndicatorSettingRequested(Me, EventArgs.Empty)
                                                                                             End Sub)
            menu.Items.Add(miEdit)

            Dim miDelete = New ToolStripMenuItem($"'{_selectedIndicatorName}' 삭제", Nothing, Sub()
                                                                                                _indicatorEngine.Remove(_selectedIndicatorName)
                                                                                                _selectedIndicatorName = ""
                                                                                                RebuildPanelLayout()
                                                                                                _needsRepaint = True
                                                                                            End Sub)
            menu.Items.Add(miDelete)
        End If

        ' 선택된 것이 전략인 경우 처리
        If Not String.IsNullOrEmpty(_selectedIndicatorName) AndAlso _selectedIndicatorName.StartsWith("STRAT_") Then
            Dim stratName = _selectedIndicatorName.Substring(6)
            Dim miRemoveStrat = New ToolStripMenuItem($"전략 '{stratName}' 제거", Nothing, Sub()
                                                                                           _appliedStrategies.RemoveAll(Function(s) s.Name = stratName)
                                                                                           _strategyEngine.Remove(stratName)
                                                                                           _selectedIndicatorName = ""
                                                                                           _signals.RemoveAll(Function(s) s.StrategyName = stratName)
                                                                                           _needsRepaint = True
                                                                                       End Sub)
            menu.Items.Add(miRemoveStrat)

            Dim strat = _appliedStrategies.FirstOrDefault(Function(s) s.Name = stratName)
            If strat IsNot Nothing Then
                Dim miToggleMode = New ToolStripMenuItem($"모드 전환 ({strat.Mode} -> {If(strat.Mode = "Test", "Live", "Test")})", Nothing, Sub()
                                                                                                                                            strat.Mode = If(strat.Mode = "Test", "Live", "Test")
                                                                                                                                            _needsRepaint = True
                                                                                                                                        End Sub)
                menu.Items.Add(miToggleMode)

                Dim miToggleActive = New ToolStripMenuItem(If(strat.IsActive, "전략 비활성화", "전략 활성화"), Nothing, Sub()
                                                                                                                 strat.IsActive = Not strat.IsActive
                                                                                                                 _needsRepaint = True
                                                                                                             End Sub)
                menu.Items.Add(miToggleActive)
            End If
        End If

        menu.Items.Add(New ToolStripSeparator())

        Dim miOpt = New ToolStripMenuItem("차트 옵션")
        Dim miPriceLine = New ToolStripMenuItem("현재가 라인", Nothing, Sub()
                                                                       _showCurrentPriceLine = Not _showCurrentPriceLine
                                                                       _needsRepaint = True
                                                                   End Sub)
        miPriceLine.Checked = _showCurrentPriceLine
        miOpt.DropDownItems.Add(miPriceLine)

        Dim miViLine = New ToolStripMenuItem("VI 예상선", Nothing, Sub()
                                                                    _showViLine = Not _showViLine
                                                                    _needsRepaint = True
                                                                End Sub)
        miViLine.Checked = _showViLine
        miOpt.DropDownItems.Add(miViLine)

        Dim miDayLine = New ToolStripMenuItem("Day Change Line", Nothing, Sub()
                                                                              _showDayChangeLines = Not _showDayChangeLines
                                                                              _needsRepaint = True
                                                                          End Sub)
        miDayLine.Checked = _showDayChangeLines
        miOpt.DropDownItems.Add(miDayLine)
        menu.Items.Add(miOpt)

        menu.Items.Add(New ToolStripSeparator())
        Dim miRefresh = New ToolStripMenuItem("차트 리프레시", Nothing, Sub()
                                                                      RefreshChartData()
                                                                  End Sub)
        menu.Items.Add(miRefresh)

        menu.Items.Add(New ToolStripSeparator())
        Dim miDataView = New ToolStripMenuItem("데이터보기", Nothing, Sub()
                                                                     RaiseEvent DataViewRequested(Me, EventArgs.Empty)
                                                                 End Sub)
        menu.Items.Add(miDataView)

        menu.Items.Add(New ToolStripSeparator())

        ' 전략 메뉴 추가
        Dim miStratMgmt = New ToolStripMenuItem("전략 관리자 (AI 설계 비서)...", Nothing, Sub() RaiseEvent StrategySettingRequested(Me, EventArgs.Empty))
        menu.Items.Add(miStratMgmt)

        Dim miApplyStrat = New ToolStripMenuItem("전략 적용 및 분석 시작", Nothing, Sub()
                                                                               EvaluateStrategies()
                                                                               _needsRepaint = True
                                                                           End Sub)
        menu.Items.Add(miApplyStrat)

        menu.Items.Add(New ToolStripSeparator())

        Dim miReset As New ToolStripMenuItem("차트 초기화 (AutoScale)")
        AddHandler miReset.Click, Sub(s, ev)
                                      _isAutoScaleY = True
                                      _vs.CandleWidth = 8
                                      _vs.Gap = 2
                                      _vs.VisibleCount = 120
                                      _vs.StartIndex = Math.Max(0, _candles.Count - _vs.VisibleCount)
                                      _needsRepaint = True
                                  End Sub
        menu.Items.Add(miReset)

        Dim miEnd As New ToolStripMenuItem("최신으로 이동 (End)")
        AddHandler miEnd.Click, Sub(s, ev)
                                    _vs.StartIndex = Math.Max(0, _candles.Count - _vs.VisibleCount)
                                    _needsRepaint = True
                                End Sub
        menu.Items.Add(miEnd)

        menu.Show(_skControl, pt)
    End Sub

    Public Sub ApplyStrategy(strat As StrategyDefinition)
        If strat Is Nothing Then Return
        AppLogger.I.Info($"[Strategy] Apply Strategy: {strat.Name}")

        ' 단일 전략 모드: 새 전략 적용 시 이전 전략/신호는 자동 제거
        _appliedStrategies.Clear()
        _signals.Clear()
        _strategyEngine.Clear()
        _appliedStrategies.Add(strat)
        _strategyEngine.Register(strat)
        EvaluateStrategies()
        _needsRepaint = True
    End Sub

    Public Function GetSelectedIndicator() As IIndicator
        If String.IsNullOrEmpty(_selectedIndicatorName) Then Return Nothing
        Return _indicatorEngine.GetAll().FirstOrDefault(Function(x) x.Name = _selectedIndicatorName)
    End Function

    Private Sub OnChartClick(x As Integer, y As Integer)
        Dim idx = XToIndex(x)
        If idx >= 0 AndAlso idx < _candles.Count Then
            Dim args As New ChartClickEventArgs With {
                .CandleIndex = idx,
                .Candle = _candles(idx),
                .Price = YToPrice(y),
                .StockCode = _stockCode
            }
            RaiseEvent ChartClicked(Me, args)
        End If
    End Sub

    Private Sub EvaluateStrategies()
        If _candles.Count < 2 Then Return

        ' 기존 신호 초기화 (중복 방지)
        _signals.Clear()

        Dim evalResults = BuildStrategyEvaluationResults()
        Dim sigList = _strategyEngine.EvaluateAll(_stockCode, _candles, evalResults, _prevClose)
        'AppLogger.I.Info($"[Strategy] 평가 완료: code={_stockCode}, strategies={_appliedStrategies.Count}, signals={sigList.Count}")
        For Each sig In sigList
            _signals.Add(sig)
            Dim m As New Msg(Topics.STRATEGY_SIGNAL)
            m("stockCode") = _stockCode
            m("signal") = sig.SignalType.ToString()
            m("strategy") = sig.StrategyName
            m("price") = sig.Price
            m("reason") = sig.Reason
            m("confidence") = sig.Confidence
            m("time") = sig.Timestamp
            MessageBus.I.Emit(m)
        Next
    End Sub

    Private Function BuildStrategyEvaluationResults() As Dictionary(Of String, List(Of IndicatorResult))
        Dim merged As New Dictionary(Of String, List(Of IndicatorResult))(StringComparer.OrdinalIgnoreCase)

        For Each kv In _indicatorEngine.Results
            merged(kv.Key) = kv.Value
        Next

        If NeedSuperTrendForStrategies() AndAlso Not HasSuperTrendResults(merged) Then
            Dim st As New SuperTrend_Indicator()
            merged(st.Name) = st.Calculate(_candles)
            AppLogger.I.Info($"[Strategy] 런타임 지표 보강: SuperTrend 자동 계산 ({st.Name})")
        End If

        Return merged
    End Function

    Private Function NeedSuperTrendForStrategies() As Boolean
        For Each strat In _appliedStrategies
            If strat Is Nothing Then Continue For

            Dim buyConds = If(strat.BuyRules, New List(Of LogicGate)()).
                SelectMany(Function(g) If(g Is Nothing OrElse g.Conditions Is Nothing, New List(Of ConditionCell)(), g.Conditions))
            Dim sellConds = If(strat.SellRules, New List(Of LogicGate)()).
                SelectMany(Function(g) If(g Is Nothing OrElse g.Conditions Is Nothing, New List(Of ConditionCell)(), g.Conditions))

            For Each c In buyConds.Concat(sellConds)
                If c Is Nothing Then Continue For
                If String.Equals(c.IndicatorA, "SuperTrend", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(c.IndicatorB, "SuperTrend", StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Next
        Next

        Return False
    End Function

    Private Function HasSuperTrendResults(results As Dictionary(Of String, List(Of IndicatorResult))) As Boolean
        If results Is Nothing Then Return False

        For Each kv In results
            Dim k = If(kv.Key, "")
            If Not (k.StartsWith("ST_", StringComparison.OrdinalIgnoreCase) OrElse
                    k.IndexOf("SUPERTREND", StringComparison.OrdinalIgnoreCase) >= 0) Then
                Continue For
            End If

            Dim rows = kv.Value
            If rows Is Nothing Then Continue For
            For Each r In rows
                If r Is Nothing Then Continue For
                Dim v = r.Val("Value")
                If Not Single.IsNaN(v) Then Return True
            Next
        Next

        Return False
    End Function

    Private Shared Function FormatAxisPrice(price As Single) As String
        If price >= 1000 Then Return price.ToString("N0")
        If price >= 100 Then Return price.ToString("N1")
        Return price.ToString("N2")
    End Function

    Public Function GetDataArrays() As List(Of ChartDataArray)
        Dim arrays As New List(Of ChartDataArray)

        Dim candlesTable As New DataTable("Candles")
        candlesTable.Columns.Add("Index", GetType(Integer))
        candlesTable.Columns.Add("Dt", GetType(String))
        candlesTable.Columns.Add("Open", GetType(Single))
        candlesTable.Columns.Add("High", GetType(Single))
        candlesTable.Columns.Add("Low", GetType(Single))
        candlesTable.Columns.Add("Close", GetType(Single))
        candlesTable.Columns.Add("Volume", GetType(Long))
        candlesTable.Columns.Add("TradeAmount", GetType(Long))

        For i As Integer = 0 To _candles.Count - 1
            Dim c = _candles(i)
            candlesTable.Rows.Add(i, c.Dt.ToString("yyyy-MM-dd HH:mm:ss"), c.Open, c.High, c.Low, c.Close, c.Volume, c.TradeAmount)
        Next
        arrays.Add(New ChartDataArray With {.Name = "Candles", .Table = candlesTable})

        Dim sigTable As New DataTable("Signals")
        sigTable.Columns.Add("Index", GetType(Integer))
        sigTable.Columns.Add("Time", GetType(String))
        sigTable.Columns.Add("Type", GetType(String))
        sigTable.Columns.Add("Strategy", GetType(String))
        sigTable.Columns.Add("Price", GetType(Single))
        sigTable.Columns.Add("Reason", GetType(String))
        sigTable.Columns.Add("Confidence", GetType(Single))

        For i As Integer = 0 To _signals.Count - 1
            Dim s = _signals(i)
            sigTable.Rows.Add(i, s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), s.SignalType.ToString(), s.StrategyName, s.Price, s.Reason, s.Confidence)
        Next
        arrays.Add(New ChartDataArray With {.Name = "Signals", .Table = sigTable})

        Dim resultMap = _indicatorEngine.Results
        For Each kv In resultMap
            Dim indName = kv.Key
            Dim results = kv.Value
            If results Is Nothing Then Continue For

            Dim keySet As New List(Of String)
            For Each r In results
                If r Is Nothing OrElse r.Values Is Nothing Then Continue For
                For Each k In r.Values.Keys
                    If Not keySet.Contains(k) Then keySet.Add(k)
                Next
            Next

            Dim t As New DataTable(indName)
            t.Columns.Add("Index", GetType(Integer))
            t.Columns.Add("Dt", GetType(String))
            For Each k In keySet
                t.Columns.Add(k, GetType(String))
            Next

            For i As Integer = 0 To results.Count - 1
                Dim r = results(i)
                If r Is Nothing Then Continue For
                Dim row = t.NewRow()
                row("Index") = i
                If i >= 0 AndAlso i < _candles.Count Then
                    row("Dt") = _candles(i).Dt.ToString("yyyy-MM-dd HH:mm:ss")
                Else
                    row("Dt") = ""
                End If

                For Each k In keySet
                    Dim v As Single = Single.NaN
                    If r.Values IsNot Nothing AndAlso r.Values.ContainsKey(k) Then v = r.Values(k)
                    row(k) = ToCellText(v)
                Next
                t.Rows.Add(row)
            Next
            arrays.Add(New ChartDataArray With {.Name = $"Indicator:{indName}", .Table = t})
        Next

        Return arrays
    End Function

    Private Shared Function RowNum(row As Dictionary(Of String, String), ParamArray keys As String()) As Single
        If row Is Nothing OrElse keys Is Nothing Then Return 0
        For Each key In keys
            If String.IsNullOrWhiteSpace(key) Then Continue For
            If Not row.ContainsKey(key) Then Continue For
            Dim raw = row(key)
            If String.IsNullOrWhiteSpace(raw) Then Continue For
            Return CSng(SharedUtil.SafeDouble(raw, True))
        Next
        Return 0
    End Function

    Private Shared Function RowHasDatePart(row As Dictionary(Of String, String)) As Boolean
        If row Is Nothing Then Return False
        Return row.ContainsKey("date") OrElse row.ContainsKey("dt") OrElse row.ContainsKey("datetime") OrElse row.ContainsKey("일자")
    End Function

    Private Shared Function ToCellText(v As Single) As String
        If Single.IsNaN(v) Then Return "NaN"
        If Single.IsInfinity(v) Then Return "INF"
        Return v.ToString("0.######")
    End Function

    Public Event ChartClicked As EventHandler(Of ChartClickEventArgs)
    Public Event IndicatorSettingRequested As EventHandler(Of EventArgs)
    Public Event StrategySettingRequested As EventHandler(Of EventArgs)
    Public Event DataViewRequested As EventHandler(Of EventArgs)

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            MessageBus.I.Off(Topics.CANDLE_LOADED, AddressOf OnCandleLoaded)
            MessageBus.I.Off(Topics.TICK_CANDLE_LOADED, AddressOf OnTickCandleLoaded)
            MessageBus.I.Off(Topics.CANDLE_PERIOD_LOADED, AddressOf OnCandlePeriodLoaded)
            MessageBus.I.Off(Topics.TICK, AddressOf OnTick)
            MessageBus.I.Off(Topics.PROGRAM_TRADE, AddressOf OnProgramTrade)
            MessageBus.I.Off(Topics.PROGRAM_TRADE_RESULT, AddressOf OnProgramTrade)
            MessageBus.I.Off(Topics.TRADE_STRENGTH, AddressOf OnTradeStrength)
            MessageBus.I.Off(Topics.SECTOR_STOCKS_RESULT, AddressOf OnSectorStocksResult)
            MessageBus.I.Off(Topics.STRATEGY_SIGNAL, AddressOf OnStrategySignal)
            If Not String.IsNullOrWhiteSpace(_stockCode) Then
                MessageBus.I.Emit("program.trade.rt.unsubscribe",
                                  "code", _stockCode,
                                  "provider", RuntimeChartSettings.MarketDataProvider)
            End If
            If _frameTimer IsNot Nothing Then
                _frameTimer.Stop()
                _frameTimer.Dispose()
                _frameTimer = Nothing
            End If
            If _autoRollTimer IsNot Nothing Then
                _autoRollTimer.Stop()
                _autoRollTimer.Dispose()
                _autoRollTimer = Nothing
            End If
            _paintBullBody.Dispose()
            _paintBearBody.Dispose()
            _paintBullWick.Dispose()
            _paintBearWick.Dispose()
            _paintGrid.Dispose()
            _paintAxisText.Dispose()
            _paintCrosshair.Dispose()
            _paintCrosshairLabel.Dispose()
            _paintCrosshairText.Dispose()
            _paintCurrentLine.Dispose()
            _paintCurrentLabel.Dispose()
            _paintCurrentText.Dispose()
            _paintSignalBuy.Dispose()
            _paintSignalSell.Dispose()
            _paintPanelBorder.Dispose()
            _paintVolBull.Dispose()
            _paintVolBear.Dispose()
            _reusePath.Dispose()
            For Each p In _indicatorPaints.Values
                p.Dispose()
            Next
            _indicatorPaints.Clear()
            If _skControl IsNot Nothing Then
                _skControl.Dispose()
                _skControl = Nothing
            End If
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

Public Class ChartClickEventArgs
    Inherits EventArgs
    Public Property CandleIndex As Integer
    Public Property Candle As CandleItem
    Public Property Price As Single
    Public Property StockCode As String
End Class
