Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms
Imports MainApp.SimTrade

Public Class ConditionBatchValidatorForm
    Inherits Form

    Private ReadOnly _settings As SimTradeSettings
    Private ReadOnly _manager As StateManager

    Private ReadOnly _dtpFrom As New DateTimePicker()
    Private ReadOnly _dtpTo As New DateTimePicker()
    Private ReadOnly _numTarget As New NumericUpDown()
    Private ReadOnly _numStop As New NumericUpDown()
    Private ReadOnly _numTopN As New NumericUpDown()

    Private ReadOnly _btnLoadUniverse As New Button()
    Private ReadOnly _btnRunBatch As New Button()
    Private ReadOnly _btnClose As New Button()

    Private ReadOnly _lblStatus As New Label()

    Private ReadOnly _dgvUniverse As New DataGridView()
    Private ReadOnly _dgvSummary As New DataGridView()
    Private ReadOnly _dgvDetail As New DataGridView()

    Public Sub New(settings As SimTradeSettings, manager As StateManager)
        _settings = settings
        _manager = manager

        BuildUi()
        LoadUniverse()
    End Sub

    Private Sub BuildUi()
        Me.Text = "Condition Batch Validator  TopN 검증"
        Me.Size = New Size(1500, 900)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.BackColor = Color.FromArgb(24, 26, 34)
        Me.ForeColor = Color.White

        Dim pnlTop As New Panel()
        pnlTop.Dock = DockStyle.Top
        pnlTop.Height = 48
        pnlTop.BackColor = Color.FromArgb(38, 40, 50)

        Dim lblFrom As New Label()
        lblFrom.Text = "From"
        lblFrom.Left = 10
        lblFrom.Top = 15
        lblFrom.Width = 40
        lblFrom.ForeColor = Color.White
        pnlTop.Controls.Add(lblFrom)

        _dtpFrom.Format = DateTimePickerFormat.Custom
        _dtpFrom.CustomFormat = "yyyy-MM-dd"
        _dtpFrom.Left = 55
        _dtpFrom.Top = 10
        _dtpFrom.Width = 110
        _dtpFrom.Value = DateTime.Today.AddDays(-7)
        pnlTop.Controls.Add(_dtpFrom)

        Dim lblTo As New Label()
        lblTo.Text = "To"
        lblTo.Left = 175
        lblTo.Top = 15
        lblTo.Width = 25
        lblTo.ForeColor = Color.White
        pnlTop.Controls.Add(lblTo)

        _dtpTo.Format = DateTimePickerFormat.Custom
        _dtpTo.CustomFormat = "yyyy-MM-dd"
        _dtpTo.Left = 205
        _dtpTo.Top = 10
        _dtpTo.Width = 110
        _dtpTo.Value = DateTime.Today
        pnlTop.Controls.Add(_dtpTo)

        Dim lblTarget As New Label()
        lblTarget.Text = "T%"
        lblTarget.Left = 325
        lblTarget.Top = 15
        lblTarget.Width = 25
        lblTarget.ForeColor = Color.LightGreen
        pnlTop.Controls.Add(lblTarget)

        _numTarget.DecimalPlaces = 1
        _numTarget.Minimum = 0.5D
        _numTarget.Maximum = 20D
        _numTarget.Increment = 0.5D
        _numTarget.Value = 5D
        _numTarget.Left = 355
        _numTarget.Top = 10
        _numTarget.Width = 60
        pnlTop.Controls.Add(_numTarget)

        Dim lblStop As New Label()
        lblStop.Text = "S%"
        lblStop.Left = 425
        lblStop.Top = 15
        lblStop.Width = 25
        lblStop.ForeColor = Color.Orange
        pnlTop.Controls.Add(lblStop)

        _numStop.DecimalPlaces = 1
        _numStop.Minimum = 0.5D
        _numStop.Maximum = 10D
        _numStop.Increment = 0.5D
        _numStop.Value = 1.5D
        _numStop.Left = 455
        _numStop.Top = 10
        _numStop.Width = 60
        pnlTop.Controls.Add(_numStop)

        Dim lblTopN As New Label()
        lblTopN.Text = "TopN"
        lblTopN.Left = 530
        lblTopN.Top = 15
        lblTopN.Width = 45
        lblTopN.ForeColor = Color.Cyan
        pnlTop.Controls.Add(lblTopN)

        _numTopN.Minimum = 1D
        _numTopN.Maximum = 50D
        _numTopN.Value = 10D
        _numTopN.Left = 580
        _numTopN.Top = 10
        _numTopN.Width = 55
        pnlTop.Controls.Add(_numTopN)

        _btnLoadUniverse.Text = "종목새로고침"
        _btnLoadUniverse.Left = 650
        _btnLoadUniverse.Top = 8
        _btnLoadUniverse.Width = 110
        _btnLoadUniverse.Height = 30
        _btnLoadUniverse.FlatStyle = FlatStyle.Flat
        _btnLoadUniverse.BackColor = Color.FromArgb(60, 65, 80)
        _btnLoadUniverse.ForeColor = Color.White
        AddHandler _btnLoadUniverse.Click, AddressOf OnLoadUniverseClick
        pnlTop.Controls.Add(_btnLoadUniverse)

        _btnRunBatch.Text = "Batch 검증"
        _btnRunBatch.Left = 770
        _btnRunBatch.Top = 8
        _btnRunBatch.Width = 100
        _btnRunBatch.Height = 30
        _btnRunBatch.FlatStyle = FlatStyle.Flat
        _btnRunBatch.BackColor = Color.FromArgb(60, 90, 70)
        _btnRunBatch.ForeColor = Color.White
        AddHandler _btnRunBatch.Click, AddressOf OnRunBatchClick
        pnlTop.Controls.Add(_btnRunBatch)

        _btnClose.Text = "닫기"
        _btnClose.Left = 880
        _btnClose.Top = 8
        _btnClose.Width = 70
        _btnClose.Height = 30
        _btnClose.FlatStyle = FlatStyle.Flat
        _btnClose.BackColor = Color.FromArgb(70, 60, 60)
        _btnClose.ForeColor = Color.White
        AddHandler _btnClose.Click, AddressOf OnCloseClick
        pnlTop.Controls.Add(_btnClose)

        _lblStatus.Text = "대기"
        _lblStatus.Left = 970
        _lblStatus.Top = 14
        _lblStatus.Width = 480
        _lblStatus.ForeColor = Color.LightGray
        pnlTop.Controls.Add(_lblStatus)

        Dim splitOuter As New SplitContainer()
        splitOuter.Dock = DockStyle.Fill
        splitOuter.Orientation = Orientation.Vertical
        splitOuter.SplitterDistance = 320
        splitOuter.BackColor = Color.FromArgb(24, 26, 34)
        splitOuter.Panel1.Padding = New Padding(0)
        splitOuter.Panel2.Padding = New Padding(0)
        Me.Controls.Add(splitOuter)
        Me.Controls.Add(pnlTop)

        InitUniverseGrid()
        _dgvUniverse.Dock = DockStyle.Fill
        splitOuter.Panel1.Controls.Add(_dgvUniverse)

        Dim splitRight As New SplitContainer()
        splitRight.Dock = DockStyle.Fill
        splitRight.Orientation = Orientation.Horizontal
        splitRight.SplitterDistance = 310
        splitRight.BackColor = Color.FromArgb(24, 26, 34)
        splitRight.Panel1.Padding = New Padding(0)
        splitRight.Panel2.Padding = New Padding(0)
        splitOuter.Panel2.Controls.Add(splitRight)

        InitSummaryGrid()
        _dgvSummary.Dock = DockStyle.Fill
        splitRight.Panel1.Controls.Add(_dgvSummary)

        InitDetailGrid()
        _dgvDetail.Dock = DockStyle.Fill
        splitRight.Panel2.Controls.Add(_dgvDetail)
    End Sub

    Private Sub InitBaseGrid(grid As DataGridView)
        grid.BackgroundColor = Color.FromArgb(24, 26, 34)
        grid.BorderStyle = BorderStyle.None
        grid.AllowUserToAddRows = False
        grid.AllowUserToDeleteRows = False
        grid.RowHeadersVisible = False
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        grid.MultiSelect = False
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 42, 52)
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
        grid.EnableHeadersVisualStyles = False
        grid.DefaultCellStyle.BackColor = Color.FromArgb(30, 32, 40)
        grid.DefaultCellStyle.ForeColor = Color.White
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(55, 80, 110)
        grid.DefaultCellStyle.SelectionForeColor = Color.White
        grid.Font = New Font("Consolas", 8.5F)
        grid.ColumnHeadersHeight = 28
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
    End Sub

    Private Sub InitUniverseGrid()
        InitBaseGrid(_dgvUniverse)
        _dgvUniverse.Columns.Clear()

        Dim includeCol As New DataGridViewCheckBoxColumn()
        includeCol.Name = "Include"
        includeCol.HeaderText = "Run"
        includeCol.Width = 45
        _dgvUniverse.Columns.Add(includeCol)

        AddGridColumn(_dgvUniverse, "Code", "Code", 70)
        AddGridColumn(_dgvUniverse, "Name", "Name", 90)
        AddGridColumn(_dgvUniverse, "State", "State", 80)
        AddGridColumn(_dgvUniverse, "ChangeRate", "Chg%", 65)
        AddGridColumn(_dgvUniverse, "Tick", "Tick", 65)
        AddGridColumn(_dgvUniverse, "CandleCount", "Candles", 65)
    End Sub

    Private Sub InitSummaryGrid()
        InitBaseGrid(_dgvSummary)
        _dgvSummary.Columns.Clear()

        AddGridColumn(_dgvSummary, "Rank", "Rank", 45)
        AddGridColumn(_dgvSummary, "Code", "Code", 70)
        AddGridColumn(_dgvSummary, "Name", "Name", 90)
        AddGridColumn(_dgvSummary, "SignalCount", "Signals", 65)
        AddGridColumn(_dgvSummary, "FirstSignalTime", "First", 80)
        AddGridColumn(_dgvSummary, "BestMFE10M", "MFE10", 70)
        AddGridColumn(_dgvSummary, "BestMFE30M", "MFE30", 70)
        AddGridColumn(_dgvSummary, "BestMFE60M", "MFE60", 70)
        AddGridColumn(_dgvSummary, "WorstMAE10M", "WorstMAE10", 85)
        AddGridColumn(_dgvSummary, "AvgMAE10M", "AvgMAE10", 80)
        AddGridColumn(_dgvSummary, "Target10Count", "T10", 45)
        AddGridColumn(_dgvSummary, "Target30Count", "T30", 45)
        AddGridColumn(_dgvSummary, "Target60Count", "T60", 45)
        AddGridColumn(_dgvSummary, "LeaderScore", "Leader", 70)
        AddGridColumn(_dgvSummary, "BestExitReason", "BestExit", 150)
        AddGridColumn(_dgvSummary, "RiskFlags", "RiskFlags", 240)
    End Sub

    Private Sub InitDetailGrid()
        InitBaseGrid(_dgvDetail)
        _dgvDetail.Columns.Clear()

        AddGridColumn(_dgvDetail, "Code", "Code", 70)
        AddGridColumn(_dgvDetail, "Name", "Name", 90)
        AddGridColumn(_dgvDetail, "Time", "Time", 80)
        AddGridColumn(_dgvDetail, "Seq", "Seq", 45)
        AddGridColumn(_dgvDetail, "Price", "Price", 70)
        AddGridColumn(_dgvDetail, "LeaderScore", "Leader", 70)
        AddGridColumn(_dgvDetail, "Tick", "Tick", 60)
        AddGridColumn(_dgvDetail, "TickVsMA5", "T/MA5", 65)
        AddGridColumn(_dgvDetail, "TickVsMA20", "T/MA20", 70)
        AddGridColumn(_dgvDetail, "OpenPct", "Open%", 65)
        AddGridColumn(_dgvDetail, "LowPct", "Low%", 65)
        AddGridColumn(_dgvDetail, "HighGapPct", "HighGap%", 75)
        AddGridColumn(_dgvDetail, "MFE10M", "MFE10", 70)
        AddGridColumn(_dgvDetail, "MFE30M", "MFE30", 70)
        AddGridColumn(_dgvDetail, "MFE60M", "MFE60", 70)
        AddGridColumn(_dgvDetail, "MAE10M", "MAE10", 70)
        AddGridColumn(_dgvDetail, "MAE30M", "MAE30", 70)
        AddGridColumn(_dgvDetail, "MAE60M", "MAE60", 70)
        AddGridColumn(_dgvDetail, "T10", "T10", 45)
        AddGridColumn(_dgvDetail, "T30", "T30", 45)
        AddGridColumn(_dgvDetail, "T60", "T60", 45)
        AddGridColumn(_dgvDetail, "ExitReason", "Exit", 150)
        AddGridColumn(_dgvDetail, "RealizedPct", "Realized", 75)
        AddGridColumn(_dgvDetail, "HoldMin", "Hold", 60)
        AddGridColumn(_dgvDetail, "Ban", "Ban", 45)
        AddGridColumn(_dgvDetail, "RiskFlags", "RiskFlags", 240)
    End Sub

    Private Sub AddGridColumn(grid As DataGridView, name As String, headerText As String, width As Integer)
        Dim col As New DataGridViewTextBoxColumn()
        col.Name = name
        col.HeaderText = headerText
        col.Width = width
        col.ReadOnly = True
        grid.Columns.Add(col)
    End Sub

    Private Sub OnLoadUniverseClick(sender As Object, e As EventArgs)
        LoadUniverse()
    End Sub

    Private Sub LoadUniverse()
        _dgvUniverse.Rows.Clear()

        If _manager Is Nothing Then
            _lblStatus.Text = "StateManager 없음"
            _lblStatus.ForeColor = Color.OrangeRed
            Return
        End If

        Dim snapshots As List(Of StockStateSnapshot) = _manager.GetSnapshot()
        If snapshots Is Nothing Then
            _lblStatus.Text = "종목 없음"
            _lblStatus.ForeColor = Color.Orange
            Return
        End If

        For i As Integer = 0 To snapshots.Count - 1
            Dim s As StockStateSnapshot = snapshots(i)
            If s Is Nothing Then Continue For

            _dgvUniverse.Rows.Add(True,
                                  s.Code,
                                  s.Name,
                                  s.State.ToString(),
                                  s.ChangeRate.ToString("F2"),
                                  SafeDoubleText(s.TickSum_Normalized),
                                  s.CandleCount.ToString())
        Next

        _lblStatus.Text = "대상 종목 " & snapshots.Count.ToString() & "개 로드"
        _lblStatus.ForeColor = Color.LightGreen
    End Sub

    Private Sub OnRunBatchClick(sender As Object, e As EventArgs)
        RunBatchValidation()
    End Sub

    Private Sub RunBatchValidation()
        If _manager Is Nothing Then Return

        _btnRunBatch.Enabled = False
        _dgvSummary.Rows.Clear()
        _dgvDetail.Rows.Clear()

        Dim summaries As New List(Of RangeSignalQualitySummary)()

        Dim fromDate As Date = _dtpFrom.Value.Date
        Dim toDate As Date = _dtpTo.Value.Date
        Dim targetPct As Double = CDbl(_numTarget.Value)
        Dim stopPct As Double = CDbl(_numStop.Value)

        Dim tester As CircuitDesignerForm = Nothing

        Try
            tester = New CircuitDesignerForm(_settings, _manager)
            tester.ShowInTaskbar = False
            tester.StartPosition = FormStartPosition.Manual
            tester.Left = -30000
            tester.Top = -30000

            Dim total As Integer = CountIncludedRows()
            Dim done As Integer = 0

            For i As Integer = 0 To _dgvUniverse.Rows.Count - 1
                Dim row As DataGridViewRow = _dgvUniverse.Rows(i)
                If row Is Nothing OrElse row.IsNewRow Then Continue For
                If Not IsUniverseRowIncluded(row) Then Continue For

                Dim code As String = CellText(row, "Code")
                If String.IsNullOrWhiteSpace(code) Then Continue For

                done += 1
                _lblStatus.Text = "Batch 검증 중: " & done.ToString() & "/" & total.ToString() & "  " & code
                _lblStatus.ForeColor = Color.Cyan
                Application.DoEvents()

                Dim summary As RangeSignalQualitySummary = tester.RunRangeValidationForStockForBatch(code, fromDate, toDate, targetPct, stopPct)
                If summary IsNot Nothing Then
                    summaries.Add(summary)
                End If

                Dim details As List(Of RangeSignalQualityResult) = tester.GetLastRangeSignalQualityResultsSnapshot()
                AddDetailRows(details)
            Next

            RankSummaries(summaries)
            RenderSummaries(summaries)

            _lblStatus.Text = "Batch 완료: Summary " & summaries.Count.ToString() & "개, Detail " & _dgvDetail.Rows.Count.ToString() & "개"
            _lblStatus.ForeColor = Color.LightGreen

        Catch ex As Exception
            _lblStatus.Text = "Batch 오류: " & ex.Message
            _lblStatus.ForeColor = Color.OrangeRed
        Finally
            If tester IsNot Nothing Then
                tester.Dispose()
            End If
            _btnRunBatch.Enabled = True
        End Try
    End Sub

    Private Function CountIncludedRows() As Integer
        Dim count As Integer = 0

        For i As Integer = 0 To _dgvUniverse.Rows.Count - 1
            Dim row As DataGridViewRow = _dgvUniverse.Rows(i)
            If row IsNot Nothing AndAlso Not row.IsNewRow AndAlso IsUniverseRowIncluded(row) Then
                count += 1
            End If
        Next

        Return count
    End Function

    Private Function IsUniverseRowIncluded(row As DataGridViewRow) As Boolean
        If row Is Nothing Then Return False
        If Not _dgvUniverse.Columns.Contains("Include") Then Return False

        Dim value As Object = row.Cells("Include").Value
        If value Is Nothing Then Return False

        If TypeOf value Is Boolean Then
            Return CBool(value)
        End If

        Dim text As String = Convert.ToString(value).Trim()
        Return text.Equals("True", StringComparison.OrdinalIgnoreCase) OrElse text = "1" OrElse text.Equals("Y", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Sub RankSummaries(summaries As List(Of RangeSignalQualitySummary))
        If summaries Is Nothing OrElse summaries.Count = 0 Then Return

        summaries.Sort(AddressOf CompareSummaryByLeaderScoreDesc)

        For i As Integer = 0 To summaries.Count - 1
            summaries(i).Rank = i + 1
        Next
    End Sub

    Private Function CompareSummaryByLeaderScoreDesc(a As RangeSignalQualitySummary, b As RangeSignalQualitySummary) As Integer
        If a Is Nothing AndAlso b Is Nothing Then Return 0
        If a Is Nothing Then Return 1
        If b Is Nothing Then Return -1

        Dim cmp As Integer = b.LeaderScore.CompareTo(a.LeaderScore)
        If cmp <> 0 Then Return cmp

        cmp = b.BestMFE30M.CompareTo(a.BestMFE30M)
        If cmp <> 0 Then Return cmp

        Return b.BestMFE10M.CompareTo(a.BestMFE10M)
    End Function

    Private Sub RenderSummaries(summaries As List(Of RangeSignalQualitySummary))
        _dgvSummary.Rows.Clear()

        If summaries Is Nothing Then Return

        For i As Integer = 0 To summaries.Count - 1
            Dim s As RangeSignalQualitySummary = summaries(i)
            If s Is Nothing Then Continue For

            _dgvSummary.Rows.Add(s.Rank.ToString(),
                                 s.Code,
                                 s.Name,
                                 s.SignalCount.ToString(),
                                 If(s.FirstSignalTime = DateTime.MinValue, "-", s.FirstSignalTime.ToString("HH:mm")),
                                 FmtPct(s.BestMFE10M),
                                 FmtPct(s.BestMFE30M),
                                 FmtPct(s.BestMFE60M),
                                 FmtPct(s.WorstMAE10M),
                                 FmtPct(s.AvgMAE10M),
                                 s.Target10Count.ToString(),
                                 s.Target30Count.ToString(),
                                 s.Target60Count.ToString(),
                                 s.LeaderScore.ToString("F1"),
                                 s.BestExitReason,
                                 s.RiskFlags)
        Next
    End Sub

    Private Sub AddDetailRows(details As List(Of RangeSignalQualityResult))
        If details Is Nothing Then Return

        For i As Integer = 0 To details.Count - 1
            Dim r As RangeSignalQualityResult = details(i)
            If r Is Nothing Then Continue For

            _dgvDetail.Rows.Add(r.Code,
                                r.Name,
                                If(r.EntryTime = DateTime.MinValue, "-", r.EntryTime.ToString("HH:mm")),
                                r.Seq.ToString(),
                                r.EntryPrice.ToString("N0"),
                                r.LeaderScore.ToString("F1"),
                                SafeDoubleText(r.Tick),
                                SafeDoubleText(r.TickVsMA5),
                                SafeDoubleText(r.TickVsMA20),
                                FmtPct(r.OpenPct),
                                FmtPct(r.LowPct),
                                FmtPct(r.HighGapPct),
                                FmtPct(r.MFE10M),
                                FmtPct(r.MFE30M),
                                FmtPct(r.MFE60M),
                                FmtPct(r.MAE10M),
                                FmtPct(r.MAE30M),
                                FmtPct(r.MAE60M),
                                If(r.T10, "Y", "-"),
                                If(r.T30, "Y", "-"),
                                If(r.T60, "Y", "-"),
                                r.ExitReason,
                                FmtPct(r.RealizedPct),
                                r.HoldMin.ToString("F0"),
                                If(r.BanAfterExit, "Y", "-"),
                                r.RiskFlags)
        Next
    End Sub

    Private Function CellText(row As DataGridViewRow, columnName As String) As String
        If row Is Nothing Then Return ""
        If row.DataGridView Is Nothing Then Return ""
        If Not row.DataGridView.Columns.Contains(columnName) Then Return ""

        Dim value As Object = row.Cells(columnName).Value
        If value Is Nothing Then Return ""

        Return Convert.ToString(value).Trim()
    End Function

    Private Function FmtPct(value As Double) As String
        If Double.IsNaN(value) OrElse Double.IsInfinity(value) Then Return "-"
        Return value.ToString("F2") & "%"
    End Function

    Private Function SafeDoubleText(value As Double) As String
        If Double.IsNaN(value) OrElse Double.IsInfinity(value) Then Return "-"
        Return value.ToString("F2")
    End Function

    Private Sub OnCloseClick(sender As Object, e As EventArgs)
        Me.Close()
    End Sub

End Class

