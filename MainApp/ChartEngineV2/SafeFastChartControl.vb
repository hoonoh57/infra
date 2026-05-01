Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports System.Windows.Forms
Imports [Shared]
Imports SkiaSharp
Imports SkiaSharp.Views.Desktop

Public Class SafeFastChartControl
    Inherits UserControl

    Private Const FRAME_INTERVAL_MS As Integer = 16

    Private ReadOnly _options As New SafeChartOptions()
    Private ReadOnly _state As New SafeChartState()
    Private ReadOnly _buffer As New SafeChartDataBuffer()
    Private ReadOnly _geo As New SafeChartGeometry()
    Private ReadOnly _indicator As New SafeChartIndicatorBridge()

    Private ReadOnly _axisRenderer As New SafeAxisRenderer()
    Private ReadOnly _candleRenderer As New SafeCandleRenderer()
    Private ReadOnly _indicatorRenderer As New SafeIndicatorRenderer()

    Private ReadOnly _sk As SKControl
    Private ReadOnly _frameTimer As Timer
    Private ReadOnly _interaction As SafeInteractionController

    Private _needsRepaint As Boolean = True
    Private _stockCode As String = ""
    Private _stockName As String = ""
    Private _chartType As String = "minute"
    Private _requestedCount As Integer = RuntimeChartSettings.DefaultChartOpenCount

    Public Sub New()
        DoubleBuffered = True

        _state.VisibleCount = _options.InitialVisibleBars
        _state.CandleWidth = _options.CandleWidth
        _state.Gap = _options.CandleGap

        _sk = New SKControl()
        _sk.Dock = DockStyle.Fill
        Controls.Add(_sk)

        AddHandler _sk.PaintSurface, AddressOf OnPaintSurface
        AddHandler _sk.MouseDown, AddressOf OnMouseDownSafe
        AddHandler _sk.MouseUp, AddressOf OnMouseUpSafe
        AddHandler _sk.MouseMove, AddressOf OnMouseMoveSafe
        AddHandler _sk.MouseLeave, AddressOf OnMouseLeaveSafe
        AddHandler _sk.MouseWheel, AddressOf OnMouseWheelSafe
        AddHandler _sk.Resize, Sub() RequestRepaint()

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

        Dim tickUnit As Integer = RuntimeChartSettings.DefaultTickUnit
        MessageBus.I.Emit(Topics.TICK_CANDLE_REQUEST,
                          "code", _stockCode,
                          "provider", RuntimeChartSettings.MarketDataProvider,
                          "count", RuntimeChartSettings.TickRequestMinCount,
                          "tickUnit", tickUnit,
                          "timeframe", RuntimeChartSettings.TickTimeframe(tickUnit))

        MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", _stockCode)

        RequestRepaint()
    End Sub

    Public Sub LoadCandles(candles As List(Of CandleItem), Optional prevClose As Single = 0.0F)
        _buffer.SetCandles(candles, prevClose)
        _indicator.CalculateAll(_buffer.Snapshot())
        MoveToLatest()
        RequestRepaint()
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
            Dim dt As DateTime = MarketDataRowParser.ParseCandleDateTime(row)
            If dt <> DateTime.MinValue Then tickBars.Add(dt)
        Next

        If tickBars.Count = 0 Then Return
        tickBars.Sort()

        If InvokeRequired Then
            BeginInvoke(Sub()
                            _indicator.SetTickBars(tickBars)
                            _indicator.CalculateAll(_buffer.Snapshot())
                            RequestRepaint()
                        End Sub)
        Else
            _indicator.SetTickBars(tickBars)
            _indicator.CalculateAll(_buffer.Snapshot())
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
        _indicator.AddRealtimeTick(tickTime)
        _buffer.UpdateLastFromTick(price, volume, tickTime, 1)
        _indicator.UpdateLast(_buffer.Snapshot())
        RequestRepaint()
    End Sub

    Private Shared Function RowToCandle(row As Dictionary(Of String, String)) As CandleItem
        If row Is Nothing Then Return Nothing

        Dim ci As New CandleItem()
        ci.Dt = MarketDataRowParser.ParseCandleDateTime(row)
        ci.Open = RowSingle(row, "open", "시가")
        ci.High = RowSingle(row, "high", "고가")
        ci.Low = RowSingle(row, "low", "저가")
        ci.Close = RowSingle(row, "close", "현재가")
        ci.Volume = RowLong(row, "volume", "거래량")
        Return ci
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
        _state.StartIndex = Math.Max(0, total - _state.VisibleCount)
        _state.Clamp(total)
    End Sub

    Private Sub OnPaintSurface(sender As Object, e As SKPaintSurfaceEventArgs)
        Dim canvas As SKCanvas = e.Surface.Canvas
        canvas.Clear(SafeChartPalette.Background)

        Dim candles As List(Of CandleItem) = _buffer.Snapshot()
        If candles.Count = 0 Then Return

        _state.Clamp(candles.Count)
        _geo.Build(e.Info.Width, e.Info.Height, _options)
        BuildVisibleRanges(candles)

        _axisRenderer.Render(canvas, candles, _state, _geo)
        _candleRenderer.Render(canvas, candles, _state, _geo, _options)
        _indicatorRenderer.RenderTickIntensity(canvas, _indicator.GetTickResults(), candles, _state, _geo)

        RenderCrosshair(canvas, candles)
        RenderTitle(canvas, candles)
    End Sub

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
    End Sub

    Private Sub RenderTitle(canvas As SKCanvas, candles As List(Of CandleItem))
        Using p As New SKPaint With {.Color = SafeChartPalette.TextBright, .TextSize = 12.0F, .IsAntialias = True}
            Dim last As CandleItem = candles(candles.Count - 1)
            Dim text As String = $"SAFE V2 {_stockCode} {_stockName}  Bars={candles.Count}  Last={last.Dt:yyyy-MM-dd HH:mm}  C={last.Close:N0}"
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
        _interaction.OnMouseDown(e)
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
End Class

