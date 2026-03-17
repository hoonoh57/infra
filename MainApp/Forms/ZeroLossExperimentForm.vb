' ═══════════════════════════════════════════════════════════════
' ZeroLossExperimentForm.vb — ZeroLoss 파라미터 실험 폼
' ═══════════════════════════════════════════════════════════════
'
' OC/Amt/S/T + 재진입/스캔종료/청산시각 파라미터 그리드 서치.
' Baseline(OC=7,Amt=100,S=-3,T=10) 자동 포함, 파란 행 강조.
' 정렬 기준: Composite / TotalPnl / AvgPnl / WinRate 전환 가능.
'
' ═══════════════════════════════════════════════════════════════

Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms
Imports [Shared]

Public Class ZeroLossExperimentForm
    Inherits DockFormBase

    ' ── Baseline 파라미터 ──
    Private Const BASELINE_OC As Single = 7.0F
    Private Const BASELINE_AMT As Single = 100.0F
    Private Const BASELINE_S As Single = -3.0F
    Private Const BASELINE_T As Single = 10.0F

    ' ── Controls: Top ──
    Private _dtpFrom As DateTimePicker
    Private _dtpTo As DateTimePicker
    Private _txtOC As TextBox
    Private _txtAmt As TextBox
    Private _txtStop As TextBox
    Private _txtTarget As TextBox
    Private _txtMaxEntries As TextBox
    Private _txtScanEnd As TextBox
    Private _txtFinalExit As TextBox
    Private _txtViCooldown As TextBox
    Private _txtViSlippage As TextBox
    Private WithEvents _btnRun As Button
    Private WithEvents _btnCancel As Button
    Private WithEvents _cboSort As ComboBox
    Private _lblStatus As Label
    Private _lblCombos As Label
    Private _progressBar As ProgressBar

    ' ── Controls: Main ──
    Private WithEvents _gridResults As DataGridView
    Private _splitMain As SplitContainer
    Private _pnlBaseline As Panel
    Private _lblBaseline As Label
    Private _txtReport As TextBox

    ' ── State ──
    Private _sweepService As Services.ZeroLossExperimentSweepService
    Private _workerThread As Thread
    Private _isRunning As Boolean
    Private _results As List(Of Services.ZeroLossExperimentResult)
    Private _currentSortMode As Services.SweepSortMode = Services.SweepSortMode.TotalPnl

    Public Sub New()
        Me.Text = "ZeroLoss 파라미터 실험"
        InitializeUI()
    End Sub

    Public Overrides ReadOnly Property DefaultDockState As WeifenLuo.WinFormsUI.Docking.DockState
        Get
            Return WeifenLuo.WinFormsUI.Docking.DockState.Document
        End Get
    End Property

    ' ════════════════════════════════════════
    ' UI 초기화
    ' ════════════════════════════════════════

    Private Sub InitializeUI()
        ' ── Top panel: 파라미터 입력 (3 rows) ──
        Dim pnlTop As New Panel With {
            .Dock = DockStyle.Top,
            .Height = 104,
            .Padding = New Padding(4)
        }

        ' Row 1: From/To + Sort + Run/Cancel
        Dim y1 = 4
        Dim x = 4

        pnlTop.Controls.Add(New Label With {.Text = "From:", .Location = New Point(x, y1 + 4), .AutoSize = True})
        x += 38
        _dtpFrom = New DateTimePicker With {
            .Location = New Point(x, y1),
            .Width = 100,
            .Format = DateTimePickerFormat.Short,
            .Value = New DateTime(2025, 12, 1)
        }
        pnlTop.Controls.Add(_dtpFrom)
        x += 106

        pnlTop.Controls.Add(New Label With {.Text = "To:", .Location = New Point(x, y1 + 4), .AutoSize = True})
        x += 24
        _dtpTo = New DateTimePicker With {
            .Location = New Point(x, y1),
            .Width = 100,
            .Format = DateTimePickerFormat.Short,
            .Value = DateTime.Today
        }
        pnlTop.Controls.Add(_dtpTo)
        x += 110

        pnlTop.Controls.Add(New Label With {.Text = "정렬:", .Location = New Point(x, y1 + 4), .AutoSize = True})
        x += 36
        _cboSort = New ComboBox With {
            .Location = New Point(x, y1),
            .Width = 100,
            .DropDownStyle = ComboBoxStyle.DropDownList
        }
        _cboSort.Items.AddRange({"TotalPnl", "Composite", "AvgPnl", "WinRate"})
        _cboSort.SelectedIndex = 0  ' TotalPnl 기본
        pnlTop.Controls.Add(_cboSort)
        x += 108

        _btnRun = New Button With {
            .Text = "Run",
            .Location = New Point(x, y1),
            .Width = 60,
            .Height = 24
        }
        pnlTop.Controls.Add(_btnRun)
        x += 66

        _btnCancel = New Button With {
            .Text = "Cancel",
            .Location = New Point(x, y1),
            .Width = 60,
            .Height = 24,
            .Enabled = False
        }
        pnlTop.Controls.Add(_btnCancel)
        x += 70

        _lblStatus = New Label With {
            .Text = "",
            .Location = New Point(x, y1 + 4),
            .AutoSize = True,
            .ForeColor = Color.DarkBlue
        }
        pnlTop.Controls.Add(_lblStatus)

        ' Row 2: OC / Amt / S / T
        Dim y2 = 32
        x = 4

        pnlTop.Controls.Add(New Label With {.Text = "OC%:", .Location = New Point(x, y2 + 4), .AutoSize = True})
        x += 34
        _txtOC = New TextBox With {.Location = New Point(x, y2), .Width = 100, .Text = "3,5,7,10"}
        pnlTop.Controls.Add(_txtOC)
        x += 108

        pnlTop.Controls.Add(New Label With {.Text = "Amt(억):", .Location = New Point(x, y2 + 4), .AutoSize = True})
        x += 52
        _txtAmt = New TextBox With {.Location = New Point(x, y2), .Width = 100, .Text = "50,100,200"}
        pnlTop.Controls.Add(_txtAmt)
        x += 108

        pnlTop.Controls.Add(New Label With {.Text = "S%:", .Location = New Point(x, y2 + 4), .AutoSize = True})
        x += 26
        _txtStop = New TextBox With {.Location = New Point(x, y2), .Width = 100, .Text = "-1,-2,-3,-5"}
        pnlTop.Controls.Add(_txtStop)
        x += 108

        pnlTop.Controls.Add(New Label With {.Text = "T%:", .Location = New Point(x, y2 + 4), .AutoSize = True})
        x += 26
        _txtTarget = New TextBox With {.Location = New Point(x, y2), .Width = 100, .Text = "3,5,7,10"}
        pnlTop.Controls.Add(_txtTarget)
        x += 108

        ' Row 3: 재진입 / 스캔종료 / 청산시각 / VI파라미터 + 조합 수
        Dim y3 = 58
        x = 4

        pnlTop.Controls.Add(New Label With {.Text = "재진입:", .Location = New Point(x, y3 + 4), .AutoSize = True})
        x += 46
        _txtMaxEntries = New TextBox With {.Location = New Point(x, y3), .Width = 50, .Text = "1,2,3"}
        pnlTop.Controls.Add(_txtMaxEntries)
        x += 56

        pnlTop.Controls.Add(New Label With {.Text = "스캔종료:", .Location = New Point(x, y3 + 4), .AutoSize = True})
        x += 60
        _txtScanEnd = New TextBox With {.Location = New Point(x, y3), .Width = 100, .Text = "13:00,14:00,14:30"}
        pnlTop.Controls.Add(_txtScanEnd)
        x += 106

        pnlTop.Controls.Add(New Label With {.Text = "청산:", .Location = New Point(x, y3 + 4), .AutoSize = True})
        x += 36
        _txtFinalExit = New TextBox With {.Location = New Point(x, y3), .Width = 100, .Text = "14:30,14:50,15:10"}
        pnlTop.Controls.Add(_txtFinalExit)
        x += 106

        pnlTop.Controls.Add(New Label With {.Text = "VI쿨다운:", .Location = New Point(x, y3 + 4), .AutoSize = True, .ForeColor = Color.DarkRed})
        x += 62
        _txtViCooldown = New TextBox With {.Location = New Point(x, y3), .Width = 50, .Text = "0,3,5"}
        pnlTop.Controls.Add(_txtViCooldown)
        x += 56

        pnlTop.Controls.Add(New Label With {.Text = "VI슬리피지%:", .Location = New Point(x, y3 + 4), .AutoSize = True, .ForeColor = Color.DarkRed})
        x += 82
        _txtViSlippage = New TextBox With {.Location = New Point(x, y3), .Width = 60, .Text = "0,0.5,1"}
        pnlTop.Controls.Add(_txtViSlippage)
        x += 66

        _lblCombos = New Label With {
            .Text = "",
            .Location = New Point(x, y3 + 4),
            .AutoSize = True,
            .ForeColor = Color.Gray
        }
        pnlTop.Controls.Add(_lblCombos)
        UpdateComboCount()

        ' 파라미터 변경 시 조합 수 업데이트
        For Each txt As TextBox In {_txtOC, _txtAmt, _txtStop, _txtTarget, _txtMaxEntries, _txtScanEnd, _txtFinalExit, _txtViCooldown, _txtViSlippage}
            AddHandler txt.TextChanged, Sub(s, ev) UpdateComboCount()
        Next

        ' Progress bar
        _progressBar = New ProgressBar With {
            .Dock = DockStyle.Bottom,
            .Height = 4,
            .Style = ProgressBarStyle.Continuous,
            .Visible = False
        }
        pnlTop.Controls.Add(_progressBar)

        ' ── Main split: left=grid, right=baseline+report ──
        _splitMain = New SplitContainer With {
            .Dock = DockStyle.Fill,
            .Orientation = Orientation.Vertical,
            .SplitterDistance = 680
        }

        ' Left: results grid
        _gridResults = CreateGrid()
        _gridResults.Dock = DockStyle.Fill
        _splitMain.Panel1.Controls.Add(_gridResults)

        ' Right: baseline + report
        Dim splitRight As New SplitContainer With {
            .Dock = DockStyle.Fill,
            .Orientation = Orientation.Horizontal,
            .SplitterDistance = 100
        }

        ' Baseline panel
        _pnlBaseline = New Panel With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(230, 240, 255),
            .Padding = New Padding(8)
        }
        _lblBaseline = New Label With {
            .Dock = DockStyle.Fill,
            .Text = "Baseline: OC=7% Amt=100억 S=-3% T=+10%" & vbCrLf & "결과 없음 — Run을 클릭하세요",
            .Font = New Font("Consolas", 10),
            .ForeColor = Color.DarkBlue
        }
        _pnlBaseline.Controls.Add(_lblBaseline)
        splitRight.Panel1.Controls.Add(_pnlBaseline)

        ' Report
        _txtReport = New TextBox With {
            .Dock = DockStyle.Fill,
            .Multiline = True,
            .ScrollBars = ScrollBars.Both,
            .ReadOnly = True,
            .Font = New Font("Consolas", 9),
            .WordWrap = False,
            .BackColor = Color.FromArgb(30, 30, 30),
            .ForeColor = Color.FromArgb(220, 220, 220)
        }
        splitRight.Panel2.Controls.Add(_txtReport)

        Dim lblReport As New Label With {.Text = "상세 리포트", .Dock = DockStyle.Top, .Height = 18, .Font = New Font("Segoe UI", 9, FontStyle.Bold)}
        splitRight.Panel2.Controls.Add(lblReport)

        _splitMain.Panel2.Controls.Add(splitRight)

        Me.Controls.Add(_splitMain)
        Me.Controls.Add(pnlTop)
    End Sub

    Private Shared Function CreateGrid() As DataGridView
        Return New DataGridView With {
            .ReadOnly = True,
            .AllowUserToAddRows = False,
            .AllowUserToDeleteRows = False,
            .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .MultiSelect = False,
            .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
            .RowHeadersVisible = False,
            .BackgroundColor = Color.White,
            .BorderStyle = BorderStyle.None,
            .Font = New Font("Segoe UI", 9)
        }
    End Function

    Private Sub UpdateComboCount()
        Dim oc = ParseFloats(_txtOC.Text).Length
        Dim amt = ParseFloats(_txtAmt.Text).Length
        Dim s = ParseFloats(_txtStop.Text).Length
        Dim t = ParseFloats(_txtTarget.Text).Length
        Dim me_ = ParseInts(_txtMaxEntries.Text).Length
        Dim se = ParseTimeMinutes(_txtScanEnd.Text).Length
        Dim fe = ParseTimeMinutes(_txtFinalExit.Text).Length
        Dim viCd = ParseInts(_txtViCooldown.Text).Length
        Dim viSl = ParseFloats(_txtViSlippage.Text).Length
        If me_ = 0 Then me_ = 1
        If se = 0 Then se = 1
        If fe = 0 Then fe = 1
        If viCd = 0 Then viCd = 1
        If viSl = 0 Then viSl = 1
        Dim total = oc * amt * s * t * me_ * se * fe * viCd * viSl
        _lblCombos.Text = $"{total:N0} 조합"
    End Sub

    ' ════════════════════════════════════════
    ' Run / Cancel / Sort
    ' ════════════════════════════════════════

    Private Sub _btnRun_Click(sender As Object, e As EventArgs) Handles _btnRun.Click
        If _isRunning Then Return

        ' 파라미터 파싱
        Dim ocValues = ParseFloats(_txtOC.Text)
        Dim amtValues = ParseFloats(_txtAmt.Text)
        Dim stopValues = ParseFloats(_txtStop.Text)
        Dim targetValues = ParseFloats(_txtTarget.Text)
        Dim maxEntriesValues = ParseInts(_txtMaxEntries.Text)
        Dim scanEndValues = ParseTimeMinutes(_txtScanEnd.Text)
        Dim finalExitValues = ParseTimeMinutes(_txtFinalExit.Text)
        Dim viCooldownValues = ParseInts(_txtViCooldown.Text)
        Dim viSlippageValues = ParseFloats(_txtViSlippage.Text)

        If ocValues.Length = 0 OrElse amtValues.Length = 0 OrElse stopValues.Length = 0 OrElse targetValues.Length = 0 Then
            MessageBox.Show("기본 파라미터(OC, Amt, S, T) 값을 콤마로 구분하여 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim paramSets = Services.ZeroLossExperimentSweepService.GenerateGridSearch(
            ocValues, amtValues, stopValues, targetValues,
            If(maxEntriesValues.Length > 0, maxEntriesValues, Nothing),
            If(scanEndValues.Length > 0, scanEndValues, Nothing),
            If(finalExitValues.Length > 0, finalExitValues, Nothing),
            If(viCooldownValues.Length > 0, viCooldownValues, Nothing),
            If(viSlippageValues.Length > 0, viSlippageValues, Nothing))
        Dim totalCombos = paramSets.Count

        ' 정렬 모드
        _currentSortMode = GetSelectedSortMode()

        Dim confirm = MessageBox.Show(
            $"파라미터 {totalCombos:N0}개 조합을 실험합니다." & vbCrLf &
            $"기간: {_dtpFrom.Value:yyyy-MM-dd} ~ {_dtpTo.Value:yyyy-MM-dd}" & vbCrLf &
            $"정렬: {_cboSort.SelectedItem}" & vbCrLf & vbCrLf &
            "계속하시겠습니까?",
            "ZeroLoss 실험", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
        If confirm <> DialogResult.OK Then Return

        ' UI 상태 전환
        _isRunning = True
        _btnRun.Enabled = False
        _btnCancel.Enabled = True
        _progressBar.Visible = True
        _progressBar.Value = 0
        _progressBar.Maximum = totalCombos
        _lblStatus.Text = "캔들 로딩 중..."
        _lblStatus.ForeColor = Color.DarkOrange

        Dim fromDate = _dtpFrom.Value
        Dim toDate = _dtpTo.Value
        Dim sortMode = _currentSortMode

        _sweepService = New Services.ZeroLossExperimentSweepService()
        AddHandler _sweepService.Progress, AddressOf OnSweepProgress

        _workerThread = New Thread(
            Sub()
                Try
                    Dim results = _sweepService.RunSweep(fromDate, toDate, paramSets, sortMode)
                    UpdateUI(Sub() OnSweepCompleted(results))
                Catch ex As Exception
                    UpdateUI(Sub()
                                 _lblStatus.Text = $"오류: {ex.Message}"
                                 _lblStatus.ForeColor = Color.Red
                                 FinishRun()
                             End Sub)
                End Try
            End Sub)
        _workerThread.IsBackground = True
        _workerThread.Start()
    End Sub

    Private Sub _btnCancel_Click(sender As Object, e As EventArgs) Handles _btnCancel.Click
        If _sweepService IsNot Nothing Then
            _sweepService.CancelRequested = True
        End If
        _lblStatus.Text = "취소 중..."
    End Sub

    Private Sub _cboSort_SelectedIndexChanged(sender As Object, e As EventArgs) Handles _cboSort.SelectedIndexChanged
        ' 결과가 있으면 재정렬
        If _results Is Nothing OrElse _results.Count = 0 OrElse _isRunning Then Return

        _currentSortMode = GetSelectedSortMode()
        Services.ZeroLossExperimentSweepService.SortResults(_results, _currentSortMode)
        PopulateGrid(_results)
        UpdateBaselinePanel(_results)
    End Sub

    Private Function GetSelectedSortMode() As Services.SweepSortMode
        Select Case _cboSort.SelectedIndex
            Case 0 : Return Services.SweepSortMode.TotalPnl
            Case 1 : Return Services.SweepSortMode.Composite
            Case 2 : Return Services.SweepSortMode.AvgPnl
            Case 3 : Return Services.SweepSortMode.WinRate
            Case Else : Return Services.SweepSortMode.TotalPnl
        End Select
    End Function

    Private Sub OnSweepProgress(sender As Object, e As Services.SweepProgressEventArgs)
        UpdateUI(Sub()
                     _progressBar.Value = Math.Min(e.Current, _progressBar.Maximum)
                     _lblStatus.Text = $"[{e.Current}/{e.Total}] {e.CurrentVersion}"
                 End Sub)
    End Sub

    Private Sub OnSweepCompleted(results As List(Of Services.ZeroLossExperimentResult))
        _results = results

        If results Is Nothing OrElse results.Count = 0 Then
            _lblStatus.Text = "결과 없음 (취소 또는 데이터 없음)"
            _lblStatus.ForeColor = Color.Gray
            FinishRun()
            Return
        End If

        PopulateGrid(results)
        UpdateBaselinePanel(results)

        _lblStatus.Text = $"완료: {results.Count}개 조합  정렬: {_cboSort.SelectedItem}"
        _lblStatus.ForeColor = Color.DarkGreen
        FinishRun()
    End Sub

    Private Sub FinishRun()
        _isRunning = False
        _btnRun.Enabled = True
        _btnCancel.Enabled = False
        _progressBar.Visible = False
    End Sub

    ' ════════════════════════════════════════
    ' 그리드 표시
    ' ════════════════════════════════════════

    Private Sub PopulateGrid(results As List(Of Services.ZeroLossExperimentResult))
        Dim dt As New DataTable()
        dt.Columns.Add("Rank", GetType(Integer))
        dt.Columns.Add("Version", GetType(String))
        dt.Columns.Add("OC%", GetType(Single))
        dt.Columns.Add("Amt", GetType(Single))
        dt.Columns.Add("S%", GetType(Single))
        dt.Columns.Add("T%", GetType(Single))
        dt.Columns.Add("E", GetType(Integer))
        dt.Columns.Add("Trades", GetType(Integer))
        dt.Columns.Add("Win%", GetType(String))
        dt.Columns.Add("AvgPnl%", GetType(String))
        dt.Columns.Add("TotalPnl%", GetType(String))
        dt.Columns.Add("Target%", GetType(String))
        dt.Columns.Add("Stop%", GetType(String))
        dt.Columns.Add("Time%", GetType(String))
        dt.Columns.Add("Composite", GetType(String))

        ' Baseline 참조값 찾기
        Dim baselineResult = results.FirstOrDefault(Function(r) IsBaseline(r.Params))
        Dim baselineSortVal As Single = 0
        If baselineResult IsNot Nothing Then
            baselineSortVal = GetSortValue(baselineResult, _currentSortMode)
        End If

        For i = 0 To results.Count - 1
            Dim r = results(i)
            Dim sortVal = GetSortValue(r, _currentSortMode)
            Dim delta = sortVal - baselineSortVal
            Dim compositeText = $"{r.CompositeScore:F4}"
            If baselineResult IsNot Nothing AndAlso Not IsBaseline(r.Params) Then
                compositeText &= $" ({delta:+0.00;-0.00})"
            End If

            dt.Rows.Add(
                i + 1,
                r.Params.VersionName,
                r.Params.OcThreshold,
                r.Params.AmtThresholdEok,
                r.Params.StopLossPct,
                r.Params.TargetProfitPct,
                r.Params.MaxEntries,
                r.TotalTrades,
                $"{r.WinRate:F1}",
                $"{r.AvgPnl:+0.00;-0.00}",
                $"{r.TotalPnl:+0.0;-0.0}",
                $"{r.TargetRate:F1}",
                $"{r.StopRate:F1}",
                $"{r.TimeRate:F1}",
                compositeText)
        Next

        _gridResults.DataSource = dt
        HighlightRows(results)
    End Sub

    Private Shared Function GetSortValue(r As Services.ZeroLossExperimentResult, mode As Services.SweepSortMode) As Single
        Select Case mode
            Case Services.SweepSortMode.TotalPnl : Return r.TotalPnl
            Case Services.SweepSortMode.AvgPnl : Return r.AvgPnl
            Case Services.SweepSortMode.WinRate : Return r.WinRate
            Case Else : Return r.CompositeScore
        End Select
    End Function

    Private Sub HighlightRows(results As List(Of Services.ZeroLossExperimentResult))
        If _gridResults.Rows.Count = 0 Then Return

        Dim bestIdx = 0
        Dim baselineIdx = -1
        For i = 0 To results.Count - 1
            If IsBaseline(results(i).Params) Then
                baselineIdx = i
                Exit For
            End If
        Next

        For i = 0 To _gridResults.Rows.Count - 1
            Dim row = _gridResults.Rows(i)
            If i = bestIdx Then
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220)
                row.DefaultCellStyle.Font = New Font("Segoe UI", 9, FontStyle.Bold)
            ElseIf i = baselineIdx Then
                row.DefaultCellStyle.BackColor = Color.FromArgb(220, 230, 255)
                row.DefaultCellStyle.Font = New Font("Segoe UI", 9, FontStyle.Bold)
            Else
                row.DefaultCellStyle.BackColor = Color.White
                row.DefaultCellStyle.Font = New Font("Segoe UI", 9, FontStyle.Regular)
            End If
        Next
    End Sub

    Private Shared Function IsBaseline(p As Services.ZeroLossExperimentParams) As Boolean
        Return Math.Abs(p.OcThreshold - BASELINE_OC) < 0.01F AndAlso
               Math.Abs(p.AmtThresholdEok - BASELINE_AMT) < 0.01F AndAlso
               Math.Abs(p.StopLossPct - BASELINE_S) < 0.01F AndAlso
               Math.Abs(p.TargetProfitPct - BASELINE_T) < 0.01F AndAlso
               p.MaxEntries = 1 AndAlso
               p.ScanEndMinute = 870 AndAlso
               p.FinalExitMinute = 890 AndAlso
               p.ViCooldownBars = 0 AndAlso
               Math.Abs(p.ViSlippagePct) < 0.01F
    End Function

    ' ════════════════════════════════════════
    ' Baseline 패널 업데이트
    ' ════════════════════════════════════════

    Private Sub UpdateBaselinePanel(results As List(Of Services.ZeroLossExperimentResult))
        Dim baseline = results.FirstOrDefault(Function(r) IsBaseline(r.Params))
        Dim best = results.FirstOrDefault()
        Dim sortName = If(_cboSort.SelectedItem IsNot Nothing, _cboSort.SelectedItem.ToString(), "TotalPnl")

        Dim sb As New StringBuilder()
        sb.AppendLine("◆ Baseline (현재 전략)")
        If baseline IsNot Nothing Then
            sb.AppendLine($"  {baseline.Params.DisplayText}")
            sb.AppendLine($"  {baseline.TotalTrades}건  승률={baseline.WinRate:F1}%  AvgPnl={baseline.AvgPnl:+0.00;-0.00}%")
            sb.AppendLine($"  TotalPnl={baseline.TotalPnl:+0.0;-0.0}%  Composite={baseline.CompositeScore:F4}")
        Else
            sb.AppendLine("  (Baseline이 조합에 포함되지 않음)")
        End If

        sb.AppendLine()
        sb.AppendLine($"★ Best ({sortName})")
        If best IsNot Nothing Then
            sb.AppendLine($"  {best.Params.DisplayText}")
            sb.AppendLine($"  {best.TotalTrades}건  승률={best.WinRate:F1}%  AvgPnl={best.AvgPnl:+0.00;-0.00}%")
            sb.AppendLine($"  TotalPnl={best.TotalPnl:+0.0;-0.0}%  Composite={best.CompositeScore:F4}")
            If baseline IsNot Nothing Then
                Dim dTotal = best.TotalPnl - baseline.TotalPnl
                Dim dComp = best.CompositeScore - baseline.CompositeScore
                sb.AppendLine($"  Delta: TotalPnl {dTotal:+0.0;-0.0}%  Composite {dComp:+0.0000;-0.0000}")
            End If
        End If

        _lblBaseline.Text = sb.ToString()
    End Sub

    ' ════════════════════════════════════════
    ' 그리드 선택 → 상세 리포트
    ' ════════════════════════════════════════

    Private Sub _gridResults_SelectionChanged(sender As Object, e As EventArgs) Handles _gridResults.SelectionChanged
        If _results Is Nothing OrElse _gridResults.SelectedRows.Count = 0 Then Return

        Dim rowIdx = _gridResults.SelectedRows(0).Index
        If rowIdx < 0 OrElse rowIdx >= _results.Count Then Return

        Dim r = _results(rowIdx)
        _txtReport.Text = BuildDetailReport(r)
    End Sub

    Private Shared Function BuildDetailReport(r As Services.ZeroLossExperimentResult) As String
        Dim sb As New StringBuilder()
        sb.AppendLine("═══════════════════════════════════════════")
        sb.AppendLine($"  {r.Params.VersionName}")
        sb.AppendLine($"  {r.Params.DisplayText}")
        sb.AppendLine("═══════════════════════════════════════════")
        sb.AppendLine()
        sb.AppendLine($"  Total Trades:   {r.TotalTrades}")
        sb.AppendLine($"  Win / Loss:     {r.Wins} / {r.Losses}  ({r.WinRate:F1}% win rate)")
        sb.AppendLine($"  Avg PnL:        {r.AvgPnl:+0.00;-0.00}%")
        sb.AppendLine($"  Total PnL:      {r.TotalPnl:+0.0;-0.0}%")
        sb.AppendLine($"  Best Trade:     {r.BestTrade:+0.00;-0.00}%")
        sb.AppendLine($"  Worst Trade:    {r.WorstTrade:+0.00;-0.00}%")
        sb.AppendLine()
        sb.AppendLine("  Exit Reason Distribution:")
        sb.AppendLine($"    Target:   {r.TargetExits,4}건  ({r.TargetRate:F1}%)")
        sb.AppendLine($"    StopLoss: {r.StopExits,4}건  ({r.StopRate:F1}%)")
        sb.AppendLine($"    Time/EOD: {r.TimeExits + r.EodExits,4}건  ({r.TimeRate:F1}%)")
        sb.AppendLine()
        sb.AppendLine($"  Composite Score: {r.CompositeScore:F4}")
        sb.AppendLine($"    = AvgPnl({r.AvgPnl:+0.00;-0.00}) x WinRate({r.WinRate:F1})/100")
        sb.AppendLine()
        sb.AppendLine("  VI (Volatility Interruption):")
        sb.AppendLine($"    VI 쿨다운:     {r.Params.ViCooldownBars}분")
        sb.AppendLine($"    VI 슬리피지:   {r.Params.ViSlippagePct}%")
        sb.AppendLine($"    VI 스킵 진입:  {r.ViSkippedEntries}건 (VI로 진입 못한 횟수)")

        Return sb.ToString()
    End Function

    ' ════════════════════════════════════════
    ' 유틸
    ' ════════════════════════════════════════

    Private Shared Function ParseFloats(text As String) As Single()
        If String.IsNullOrWhiteSpace(text) Then Return Array.Empty(Of Single)()
        Dim result As New List(Of Single)()
        For Each part In text.Split(","c)
            Dim v As Single
            If Single.TryParse(part.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, v) Then
                result.Add(v)
            End If
        Next
        Return result.ToArray()
    End Function

    Private Shared Function ParseInts(text As String) As Integer()
        If String.IsNullOrWhiteSpace(text) Then Return Array.Empty(Of Integer)()
        Dim result As New List(Of Integer)()
        For Each part In text.Split(","c)
            Dim v As Integer
            If Integer.TryParse(part.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, v) Then
                result.Add(v)
            End If
        Next
        Return result.ToArray()
    End Function

    ''' <summary>"14:30" → 870분 형태로 파싱</summary>
    Private Shared Function ParseTimeMinutes(text As String) As Integer()
        If String.IsNullOrWhiteSpace(text) Then Return Array.Empty(Of Integer)()
        Dim result As New List(Of Integer)()
        For Each part In text.Split(","c)
            Dim trimmed = part.Trim()
            Dim colonIdx = trimmed.IndexOf(":"c)
            If colonIdx > 0 Then
                Dim h As Integer, m As Integer
                If Integer.TryParse(trimmed.Substring(0, colonIdx), h) AndAlso
                   Integer.TryParse(trimmed.Substring(colonIdx + 1), m) Then
                    result.Add(h * 60 + m)
                End If
            Else
                ' 숫자만 입력 시 분으로 해석
                Dim v As Integer
                If Integer.TryParse(trimmed, v) Then result.Add(v)
            End If
        Next
        Return result.ToArray()
    End Function

    Private Sub UpdateUI(action As Action)
        If Me.IsDisposed Then Return
        If Me.InvokeRequired Then
            Try
                Me.BeginInvoke(action)
            Catch
            End Try
        Else
            action()
        End If
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            If _sweepService IsNot Nothing Then
                _sweepService.CancelRequested = True
            End If
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
