Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Windows.Forms
Imports [Shared]
Imports SkiaSharp
Imports SkiaSharp.Views.Desktop
Imports MainApp.Models
Imports MainApp.Services

Public Class SafeFastChartControl
    Inherits UserControl

    Public Event ChartProfileChanged As EventHandler

    Private Const FRAME_INTERVAL_MS As Integer = 16
    Private Const RIGHT_DRAG_PADDING_BARS As Integer = 12

    Private ReadOnly _options As New SafeChartOptions()
    Private ReadOnly _state As New SafeChartState()
    Private ReadOnly _buffer As New SafeChartDataBuffer()
    Private ReadOnly _geo As New SafeChartGeometry()
    Private ReadOnly _indicatorEngine As New IndicatorEngine()

    Private ReadOnly _axisRenderer As New SafeAxisRenderer()
    Private ReadOnly _candleRenderer As New SafeCandleRenderer()
    Private ReadOnly _indicatorRenderer As New SafeIndicatorRenderer()

    Private ReadOnly _sk As SKControl
    Private ReadOnly _frameTimer As Timer
    Private ReadOnly _interaction As SafeInteractionController
    Private ReadOnly _contextMenu As ContextMenuStrip

    Private _needsRepaint As Boolean = True
    Private _stockCode As String = ""
    Private _stockName As String = ""
    Private _chartType As String = "minute"
    Private _requestedCount As Integer = RuntimeChartSettings.DefaultChartOpenCount

    Public Sub New()
        SetStyle(ControlStyles.Selectable, True)
        DoubleBuffered = True

        _state.VisibleCount = _options.InitialVisibleBars
        _state.CandleWidth = _options.CandleWidth
        _state.Gap = _options.CandleGap
        _state.RightPaddingBars = RIGHT_DRAG_PADDING_BARS

        _sk = New SKControl()
        _sk.Dock = DockStyle.Fill
        Controls.Add(_sk)

        AddHandler _sk.PaintSurface, AddressOf OnPaintSurface
        AddHandler _sk.MouseDown, AddressOf OnMouseDownSafe
        AddHandler _sk.MouseUp, AddressOf OnMouseUpSafe
        AddHandler _sk.MouseMove, AddressOf OnMouseMoveSafe
        AddHandler _sk.MouseLeave, AddressOf OnMouseLeaveSafe
        AddHandler _sk.MouseWheel, AddressOf OnMouseWheelSafe
        AddHandler _sk.MouseDoubleClick, AddressOf OnMouseDoubleClickSafe
        AddHandler _sk.Resize, Sub() RequestRepaint()

        _contextMenu = BuildContextMenu()
        _sk.ContextMenuStrip = _contextMenu

        _interaction = New SafeInteractionController(_state, AddressOf RequestRepaint)

        _frameTimer = New Timer()
        _frameTimer.Interval = FRAME_INTERVAL_MS
        AddHandler _frameTimer.Tick, AddressOf OnFrameTick
        _frameTimer.Start()

        MessageBus.I.On(Topics.CANDLE_LOADED, AddressOf OnCandleLoaded)
        MessageBus.I.On(Topics.TICK_CANDLE_LOADED, AddressOf OnTickCandleLoaded)
        MessageBus.I.On(Topics.TICK, AddressOf OnTick)

        BackColor = System.Drawing.Color.Black
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            Try
                MessageBus.I.Off(Topics.CANDLE_LOADED, AddressOf OnCandleLoaded)
                MessageBus.I.Off(Topics.TICK_CANDLE_LOADED, AddressOf OnTickCandleLoaded)
                MessageBus.I.Off(Topics.TICK, AddressOf OnTick)
            Catch
            End Try

            If _frameTimer IsNot Nothing Then
                _frameTimer.Stop()
                _frameTimer.Dispose()
            End If

            If _contextMenu IsNot Nothing Then
                _contextMenu.Dispose()
            End If
        End If

        MyBase.Dispose(disposing)
    End Sub

    Public Sub SetStock(stockCode As String, Optional chartType As String = "minute", Optional count As Integer = 0)
        _stockCode = SharedUtil.NormalizeChartCode(stockCode)
        _stockName = _stockCode
        _chartType = chartType
        _requestedCount = If(count > 0, count, RuntimeChartSettings.DefaultChartOpenCount)

        MessageBus.I.Emit(Topics.CANDLE_REQUEST,
                          "code", _stockCode,
                          "provider", RuntimeChartSettings.MarketDataProvider,
                          "timeframe", RuntimeChartSettings.DefaultCandleTimeframe,
                          "count", _requestedCount)

        RequestAuxiliaryIndicatorData()
        MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", _stockCode)

        RequestRepaint()
    End Sub

    Public Sub LoadCandles(candles As List(Of CandleItem), Optional prevClose As Single = 0.0F)
        _buffer.SetCandles(candles, prevClose)
        _indicatorEngine.CalculateAll(_buffer.Snapshot())
        MoveToLatest()
        RequestAuxiliaryIndicatorData()
        RequestRepaint()
    End Sub

    Public Sub AddIndicator(ind As IIndicator)
        If ind Is Nothing Then Return
        _indicatorEngine.Register(ind)
        If _buffer.Count > 0 Then
            _indicatorEngine.CalculateAll(_buffer.Snapshot())
            RequestAuxiliaryIndicatorData()
        End If
        RequestRepaint()
        NotifyChartProfileChanged()
    End Sub

    Public Sub RemoveIndicator(name As String)
        If String.IsNullOrWhiteSpace(name) Then Return
        _indicatorEngine.Remove(name)
        RequestRepaint()
        NotifyChartProfileChanged()
    End Sub

    Public Sub ReCalculate()
        If _buffer.Count <= 0 Then Return
        _indicatorEngine.CalculateAll(_buffer.Snapshot())
        RequestRepaint()
    End Sub

    Public Function ExportChartProfile() As ChartProfileData
        Dim profile As New ChartProfileData()
        Dim order As Integer = 0

        For Each ind As IIndicator In _indicatorEngine.GetAll()
            order += 1
            profile.Indicators.Add(New ChartProfileIndicatorItem With {
                .IndicatorType = GetIndicatorTypeName(ind),
                .IndicatorName = ind.Name,
                .DisplayOrder = order,
                .PanelIndex = ind.PanelIndex,
                .Parameters = New Dictionary(Of String, Object)(If(ind.Parameters, New Dictionary(Of String, Object)()), StringComparer.OrdinalIgnoreCase)
            })
        Next

        profile.ContextOptions = New ChartProfileContextOptions With {
            .ShowCurrentPriceLine = _options.ShowCurrentPriceLine,
            .ShowPrevCloseLine = _options.ShowPrevCloseLine,
            .ShowViLine = False,
            .ShowDayChangeLines = True,
            .ShowCrosshair = _options.ShowCrosshair,
            .IsAutoScaleY = _state.AutoScaleY,
            .ManualMaxPrice = _state.ManualPriceHigh,
            .ManualMinPrice = _state.ManualPriceLow,
            .CandleWidth = _state.CandleWidth,
            .Gap = _state.Gap,
            .VisibleCount = _state.VisibleCount,
            .PanelHeightRatio = _options.IndicatorPanelRatio
        }

        Return profile
    End Function

    Public Sub ApplyChartProfile(profile As ChartProfileData)
        If profile Is Nothing Then Return

        _indicatorEngine.Clear()

        Dim indicators As List(Of ChartProfileIndicatorItem) = If(profile.Indicators, New List(Of ChartProfileIndicatorItem)())
        indicators.Sort(Function(a As ChartProfileIndicatorItem, b As ChartProfileIndicatorItem) a.DisplayOrder.CompareTo(b.DisplayOrder))

        For Each item As ChartProfileIndicatorItem In indicators
            If item Is Nothing Then Continue For
            Dim indicator As IIndicator = CreateIndicatorByType(item.IndicatorType)
            If indicator Is Nothing Then Continue For

            If item.Parameters IsNot Nothing AndAlso item.Parameters.Count > 0 Then
                indicator.Parameters = New Dictionary(Of String, Object)(item.Parameters, StringComparer.OrdinalIgnoreCase)
            End If

            _indicatorEngine.Register(indicator)
        Next

        ApplyChartContextOptions(profile.ContextOptions)

        If _buffer.Count > 0 Then
            _indicatorEngine.CalculateAll(_buffer.Snapshot())
            RequestAuxiliaryIndicatorData()
        End If

        RequestRepaint()
    End Sub

    Private Sub ApplyChartContextOptions(options As ChartProfileContextOptions)
        If options Is Nothing Then Return

        _options.ShowCurrentPriceLine = options.ShowCurrentPriceLine
        _options.ShowPrevCloseLine = options.ShowPrevCloseLine
        _options.ShowCrosshair = options.ShowCrosshair
        _options.IndicatorPanelRatio = If(options.PanelHeightRatio > 0, options.PanelHeightRatio, _options.IndicatorPanelRatio)

        If options.CandleWidth > 0 Then _state.CandleWidth = options.CandleWidth
        If options.Gap >= 0 Then _state.Gap = options.Gap
        If options.VisibleCount > 0 Then _state.VisibleCount = options.VisibleCount

        If options.IsAutoScaleY Then
            _state.ResetManualPriceScale()
        ElseIf options.ManualMaxPrice > options.ManualMinPrice Then
            _state.AutoScaleY = False
            _state.ManualPriceHigh = options.ManualMaxPrice
            _state.ManualPriceLow = options.ManualMinPrice
        End If
    End Sub

    Private Sub NotifyChartProfileChanged()
        RaiseEvent ChartProfileChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub OnCandleLoaded(m As Msg)
        If m Is Nothing Then Return
        If m.Has("provider") AndAlso Not RuntimeChartSettings.IsMarketDataProvider(m.Str("provider")) Then Return

        Dim code As String = If(m.Has("stockCode"), m.Str("stockCode"), m.Str("code"))
        code = SharedUtil.NormalizeChartCode(code)
        If Not String.Equals(code, _stockCode, StringComparison.OrdinalIgnoreCase) Then Return

        Dim rows As List(Of Dictionary(Of String, String)) = m.DictList("rows")
        If rows Is Nothing OrElse rows.Count = 0 Then Return

        Dim list As New List(Of CandleItem)(rows.Count)

        For Each row As Dictionary(Of String, String) In rows
            Dim ci As CandleItem = RowToCandle(row)
            If ci IsNot Nothing AndAlso ci.Dt <> DateTime.MinValue Then
                list.Add(ci)
            End If
        Next

        If list.Count = 0 Then Return

        Dim pc As Single = 0.0F
        If m.Has("prevClose") Then pc = m.Sng("prevClose")

        If InvokeRequired Then
            BeginInvoke(Sub() LoadCandles(list, pc))
        Else
            LoadCandles(list, pc)
        End If
    End Sub

    Private Sub OnTickCandleLoaded(m As Msg)
        If m Is Nothing OrElse Not m.Has("rows") Then Return
        If m.Has("provider") AndAlso Not RuntimeChartSettings.IsMarketDataProvider(m.Str("provider")) Then Return

        Dim code As String = If(m.Has("stockCode"), m.Str("stockCode"), m.Str("code"))
        code = SharedUtil.NormalizeChartCode(code)
        If Not String.Equals(code, _stockCode, StringComparison.OrdinalIgnoreCase) Then Return

        Dim rows As List(Of Dictionary(Of String, String)) = m.DictList("rows")
        If rows Is Nothing OrElse rows.Count = 0 Then Return

        Dim tickBars As New List(Of DateTime)(rows.Count)

        For Each row As Dictionary(Of String, String) In rows
            Dim dt As DateTime = ParseCandleDateTime(row)
            If dt <> DateTime.MinValue Then tickBars.Add(dt)
        Next

        If tickBars.Count = 0 Then Return
        tickBars.Sort()

        If InvokeRequired Then
            BeginInvoke(Sub()
                            ApplyTickBarsToRegisteredIndicators(tickBars)
                            _indicatorEngine.CalculateAll(_buffer.Snapshot())
                            RequestRepaint()
                        End Sub)
        Else
            ApplyTickBarsToRegisteredIndicators(tickBars)
            _indicatorEngine.CalculateAll(_buffer.Snapshot())
            RequestRepaint()
        End If
    End Sub

    Private Sub OnTick(m As Msg)
        If m Is Nothing Then Return

        Dim code As String = If(m.Has("stockCode"), m.Str("stockCode"), m.Str("code"))
        code = SharedUtil.NormalizeChartCode(code)
        If Not String.Equals(code, _stockCode, StringComparison.OrdinalIgnoreCase) Then Return

        Dim price As Single = Math.Abs(m.Sng("price"))
        Dim vol As Long = If(m.Has("volume"), Math.Abs(m.Lng("volume")), 0L)
        Dim tickTime As DateTime = DateTime.Now

        If m.Has("dt") Then
            Dim parsed As DateTime = SharedUtil.ToDateTime(m.Str("dt"))
            If parsed <> DateTime.MinValue Then tickTime = parsed
        ElseIf m.Has("time") Then
            Dim parsed As DateTime = SharedUtil.ToDateTime(m.Str("time"))
            If parsed <> DateTime.MinValue Then tickTime = parsed
        End If

        If InvokeRequired Then
            BeginInvoke(Sub() ApplyRealtimeTick(price, vol, tickTime))
        Else
            ApplyRealtimeTick(price, vol, tickTime)
        End If
    End Sub

    Private Sub ApplyRealtimeTick(price As Single, volume As Long, tickTime As DateTime)
        For Each ind As IIndicator In _indicatorEngine.GetAll()
            Dim tickInd As TickIntensity_Indicator = TryCast(ind, TickIntensity_Indicator)
            If tickInd IsNot Nothing Then tickInd.AddTick(tickTime)
        Next

        _buffer.UpdateLastFromTick(price, volume, tickTime, 1)
        _indicatorEngine.UpdateLast(_buffer.Snapshot())
        RequestRepaint()
    End Sub

    Private Sub ApplyTickBarsToRegisteredIndicators(tickBars As List(Of DateTime))
        For Each ind As IIndicator In _indicatorEngine.GetAll()
            Dim tickInd As TickIntensity_Indicator = TryCast(ind, TickIntensity_Indicator)
            If tickInd IsNot Nothing Then tickInd.SetTickBars(tickBars)
        Next
    End Sub

    Private Sub RequestAuxiliaryIndicatorData()
        Dim hasTickIntensity As Boolean = False

        For Each ind As IIndicator In _indicatorEngine.GetAll()
            If TypeOf ind Is TickIntensity_Indicator Then
                hasTickIntensity = True
                Exit For
            End If
        Next

        If hasTickIntensity Then
            Dim tickUnit As Integer = RuntimeChartSettings.DefaultTickUnit
            MessageBus.I.Emit(Topics.TICK_CANDLE_REQUEST,
                              "code", _stockCode,
                              "provider", RuntimeChartSettings.MarketDataProvider,
                              "count", RuntimeChartSettings.TickRequestMinCount,
                              "tickUnit", tickUnit,
                              "timeframe", RuntimeChartSettings.TickTimeframe(tickUnit))
        End If
    End Sub

    Private Shared Function GetIndicatorTypeName(ind As IIndicator) As String
        If ind Is Nothing Then Return ""
        Return ind.GetType().Name
    End Function

    Private Shared Function CreateIndicatorByType(typeName As String) As IIndicator
        Dim t As String = If(typeName, "").Trim()
        If t = "" Then Return Nothing

        Select Case t.ToUpperInvariant()
            Case "MA_INDICATOR", "MA"
                Return New MA_Indicator()
            Case "RSI_INDICATOR", "RSI"
                Return New RSI_Indicator()
            Case "MACD_INDICATOR", "MACD"
                Return New MACD_Indicator()
            Case "BOLLINGER_INDICATOR", "BOLLINGER", "BB"
                Return New Bollinger_Indicator()
            Case "SUPER TREND", "SUPER_TREND", "SUPERTREND", "SUPERTREND_INDICATOR"
                Return New SuperTrend_Indicator()
            Case "VWAP_INDICATOR", "VWAP"
                Return New VWAP_Indicator()
            Case "VOLUME_INDICATOR", "VOLUME"
                Return New Volume_Indicator()
            Case "OBV_INDICATOR", "OBV"
                Return New OBV_Indicator()
            Case "DISPARITY_INDICATOR", "DISPARITY"
                Return New Disparity_Indicator()
            Case "JMA_INDICATOR", "JMA"
                Return New JMA_Indicator()
            Case "TICKINTENSITY_INDICATOR", "TICKINTENSITY", "TICKINT"
                Return New TickIntensity_Indicator()
            Case "TRADESTRENGTH_INDICATOR", "TRADESTRENGTH"
                Return New TradeStrength_Indicator()
            Case "PROGRAMTRADE_INDICATOR", "PROGRAMTRADE"
                Return New ProgramTrade_Indicator()
            Case "CUMTRADEAMOUNT_INDICATOR", "CUMTRADEAMOUNT"
                Return New CumTradeAmount_Indicator()
            Case "SECTORLEADER_INDICATOR", "SECTORLEADER"
                Return New SectorLeader_Indicator()
        End Select

        Return Nothing
    End Function

    Private Shared Function RowToCandle(row As Dictionary(Of String, String)) As CandleItem
        If row Is Nothing Then Return Nothing

        Dim ci As New CandleItem()
        ci.Dt = ParseCandleDateTime(row)
        ci.Open = RowSingle(row, "open", "시가")
        ci.High = RowSingle(row, "high", "고가")
        ci.Low = RowSingle(row, "low", "저가")
        ci.Close = RowSingle(row, "close", "현재가")
        ci.Volume = RowLong(row, "volume", "거래량")
        Return ci
    End Function

    Private Shared Function ParseCandleDateTime(row As Dictionary(Of String, String)) As DateTime
        If row Is Nothing Then Return DateTime.MinValue

        Dim dtText As String = RowString(row, "dt", "datetime", "dateTime", "일시")
        If Not String.IsNullOrWhiteSpace(dtText) Then
            Dim parsedDt As DateTime = ParseDateTimeText(dtText)
            If parsedDt <> DateTime.MinValue Then Return parsedDt
        End If

        Dim dateText As String = RowString(row, "date", "일자")
        Dim timeText As String = RowString(row, "time", "시간")

        If String.IsNullOrWhiteSpace(dateText) Then Return DateTime.MinValue

        Dim baseDate As DateTime = ParseDateText(dateText)
        If baseDate = DateTime.MinValue Then Return DateTime.MinValue

        If String.IsNullOrWhiteSpace(timeText) Then Return baseDate

        Dim cleanTime As String = OnlyDigits(timeText)
        If cleanTime.Length <= 0 Then Return baseDate

        Dim hh As Integer = 0
        Dim mm As Integer = 0
        Dim ss As Integer = 0

        If cleanTime.Length <= 4 Then
            cleanTime = cleanTime.PadLeft(4, "0"c)
            If Not Integer.TryParse(cleanTime.Substring(0, 2), hh) Then Return baseDate
            If Not Integer.TryParse(cleanTime.Substring(2, 2), mm) Then Return baseDate
            ss = 0
        Else
            If cleanTime.Length < 6 Then cleanTime = cleanTime.PadLeft(6, "0"c)
            If cleanTime.Length > 6 Then cleanTime = cleanTime.Substring(cleanTime.Length - 6, 6)
            If Not Integer.TryParse(cleanTime.Substring(0, 2), hh) Then Return baseDate
            If Not Integer.TryParse(cleanTime.Substring(2, 2), mm) Then Return baseDate
            If Not Integer.TryParse(cleanTime.Substring(4, 2), ss) Then Return baseDate
        End If

        If hh < 0 OrElse hh > 23 Then Return baseDate
        If mm < 0 OrElse mm > 59 Then Return baseDate
        If ss < 0 OrElse ss > 59 Then Return baseDate

        Return New DateTime(baseDate.Year, baseDate.Month, baseDate.Day, hh, mm, ss)
    End Function

    Private Shared Function ParseDateTimeText(value As String) As DateTime
        Dim s As String = If(value, "").Trim()
        If s.Length <= 0 Then Return DateTime.MinValue

        Dim parsed As DateTime
        If DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return parsed
        If DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, parsed) Then Return parsed

        Dim digits As String = OnlyDigits(s)
        If digits.Length >= 14 Then
            digits = digits.Substring(0, 14)
            If DateTime.TryParseExact(digits, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return parsed
        End If

        If digits.Length = 12 Then
            If DateTime.TryParseExact(digits, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return parsed
        End If

        If digits.Length = 8 Then
            If DateTime.TryParseExact(digits, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return parsed
        End If

        Return DateTime.MinValue
    End Function

    Private Shared Function ParseDateText(value As String) As DateTime
        Dim s As String = If(value, "").Trim()
        If s.Length <= 0 Then Return DateTime.MinValue

        Dim parsed As DateTime
        If DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return parsed.Date
        If DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, parsed) Then Return parsed.Date

        Dim digits As String = OnlyDigits(s)
        If digits.Length >= 8 Then
            digits = digits.Substring(0, 8)
            If DateTime.TryParseExact(digits, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then Return parsed.Date
        End If

        Return DateTime.MinValue
    End Function

    Private Shared Function RowString(row As Dictionary(Of String, String), ParamArray keys As String()) As String
        If row Is Nothing OrElse keys Is Nothing Then Return ""

        For i As Integer = 0 To keys.Length - 1
            Dim key As String = keys(i)
            If String.IsNullOrWhiteSpace(key) Then Continue For

            Dim value As String = ""
            If row.TryGetValue(key, value) Then Return If(value, "")
        Next

        Return ""
    End Function

    Private Shared Function OnlyDigits(value As String) As String
        If String.IsNullOrEmpty(value) Then Return ""

        Dim chars As Char() = value.ToCharArray()
        Dim buffer As New System.Text.StringBuilder(chars.Length)

        For i As Integer = 0 To chars.Length - 1
            If Char.IsDigit(chars(i)) Then buffer.Append(chars(i))
        Next

        Return buffer.ToString()
    End Function

    Private Shared Function RowSingle(row As Dictionary(Of String, String), key1 As String, key2 As String) As Single
        Dim s As String = ""
        If row.TryGetValue(key1, s) Then
            Dim v As Single = 0.0F
            If Single.TryParse(s, v) Then Return v
        End If

        If row.TryGetValue(key2, s) Then
            Dim v As Single = 0.0F
            If Single.TryParse(s, v) Then Return v
        End If

        Return 0.0F
    End Function

    Private Shared Function RowLong(row As Dictionary(Of String, String), key1 As String, key2 As String) As Long
        Dim s As String = ""
        If row.TryGetValue(key1, s) Then
            Dim v As Long = 0
            If Long.TryParse(s, v) Then Return v
        End If

        If row.TryGetValue(key2, s) Then
            Dim v As Long = 0
            If Long.TryParse(s, v) Then Return v
        End If

        Return 0L
    End Function

    Private Sub OnFrameTick(sender As Object, e As EventArgs)
        If _needsRepaint Then
            _needsRepaint = False
            _sk.Invalidate()
        End If
    End Sub

    Private Sub RequestRepaint()
        _needsRepaint = True
    End Sub

    Private Sub MoveToLatest()
        Dim total As Integer = _buffer.Count
        If total <= 0 Then Return

        _state.VisibleCount = Math.Min(Math.Max(10, _state.VisibleCount), total)
        Dim pad As Integer = Math.Min(RIGHT_DRAG_PADDING_BARS, Math.Max(3, CInt(Math.Round(_state.VisibleCount * 0.08R))))
        _state.StartIndex = Math.Max(0, total - _state.VisibleCount + pad)
        _state.Clamp(total)
    End Sub

    Private Sub OnPaintSurface(sender As Object, e As SKPaintSurfaceEventArgs)
        Dim canvas As SKCanvas = e.Surface.Canvas
        canvas.Clear(SafeChartPalette.Background)

        Dim candles As List(Of CandleItem) = _buffer.Snapshot()
        If candles.Count = 0 Then Return

        _state.Clamp(candles.Count)
        _geo.Build(e.Info.Width, e.Info.Height, _options, GetActivePanelIndexes())
        BuildVisibleRanges(candles)

        _axisRenderer.Render(canvas, candles, _state, _geo)
        _candleRenderer.Render(canvas, candles, _state, _geo, _options)
        _indicatorRenderer.RenderIndicators(canvas, _indicatorEngine.GetAll(), _indicatorEngine.Results, candles, _state, _geo)

        RenderCrosshair(canvas, candles)
        RenderTitle(canvas, candles)
    End Sub

    Private Function GetActivePanelIndexes() As List(Of Integer)
        Dim panels As New List(Of Integer)()

        For Each ind As IIndicator In _indicatorEngine.GetAll()
            If ind Is Nothing Then Continue For
            If ind.PanelIndex <= 0 Then Continue For
            If Not panels.Contains(ind.PanelIndex) Then panels.Add(ind.PanelIndex)
        Next

        panels.Sort()
        Return panels
    End Function

    Private Sub BuildVisibleRanges(candles As List(Of CandleItem))
        Dim endIdx As Integer = _state.EndIndex(candles.Count)
        If endIdx < _state.StartIndex Then Return

        Dim hi As Single = Single.MinValue
        Dim lo As Single = Single.MaxValue
        Dim volMax As Long = 1

        For i As Integer = _state.StartIndex To endIdx
            Dim c As CandleItem = candles(i)
            If c Is Nothing Then Continue For

            If c.High > hi Then hi = CSng(c.High)
            If c.Low < lo Then lo = CSng(c.Low)
            If c.Volume > volMax Then volMax = c.Volume
        Next

        If hi <= lo Then
            hi += 1.0F
            lo -= 1.0F
        End If

        Dim pad As Single = (hi - lo) * 0.06F
        _geo.PriceHigh = hi + pad
        _geo.PriceLow = lo - pad
        _geo.VolumeMax = volMax

        If Not _state.AutoScaleY AndAlso _state.ManualPriceHigh > _state.ManualPriceLow Then
            _geo.PriceHigh = _state.ManualPriceHigh
            _geo.PriceLow = _state.ManualPriceLow
        End If
    End Sub

    Private Sub RenderTitle(canvas As SKCanvas, candles As List(Of CandleItem))
        Using p As New SKPaint With {.Color = SafeChartPalette.TextBright, .TextSize = 12.0F, .IsAntialias = True}
            Dim last As CandleItem = candles(candles.Count - 1)
            Dim yMode As String = If(_state.AutoScaleY, "AUTO", "MANUAL")
            Dim text As String = $"SAFE V2 {_stockCode} {_stockName}  Indicators={_indicatorEngine.GetAll().Count}  Panels={GetActivePanelIndexes().Count}  Bars={candles.Count}  Last={last.Dt:yyyy-MM-dd HH:mm}  C={last.Close:N0}  Y={yMode}"
            canvas.DrawText(text, 12.0F, 18.0F, p)
        End Using
    End Sub

    Private Sub RenderCrosshair(canvas As SKCanvas, candles As List(Of CandleItem))
        If Not _options.ShowCrosshair OrElse Not _state.MouseInside Then Return

        Using p As New SKPaint With {.Color = SafeChartPalette.Crosshair, .StrokeWidth = 1.0F, .Style = SKPaintStyle.Stroke}
            canvas.DrawLine(_state.MouseX, _geo.MainRect.Top, _state.MouseX, _geo.IndicatorRect.Bottom, p)
            canvas.DrawLine(_geo.MainRect.Left, _state.MouseY, _geo.MainRect.Right, _state.MouseY, p)
        End Using
    End Sub

    Private Sub OnMouseDownSafe(sender As Object, e As MouseEventArgs)
        If e Is Nothing Then Return
        _sk.Focus()
        If e.Button = MouseButtons.Left Then
            _interaction.OnMouseDown(e, _buffer.Count, _geo.MainRect.Right, _geo.PriceHigh, _geo.PriceLow)
        End If
    End Sub

    Private Sub OnMouseUpSafe(sender As Object, e As MouseEventArgs)
        _interaction.OnMouseUp(e)
    End Sub

    Private Sub OnMouseMoveSafe(sender As Object, e As MouseEventArgs)
        _interaction.OnMouseMove(e, _buffer.Count)
    End Sub

    Private Sub OnMouseLeaveSafe(sender As Object, e As EventArgs)
        _interaction.OnMouseLeave()
    End Sub

    Private Sub OnMouseWheelSafe(sender As Object, e As MouseEventArgs)
        _interaction.OnMouseWheel(e, _buffer.Count)
    End Sub

    Private Sub OnMouseDoubleClickSafe(sender As Object, e As MouseEventArgs)
        If e Is Nothing Then Return
        If e.Button = MouseButtons.Left Then
            _interaction.ResetPriceScale()
            NotifyChartProfileChanged()
        End If
    End Sub

    Private Function BuildContextMenu() As ContextMenuStrip
        Dim menu As New ContextMenuStrip()

        Dim mnuLatest As New ToolStripMenuItem("최신봉으로 이동")
        AddHandler mnuLatest.Click,
            Sub()
                _interaction.MoveToLatest(_buffer.Count)
            End Sub
        menu.Items.Add(mnuLatest)

        Dim mnuShowAll As New ToolStripMenuItem("전체 보기")
        AddHandler mnuShowAll.Click,
            Sub()
                If _buffer.Count > 0 Then
                    _state.StartIndex = 0
                    _state.VisibleCount = _buffer.Count
                    _state.Clamp(_buffer.Count)
                    RequestRepaint()
                    NotifyChartProfileChanged()
                End If
            End Sub
        menu.Items.Add(mnuShowAll)

        Dim mnuResetY As New ToolStripMenuItem("Y축 자동 복귀")
        AddHandler mnuResetY.Click,
            Sub()
                _interaction.ResetPriceScale()
                NotifyChartProfileChanged()
            End Sub
        menu.Items.Add(mnuResetY)

        menu.Items.Add(New ToolStripSeparator())

        Dim mnuCross As New ToolStripMenuItem("십자선 표시")
        mnuCross.CheckOnClick = True
        AddHandler mnuCross.DropDownOpening, Sub() mnuCross.Checked = _options.ShowCrosshair
        AddHandler mnuCross.Click,
            Sub()
                _options.ShowCrosshair = mnuCross.Checked
                RequestRepaint()
                NotifyChartProfileChanged()
            End Sub
        menu.Items.Add(mnuCross)

        Dim mnuCurrentLine As New ToolStripMenuItem("현재가선 표시")
        mnuCurrentLine.CheckOnClick = True
        AddHandler mnuCurrentLine.DropDownOpening, Sub() mnuCurrentLine.Checked = _options.ShowCurrentPriceLine
        AddHandler mnuCurrentLine.Click,
            Sub()
                _options.ShowCurrentPriceLine = mnuCurrentLine.Checked
                RequestRepaint()
                NotifyChartProfileChanged()
            End Sub
        menu.Items.Add(mnuCurrentLine)

        Dim mnuPrevLine As New ToolStripMenuItem("전일종가선 표시")
        mnuPrevLine.CheckOnClick = True
        AddHandler mnuPrevLine.DropDownOpening, Sub() mnuPrevLine.Checked = _options.ShowPrevCloseLine
        AddHandler mnuPrevLine.Click,
            Sub()
                _options.ShowPrevCloseLine = mnuPrevLine.Checked
                RequestRepaint()
                NotifyChartProfileChanged()
            End Sub
        menu.Items.Add(mnuPrevLine)

        menu.Items.Add(New ToolStripSeparator())

        Dim mnuSave As New ToolStripMenuItem("현재 차트 상태 저장")
        AddHandler mnuSave.Click,
            Sub()
                NotifyChartProfileChanged()
            End Sub
        menu.Items.Add(mnuSave)

        Return menu
    End Function
End Class
