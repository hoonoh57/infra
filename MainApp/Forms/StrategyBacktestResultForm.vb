Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Drawing
Imports System.Windows.Forms

Public Class StrategyBacktestResultForm
    Inherits Form

    Private ReadOnly _result As ChartStrategyAnalysisResult
    Private _lblSummary As Label
    Private _tabs As TabControl
    Private _gridSignals As DataGridView
    Private _gridTrades As DataGridView
    Private _gridDecisionLogs As DataGridView
    Private _txtMessage As TextBox

    Public Sub New(result As ChartStrategyAnalysisResult)
        _result = If(result, New ChartStrategyAnalysisResult())
        InitializeUI()
        BindData()
    End Sub

    Private Sub InitializeUI()
        Me.Text = "전략 백테스트 및 결과분석"
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Size = New Size(1280, 800)
        Me.BackColor = Color.FromArgb(24, 26, 32)
        Me.ForeColor = Color.White

        _lblSummary = New Label()
        _lblSummary.Dock = DockStyle.Top
        _lblSummary.Height = 78
        _lblSummary.Padding = New Padding(12, 8, 12, 8)
        _lblSummary.Font = New Font("맑은 고딕", 10.0F, FontStyle.Bold)
        _lblSummary.ForeColor = Color.White
        _lblSummary.BackColor = Color.FromArgb(35, 38, 48)

        _tabs = New TabControl()
        _tabs.Dock = DockStyle.Fill
        _tabs.Font = New Font("맑은 고딕", 9.0F, FontStyle.Regular)

        Dim tabTrades As New TabPage("매매 성능")
        Dim tabSignals As New TabPage("신호 상세")
        Dim tabDecision As New TabPage("차단 사유")
        Dim tabMessage As New TabPage("분석 로그")

        _gridTrades = CreateGrid()
        _gridSignals = CreateGrid()
        _gridDecisionLogs = CreateGrid()
        _txtMessage = New TextBox()
        _txtMessage.Dock = DockStyle.Fill
        _txtMessage.Multiline = True
        _txtMessage.ReadOnly = True
        _txtMessage.ScrollBars = ScrollBars.Both
        _txtMessage.BackColor = Color.FromArgb(18, 20, 26)
        _txtMessage.ForeColor = Color.Gainsboro
        _txtMessage.Font = New Font("Consolas", 10.0F)

        tabTrades.Controls.Add(_gridTrades)
        tabSignals.Controls.Add(_gridSignals)
        tabDecision.Controls.Add(_gridDecisionLogs)
        tabMessage.Controls.Add(_txtMessage)

        _tabs.TabPages.Add(tabTrades)
        _tabs.TabPages.Add(tabSignals)
        _tabs.TabPages.Add(tabDecision)
        _tabs.TabPages.Add(tabMessage)

        Me.Controls.Add(_tabs)
        Me.Controls.Add(_lblSummary)
    End Sub

    Private Shared Function CreateGrid() As DataGridView
        Dim grid As New DataGridView()
        grid.Dock = DockStyle.Fill
        grid.AllowUserToAddRows = False
        grid.AllowUserToDeleteRows = False
        grid.AllowUserToResizeRows = False
        grid.ReadOnly = True
        grid.MultiSelect = False
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        grid.RowHeadersVisible = False
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
        grid.BackgroundColor = Color.FromArgb(18, 20, 26)
        grid.BorderStyle = BorderStyle.None
        grid.GridColor = Color.FromArgb(55, 60, 72)
        grid.EnableHeadersVisualStyles = False
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 44, 56)
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
        grid.ColumnHeadersDefaultCellStyle.Font = New Font("맑은 고딕", 9.0F, FontStyle.Bold)
        grid.DefaultCellStyle.BackColor = Color.FromArgb(24, 26, 32)
        grid.DefaultCellStyle.ForeColor = Color.White
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(70, 90, 130)
        grid.DefaultCellStyle.SelectionForeColor = Color.White
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(30, 32, 40)
        Return grid
    End Function

    Private Sub BindData()
        _lblSummary.Text = BuildSummaryText()
        _gridTrades.DataSource = _result.TradeTable
        _gridSignals.DataSource = _result.SignalTable
        _gridDecisionLogs.DataSource = _result.DecisionLogTable
        _txtMessage.Text = BuildMessageText()
    End Sub

    Private Function BuildSummaryText() As String
        Dim startText As String = If(_result.StartTimeStamp = DateTime.MinValue, "-", _result.StartTimeStamp.ToString("yyyy-MM-dd HH:mm"))
        Dim endText As String = If(_result.EndTimeStamp = DateTime.MinValue, "-", _result.EndTimeStamp.ToString("yyyy-MM-dd HH:mm"))

        Return String.Format("종목: {0}   전략: {1}" & Environment.NewLine &
                             "캔들: {2:N0}개   기간: {3} ~ {4}   신호: {5:N0}건   거래: {6:N0}건   진단: {7:N0}건   승률: {8:0.00}%   평균수익률: {9:0.00}%   최대: {10:0.00}%   최소: {11:0.00}%   PF: {12:0.00}",
                             _result.StockCode,
                             _result.StrategyDisplayName,
                             _result.CandleCount,
                             startText,
                             endText,
                             _result.SignalCount,
                             _result.TradeCount,
                             _result.DecisionLogCount,
                             _result.WinRate,
                             _result.AvgReturnPct,
                             _result.MaxReturnPct,
                             _result.MinReturnPct,
                             _result.ProfitFactor)
    End Function

    Private Function BuildMessageText() As String
        Dim text As String = ""
        text &= "RunTime: " & _result.RunTime.ToString("yyyy-MM-dd HH:mm:ss") & Environment.NewLine
        text &= "StockCode: " & _result.StockCode & Environment.NewLine
        text &= "StrategyName: " & _result.StrategyName & Environment.NewLine
        text &= "StrategyDisplayName: " & _result.StrategyDisplayName & Environment.NewLine
        text &= "Message: " & _result.Message & Environment.NewLine
        text &= Environment.NewLine
        text &= "해석 기준" & Environment.NewLine
        text &= "- 신호 0건이면 '차단 사유' 탭에서 어느 조건이 막았는지 확인합니다." & Environment.NewLine
        text &= "- 2차 VI 후보는 시가대비 11~16%, TickMA5>=10, 최근 TickCross, Tick 붕괴 방지를 동시에 봅니다." & Environment.NewLine
        text &= "- 매매 성능 탭은 Buy/StrongBuy 이후 Sell/StrongSell을 1회 거래로 묶어 계산합니다." & Environment.NewLine
        text &= "- 미청산 포지션은 마지막 캔들 종가 기준 임시 평가로 표시합니다." & Environment.NewLine
        Return text
    End Function

End Class
