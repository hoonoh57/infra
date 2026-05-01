Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Namespace SimTrade

    ''' <summary>
    ''' 모의매매 수익로직 검증용 진단 UI.
    ''' 1차 목표:
    ''' - TickSum=6 고정 의심 상태를 화면에서 즉시 확인
    ''' - TopN 순위/점수와 지표값을 한 화면에서 검증
    ''' - 향후 실제 수익률/최대상승률/최대역행률 컬럼을 연결할 기준 탭 제공
    ''' </summary>
    Public Class SimTradeDiagnosticsUI

        Private _tabTickDiag As TabPage
        Private _tabRankVerify As TabPage
        Private _dgvTickDiag As DataGridView
        Private _dgvRankVerify As DataGridView
        Private _lblTickDiagSummary As Label
        Private _lblRankSummary As Label

        Public Sub Build(tabControl As TabControl)
            If tabControl Is Nothing Then Return

            _tabTickDiag = New TabPage("TickSum 진단")
            _tabTickDiag.BackColor = Color.FromArgb(30, 30, 30)
            _lblTickDiagSummary = CreateSummaryLabel()
            _dgvTickDiag = CreateGrid()
            BuildTickDiagColumns(_dgvTickDiag)
            _tabTickDiag.Controls.Add(_dgvTickDiag)
            _tabTickDiag.Controls.Add(_lblTickDiagSummary)

            _tabRankVerify = New TabPage("순위→수익 검증")
            _tabRankVerify.BackColor = Color.FromArgb(30, 30, 30)
            _lblRankSummary = CreateSummaryLabel()
            _dgvRankVerify = CreateGrid()
            BuildRankVerifyColumns(_dgvRankVerify)
            _tabRankVerify.Controls.Add(_dgvRankVerify)
            _tabRankVerify.Controls.Add(_lblRankSummary)

            tabControl.TabPages.Add(_tabTickDiag)
            tabControl.TabPages.Add(_tabRankVerify)
        End Sub

        Public Sub Refresh(snapshots As List(Of StockStateSnapshot), topResult As Top10Result)
            RefreshTickDiagnostics(snapshots)
            RefreshRankVerification(snapshots, topResult)
        End Sub

        Private Sub RefreshTickDiagnostics(snapshots As List(Of StockStateSnapshot))
            If _dgvTickDiag Is Nothing Then Return
            If snapshots Is Nothing Then Return

            Dim rows As List(Of StockStateSnapshot) = snapshots.
                OrderBy(Function(s As StockStateSnapshot) If(s.TopNRank > 0, 0, 1)).
                ThenBy(Function(s As StockStateSnapshot) If(s.TopNRank > 0, s.TopNRank, Integer.MaxValue)).
                ThenBy(Function(s As StockStateSnapshot) s.Code).
                ToList()

            Dim validTicks As List(Of Double) = rows.
                Where(Function(s As StockStateSnapshot) Not Double.IsNaN(s.TickSum_Normalized)).
                Select(Function(s As StockStateSnapshot) s.TickSum_Normalized).
                ToList()

            Dim exactSixCount As Integer = validTicks.Count(Function(v As Double) Math.Abs(v - 6.0R) < 0.0001R)
            Dim fixedSixSuspicious As Boolean = validTicks.Count >= 3 AndAlso exactSixCount >= Math.Max(3, CInt(Math.Ceiling(validTicks.Count * 0.5R)))

            _dgvTickDiag.SuspendLayout()
            _dgvTickDiag.Rows.Clear()

            For Each s As StockStateSnapshot In rows
                Dim simTickText As String = If(Double.IsNaN(s.TickSum_Normalized), "-", s.TickSum_Normalized.ToString("F1"))
                Dim chartTickText As String = "N/A"
                Dim boardTickText As String = simTickText
                Dim rawTickText As String = "N/A"
                Dim mappedTickText As String = s.TickBarCount.ToString()
                Dim diag As String = DiagnoseTickSnapshot(s, fixedSixSuspicious)

                Dim rowIndex As Integer = _dgvTickDiag.Rows.Add(
                    DateTime.Now.ToString("HH:mm:ss"),
                    s.Code,
                    s.Name,
                    If(s.TopNRank > 0, s.TopNRank.ToString(), "-"),
                    s.CurrentPrice.ToString("N0"),
                    s.ChangeRate.ToString("F2") & "%",
                    rawTickText,
                    mappedTickText,
                    chartTickText,
                    simTickText,
                    boardTickText,
                    If(Double.IsNaN(s.TickSum_Normalized), "-", s.TickSum_Normalized.ToString("F1")),
                    If(s.TopTickScore > 0, s.TopTickScore.ToString("F1"), "-"),
                    If(s.TopNScore > 0, s.TopNScore.ToString("F1"), "-"),
                    diag)

                ApplyDiagRowStyle(_dgvTickDiag.Rows(rowIndex), diag, s)
            Next

            _dgvTickDiag.ResumeLayout()

            If _lblTickDiagSummary IsNot Nothing Then
                _lblTickDiagSummary.Text = "TickSum 진단: 종목 " & rows.Count.ToString() &
                    " / 유효Tick " & validTicks.Count.ToString() &
                    " / TickSum=6 " & exactSixCount.ToString() &
                    If(fixedSixSuspicious, "  ⚠ 고정6 의심", "")
                _lblTickDiagSummary.ForeColor = If(fixedSixSuspicious, Color.OrangeRed, Color.Cyan)
            End If
        End Sub

        Private Sub RefreshRankVerification(snapshots As List(Of StockStateSnapshot), topResult As Top10Result)
            If _dgvRankVerify Is Nothing Then Return
            If snapshots Is Nothing Then Return

            Dim snapByCode As New Dictionary(Of String, StockStateSnapshot)(StringComparer.OrdinalIgnoreCase)
            For Each s As StockStateSnapshot In snapshots
                If Not snapByCode.ContainsKey(s.Code) Then snapByCode.Add(s.Code, s)
            Next

            _dgvRankVerify.SuspendLayout()
            _dgvRankVerify.Rows.Clear()

            If topResult IsNot Nothing AndAlso topResult.TopStocks IsNot Nothing Then
                For Each score As Top10Score In topResult.TopStocks
                    Dim snap As StockStateSnapshot = Nothing
                    snapByCode.TryGetValue(score.Code, snap)

                    Dim currentPrice As String = If(snap IsNot Nothing, snap.CurrentPrice.ToString("N0"), "-")
                    Dim chg As String = If(snap IsNot Nothing, snap.ChangeRate.ToString("F2") & "%", "-")
                    Dim tickSum As String = If(snap IsNot Nothing AndAlso Not Double.IsNaN(snap.TickSum_Normalized), snap.TickSum_Normalized.ToString("F1"), "-")
                    Dim tickBars As String = If(snap IsNot Nothing, snap.TickBarCount.ToString(), "-")

                    _dgvRankVerify.Rows.Add(
                        DateTime.Now.ToString("HH:mm:ss"),
                        score.Rank.ToString(),
                        score.Code,
                        score.Name,
                        currentPrice,
                        chg,
                        tickSum,
                        tickBars,
                        score.TotalScore.ToString("F1"),
                        score.ScoreTickSum.ToString("F1"),
                        score.ScoreTradeAmount.ToString("F1"),
                        score.ScoreST.ToString("F1"),
                        score.ScoreJMA.ToString("F1"),
                        score.ScoreRSI.ToString("F1"),
                        score.ScoreChangeRate.ToString("F1"),
                        "대기",
                        "향후5분/10분/20분 수익률 연결 예정")
                Next
            End If

            _dgvRankVerify.ResumeLayout()

            If _lblRankSummary IsNot Nothing Then
                Dim topCount As Integer = If(topResult IsNot Nothing AndAlso topResult.TopStocks IsNot Nothing, topResult.TopStocks.Count, 0)
                Dim evalCount As Integer = If(topResult IsNot Nothing, topResult.TotalEvaluated, 0)
                _lblRankSummary.Text = "순위→수익 검증: Top " & topCount.ToString() & " / 평가 " & evalCount.ToString() & " / 실제 수익률 컬럼은 다음 단계에서 체결·캔들 결과와 연결"
                _lblRankSummary.ForeColor = Color.Cyan
            End If
        End Sub

        Private Shared Function DiagnoseTickSnapshot(s As StockStateSnapshot, fixedSixSuspicious As Boolean) As String
            If s Is Nothing Then Return "NO_STATE"
            If Double.IsNaN(s.TickSum_Normalized) Then Return "TickSum 없음"
            If s.TickBarCount <= 0 AndAlso s.TickSum_Normalized > 0 Then Return "TickBar=0인데 TickSum 존재"
            If Math.Abs(s.TickSum_Normalized - 6.0R) < 0.0001R AndAlso fixedSixSuspicious Then Return "고정6 의심"
            If Math.Abs(s.TickSum_Normalized - 6.0R) < 0.0001R Then Return "TickSum=6 관찰"
            Return "OK"
        End Function

        Private Shared Sub ApplyDiagRowStyle(row As DataGridViewRow, diag As String, s As StockStateSnapshot)
            If row Is Nothing Then Return

            row.DefaultCellStyle.BackColor = Color.FromArgb(35, 35, 35)
            row.DefaultCellStyle.ForeColor = Color.White

            If diag.Contains("고정6") OrElse diag.Contains("의심") Then
                row.DefaultCellStyle.BackColor = Color.FromArgb(80, 45, 35)
            ElseIf diag.Contains("없음") OrElse diag.Contains("TickBar=0") Then
                row.DefaultCellStyle.BackColor = Color.FromArgb(70, 60, 35)
            ElseIf s IsNot Nothing AndAlso s.TopNRank > 0 Then
                row.DefaultCellStyle.BackColor = Color.FromArgb(42, 48, 58)
            End If
        End Sub

        Private Shared Function CreateSummaryLabel() As Label
            Dim lbl As New Label()
            lbl.Dock = DockStyle.Top
            lbl.Height = 28
            lbl.TextAlign = ContentAlignment.MiddleLeft
            lbl.BackColor = Color.FromArgb(45, 45, 48)
            lbl.ForeColor = Color.Cyan
            lbl.Font = New Font("맑은 고딕", 9.0F, FontStyle.Bold)
            lbl.Padding = New Padding(8, 0, 0, 0)
            Return lbl
        End Function

        Private Shared Function CreateGrid() As DataGridView
            Dim dgv As New DataGridView()
            dgv.Dock = DockStyle.Fill
            dgv.ReadOnly = True
            dgv.AllowUserToAddRows = False
            dgv.AllowUserToDeleteRows = False
            dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            dgv.BackgroundColor = Color.FromArgb(30, 30, 30)
            dgv.ForeColor = Color.White
            dgv.GridColor = Color.FromArgb(60, 60, 60)
            dgv.DefaultCellStyle.BackColor = Color.FromArgb(35, 35, 35)
            dgv.DefaultCellStyle.ForeColor = Color.White
            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 80, 120)
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(50, 50, 55)
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
            dgv.EnableHeadersVisualStyles = False
            dgv.RowHeadersVisible = False
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            Return dgv
        End Function

        Private Shared Sub BuildTickDiagColumns(dgv As DataGridView)
            AddCol(dgv, "시간", 70)
            AddCol(dgv, "코드", 75)
            AddCol(dgv, "종목명", 120)
            AddCol(dgv, "TopN", 50)
            AddCol(dgv, "현재가", 75)
            AddCol(dgv, "등락률", 65)
            AddCol(dgv, "RawTick", 70)
            AddCol(dgv, "MappedTick", 80)
            AddCol(dgv, "ChartTick", 75)
            AddCol(dgv, "SimTick", 70)
            AddCol(dgv, "BoardTick", 75)
            AddCol(dgv, "TickSum", 70)
            AddCol(dgv, "TopTick", 70)
            AddCol(dgv, "TopScore", 75)
            AddCol(dgv, "진단", 180, DataGridViewAutoSizeColumnMode.Fill)
        End Sub

        Private Shared Sub BuildRankVerifyColumns(dgv As DataGridView)
            AddCol(dgv, "시간", 70)
            AddCol(dgv, "Rank", 55)
            AddCol(dgv, "코드", 75)
            AddCol(dgv, "종목명", 120)
            AddCol(dgv, "현재가", 75)
            AddCol(dgv, "등락률", 65)
            AddCol(dgv, "TickSum", 70)
            AddCol(dgv, "TickBars", 70)
            AddCol(dgv, "Total", 70)
            AddCol(dgv, "Tick", 60)
            AddCol(dgv, "Amount", 70)
            AddCol(dgv, "ST", 55)
            AddCol(dgv, "JMA", 55)
            AddCol(dgv, "RSI", 55)
            AddCol(dgv, "Chg", 55)
            AddCol(dgv, "검증상태", 80)
            AddCol(dgv, "수익검증", 240, DataGridViewAutoSizeColumnMode.Fill)
        End Sub

        Private Shared Sub AddCol(dgv As DataGridView,
                                  headerText As String,
                                  width As Integer,
                                  Optional autoSizeMode As DataGridViewAutoSizeColumnMode = DataGridViewAutoSizeColumnMode.None)
            Dim colIndex As Integer = dgv.Columns.Add(headerText, headerText)
            Dim col As DataGridViewColumn = dgv.Columns(colIndex)
            col.Width = width
            col.MinimumWidth = Math.Min(width, 40)
            col.AutoSizeMode = autoSizeMode
        End Sub
    End Class
End Namespace
