' ═══════════════════════════════════════════════════════════════
' ChartForm.vb — 주식 차트 도킹 폼
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports [Shared]
Imports WeifenLuo.WinFormsUI.Docking
Imports MainApp.Models
Imports MainApp.Services

Public Class ChartForm
    Inherits DockFormBase
    Implements IChartHost

    Private _chart As FastChartControl
    Private _stockCode As String = ""

    Public Sub New()
        Me.Text = "차트"
        InitializeChart()
    End Sub

    Private Sub InitializeChart()
        _chart = New FastChartControl()
        _chart.Dock = DockStyle.Fill
        _chart.SetHost(Me)
        Me.Controls.Add(_chart)

        AddHandler _chart.IndicatorSettingRequested, AddressOf OnIndicatorSettingRequested
        AddHandler _chart.ChartProfileChanged, AddressOf OnChartProfileChanged
        AddHandler _chart.StrategySettingRequested, AddressOf OnStrategySettingRequested
        AddHandler _chart.DataViewRequested, AddressOf OnDataViewRequested

        Dim profilePath = Path.Combine(Application.StartupPath, "chart_profile.json")
        If File.Exists(profilePath) Then
            _chart.ApplyChartProfile(ChartProfileService.I.GetProfile())
        End If
    End Sub

    Private Sub OnIndicatorSettingRequested(sender As Object, e As EventArgs)
        Dim ind = _chart.GetSelectedIndicator()
        If ind Is Nothing Then Return

        Using f As New IndicatorSettingForm(ind)
            If f.ShowDialog(Me) = DialogResult.OK Then
                _chart.ReCalculate()
                SaveChartProfile()
            End If
        End Using
    End Sub

    Private Sub OnChartProfileChanged(sender As Object, e As EventArgs)
        SaveChartProfile()
    End Sub

    Private Sub SaveChartProfile()
        ChartProfileService.I.SaveProfile(_chart.ExportChartProfile())
    End Sub

    Private Sub OnStrategySettingRequested(sender As Object, e As EventArgs)
        Using f As New StrategyManagerForm(Sub(strat)
                                               ' 전략 적용 로직
                                               _chart.ApplyStrategy(strat)
                                           End Sub)
            f.ShowDialog(Me)
        End Using
    End Sub

    Private Sub OnDataViewRequested(sender As Object, e As EventArgs)
        Dim dataArrays = _chart.GetDataArrays()
        Dim shell = TryCast(Me.FindForm(), MainShell)
        If shell IsNot Nothing Then
            shell.ShowDataView(_stockCode, dataArrays)
            Return
        End If

        Dim frm As New frmDataView()
        frm.SetData(_stockCode, dataArrays)
        frm.Show(Me.DockPanel, DockState.Float)
    End Sub

    Public Sub SetStock(stockCode As String)
        _stockCode = SharedUtil.NormalizeChartCode(stockCode)
        Dim name = GetStockName(_stockCode)
        Me.Text = $"[{_stockCode}] {name}"
        _chart.SetStock(_stockCode)
    End Sub

    ' ════════════════════════════════════════
    ' IChartHost 구현
    ' ════════════════════════════════════════

    Public Function GetStockName(stockCode As String) As String Implements IChartHost.GetStockName
        Dim item = StockInfoManager.I.GetItem(stockCode)
        If item IsNot Nothing Then Return item.Name
        Return stockCode
    End Function

    Public Sub RequestCandles(stockCode As String, chartType As String, count As Integer) Implements IChartHost.RequestCandles
        Dim requestCode = SharedUtil.NormalizeChartCode(stockCode)
        If StockInfoManager.I.TryEmitCachedCandles(requestCode, count) Then Return
        StockInfoManager.I.MarkCandleRequested(requestCode)

        ' 차트 타입에 따른 캔들 요청 토픽 결정
        Dim topic = Topics.CANDLE_REQUEST
        If chartType = "day" Then topic = Topics.DAILY_REQUEST

        MessageBus.I.Emit(topic,
                          "code", requestCode,
                          "stockCode", requestCode, ' FastChartControl handles "stockCode" or "code"
                          "provider", RuntimeChartSettings.MarketDataProvider,
                          "timeframe", RuntimeChartSettings.DefaultCandleTimeframe,
                          "count", count)
    End Sub

    Public Sub SubscribeRealtime(stockCode As String) Implements IChartHost.SubscribeRealtime
        MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", stockCode)
    End Sub

    Public Sub UnsubscribeRealtime(stockCode As String) Implements IChartHost.UnsubscribeRealtime
        ' 여기서 해제하면 다른 폼이 쓰고 있을 수 있으므로 신중해야 함.
        ' 매니저가 관리하므로 호스트에서는 요청만 하거나 무시할 수 있음.
    End Sub

    Protected Overrides Sub UnsubscribeAll()
        ' FastChartControl 내부에서 MessageBus 구독 해제함
        MyBase.UnsubscribeAll()
    End Sub

End Class
