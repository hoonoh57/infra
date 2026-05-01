Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports [Shared]
Imports MainApp.Models
Imports MainApp.Services

Public Class SafeChartHostForm
    Inherits Form
    Implements IChartHost

    Private ReadOnly _chart As SafeFastChartControl
    Private _stockCode As String = ""
    Private ReadOnly _initialStockCode As String
    Private ReadOnly _chartType As String
    Private ReadOnly _count As Integer

    Public Sub New(stockCode As String, Optional chartType As String = "minute", Optional count As Integer = 300)
        _initialStockCode = SharedUtil.NormalizeChartCode(stockCode)
        _chartType = chartType
        _count = count

        Me.Text = "Safe Chart V2 - " & _initialStockCode
        Me.Width = 1200
        Me.Height = 800
        Me.StartPosition = FormStartPosition.CenterScreen

        _chart = New SafeFastChartControl()
        _chart.Dock = DockStyle.Fill
        _chart.SetHost(Me)
        Me.Controls.Add(_chart)

        AddHandler _chart.IndicatorSettingRequested, AddressOf OnIndicatorSettingRequested
        AddHandler _chart.ChartProfileChanged, AddressOf OnChartProfileChanged
        AddHandler _chart.StrategySettingRequested, AddressOf OnStrategySettingRequested
        AddHandler _chart.DataViewRequested, AddressOf OnDataViewRequested
        AddHandler Me.Shown, AddressOf OnSafeChartHostShown
    End Sub

    Private Sub OnSafeChartHostShown(sender As Object, e As EventArgs)
        Dim profilePath As String = Path.Combine(Application.StartupPath, "chart_profile.json")
        If File.Exists(profilePath) Then
            _chart.ApplyChartProfile(ChartProfileService.I.GetProfile())
        End If
        SetStock(_initialStockCode)
    End Sub

    Public Sub SetStock(stockCode As String)
        _stockCode = SharedUtil.NormalizeChartCode(stockCode)
        Dim name As String = GetStockName(_stockCode)
        Me.Text = "Safe Chart V2 - [" & _stockCode & "] " & name
        _chart.SetStock(_stockCode, _chartType, _count)
    End Sub

    Private Sub OnIndicatorSettingRequested(sender As Object, e As EventArgs)
        Dim ind As IIndicator = _chart.GetSelectedIndicator()
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
        Using f As New StrategyManagerForm(AddressOf ApplyStrategyDefinitionAndAnalyze,
                                           AddressOf ApplyHardcodedStrategyAndAnalyze)
            f.ShowDialog(Me)
        End Using
    End Sub

    Private Sub ApplyStrategyDefinitionAndAnalyze(strat As StrategyDefinition)
        If strat Is Nothing Then Return

        _chart.ApplyStrategy(strat)

        Dim builtIn As IStrategy = BuiltInStrategyFactory.CreateStrategy(strat.Name)
        If builtIn IsNot Nothing Then
            RunStrategyAnalysis(builtIn)
            Return
        End If

        AppLogger.I.Info("V2 전략 적용 완료: " & strat.Name, "ChartStrategy")
    End Sub

    Private Sub ApplyHardcodedStrategyAndAnalyze(strategy As IStrategy)
        If strategy Is Nothing Then Return

        _chart.ApplyStrategy(strategy)
        RunStrategyAnalysis(strategy)
    End Sub

    Private Sub RunStrategyAnalysis(strategy As IStrategy)
        If strategy Is Nothing Then Return

        Dim result As ChartStrategyAnalysisResult = ChartStrategyAnalysisService.Run(_chart, strategy)
        AppLogger.I.Info("V2 전략 분석: " & result.StockCode & " / " & result.StrategyDisplayName & " / 신호 " & result.SignalCount.ToString() & "건 / 거래 " & result.TradeCount.ToString() & "건 / " & result.Message, "ChartStrategy")

        Using f As New StrategyBacktestResultForm(result)
            f.ShowDialog(Me)
        End Using
    End Sub

    Private Sub OnDataViewRequested(sender As Object, e As EventArgs)
        Dim dataArrays As Dictionary(Of String, Single()) = _chart.GetDataArrays()
        Dim frm As New frmDataView()
        frm.SetData(_stockCode, dataArrays)
        frm.Show(Me)
    End Sub

    Public Function GetStockName(stockCode As String) As String Implements IChartHost.GetStockName
        Dim item = StockInfoManager.I.GetItem(stockCode)
        If item IsNot Nothing Then Return item.Name
        Return stockCode
    End Function

    Public Sub RequestCandles(stockCode As String, chartType As String, count As Integer) Implements IChartHost.RequestCandles
        Dim requestCode As String = SharedUtil.NormalizeChartCode(stockCode)
        If StockInfoManager.I.TryEmitCachedCandles(requestCode, count) Then Return
        StockInfoManager.I.MarkCandleRequested(requestCode)

        Dim topic As String = Topics.CANDLE_REQUEST
        If String.Equals(chartType, "day", StringComparison.OrdinalIgnoreCase) Then topic = Topics.DAILY_REQUEST

        MessageBus.I.Emit(topic,
                          "code", requestCode,
                          "stockCode", requestCode,
                          "provider", RuntimeChartSettings.MarketDataProvider,
                          "timeframe", RuntimeChartSettings.DefaultCandleTimeframe,
                          "count", count)
    End Sub

    Public Sub SubscribeRealtime(stockCode As String) Implements IChartHost.SubscribeRealtime
        MessageBus.I.Emit(Topics.REALTIME_SUBSCRIBE, "codes", stockCode)
    End Sub

    Public Sub UnsubscribeRealtime(stockCode As String) Implements IChartHost.UnsubscribeRealtime
        ' 원본 ChartForm과 동일하게 구독 해제는 매니저 정책에 맡긴다.
    End Sub
End Class
