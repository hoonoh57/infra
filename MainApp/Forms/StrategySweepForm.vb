' ═══════════════════════════════════════════════════════════════
' StrategySweepForm.vb — Strategy Parameter Sweep UI
' ═══════════════════════════════════════════════════════════════
' DB에 저장된 sweep 결과 조회 + 새 sweep 실행(CLI 위임)
' ═══════════════════════════════════════════════════════════════

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms
Imports Newtonsoft.Json

Public Class StrategySweepForm
    Inherits DockFormBase

    ' ── DB config ──
    Private ReadOnly _config As Services.ResearchDbMySqlConfig

    ' ── Controls ──
    Private WithEvents _dtpFrom As DateTimePicker
    Private WithEvents _dtpTo As DateTimePicker
    Private WithEvents _btnRun As Button
    Private WithEvents _btnRefresh As Button
    Private _lblStatus As Label

    Private WithEvents _gridSweeps As DataGridView
    Private WithEvents _gridVersions As DataGridView
    Private _txtReport As TextBox

    Private _splitMain As SplitContainer
    Private _splitRight As SplitContainer

    ' ── State ──
    Private _sweepProcess As Process
    Private _isRunning As Boolean

    Public Sub New()
        Me.Text = "Strategy Sweep"
        _config = LoadDbConfig()
        InitializeUI()
        If _config IsNot Nothing AndAlso _config.Enabled Then
            LoadSweepRuns()
        End If
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
        ' ── Top panel: parameters ──
        Dim pnlTop As New Panel With {
            .Dock = DockStyle.Top,
            .Height = 40,
            .Padding = New Padding(4)
        }

        Dim x = 4

        pnlTop.Controls.Add(New Label With {.Text = "From:", .Location = New Point(x, 10), .AutoSize = True})
        x += 40
        _dtpFrom = New DateTimePicker With {
            .Location = New Point(x, 6),
            .Width = 110,
            .Format = DateTimePickerFormat.Short,
            .Value = New DateTime(2026, 2, 19)
        }
        pnlTop.Controls.Add(_dtpFrom)
        x += 118

        pnlTop.Controls.Add(New Label With {.Text = "To:", .Location = New Point(x, 10), .AutoSize = True})
        x += 25
        _dtpTo = New DateTimePicker With {
            .Location = New Point(x, 6),
            .Width = 110,
            .Format = DateTimePickerFormat.Short,
            .Value = DateTime.Today
        }
        pnlTop.Controls.Add(_dtpTo)
        x += 118

        _btnRun = New Button With {
            .Text = "Run Sweep",
            .Location = New Point(x, 5),
            .Width = 90,
            .Height = 28
        }
        pnlTop.Controls.Add(_btnRun)
        x += 98

        _btnRefresh = New Button With {
            .Text = "Refresh",
            .Location = New Point(x, 5),
            .Width = 70,
            .Height = 28
        }
        pnlTop.Controls.Add(_btnRefresh)
        x += 78

        _lblStatus = New Label With {
            .Text = "",
            .Location = New Point(x, 10),
            .AutoSize = True,
            .ForeColor = Color.DarkBlue
        }
        pnlTop.Controls.Add(_lblStatus)

        ' ── Main split: left=sweeps grid, right=versions+report ──
        _splitMain = New SplitContainer With {
            .Dock = DockStyle.Fill,
            .Orientation = Orientation.Vertical,
            .SplitterDistance = 360
        }

        ' Left: sweep runs grid
        _gridSweeps = CreateGrid()
        _gridSweeps.Dock = DockStyle.Fill
        _splitMain.Panel1.Controls.Add(_gridSweeps)

        Dim lblSweeps As New Label With {.Text = "Sweep Runs", .Dock = DockStyle.Top, .Height = 20, .Font = New Font("Segoe UI", 9, FontStyle.Bold)}
        _splitMain.Panel1.Controls.Add(lblSweeps)

        ' Right: split top=versions, bottom=report
        _splitRight = New SplitContainer With {
            .Dock = DockStyle.Fill,
            .Orientation = Orientation.Horizontal,
            .SplitterDistance = 280
        }

        _gridVersions = CreateGrid()
        _gridVersions.Dock = DockStyle.Fill
        _splitRight.Panel1.Controls.Add(_gridVersions)

        Dim lblVersions As New Label With {.Text = "Top Versions", .Dock = DockStyle.Top, .Height = 20, .Font = New Font("Segoe UI", 9, FontStyle.Bold)}
        _splitRight.Panel1.Controls.Add(lblVersions)

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
        _splitRight.Panel2.Controls.Add(_txtReport)

        Dim lblReport As New Label With {.Text = "Report", .Dock = DockStyle.Top, .Height = 20, .Font = New Font("Segoe UI", 9, FontStyle.Bold)}
        _splitRight.Panel2.Controls.Add(lblReport)

        _splitMain.Panel2.Controls.Add(_splitRight)

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

    ' ════════════════════════════════════════
    ' DB 조회: Sweep Runs
    ' ════════════════════════════════════════

    Private Sub LoadSweepRuns()
        Try
            Dim sql = "SELECT sweep_id, DATE_FORMAT(from_date,'%Y-%m-%d'), DATE_FORMAT(to_date,'%Y-%m-%d'), " &
                      "total_combinations, best_version_name, best_avg_exit_return, best_composite_score, " &
                      "elapsed_seconds, DATE_FORMAT(created_at,'%Y-%m-%d %H:%i') " &
                      "FROM strategy_sweep_runs ORDER BY sweep_id DESC;"

            Dim rows = ExecuteQuery(sql)

            Dim dt As New DataTable()
            dt.Columns.Add("ID", GetType(Long))
            dt.Columns.Add("From", GetType(String))
            dt.Columns.Add("To", GetType(String))
            dt.Columns.Add("Combos", GetType(Integer))
            dt.Columns.Add("Best Version", GetType(String))
            dt.Columns.Add("AvgExit%", GetType(Decimal))
            dt.Columns.Add("Composite", GetType(Decimal))
            dt.Columns.Add("Elapsed(s)", GetType(Double))
            dt.Columns.Add("Created", GetType(String))

            For Each line In rows
                Dim cols = line.Split(ControlChars.Tab)
                If cols.Length < 9 Then Continue For
                dt.Rows.Add(
                    SafeLong(cols(0)),
                    cols(1), cols(2),
                    SafeInt(cols(3)),
                    cols(4),
                    SafeDec(cols(5)),
                    SafeDec(cols(6)),
                    SafeDbl(cols(7)),
                    cols(8))
            Next

            _gridSweeps.DataSource = dt
            _lblStatus.Text = $"{dt.Rows.Count} sweep(s) loaded"
        Catch ex As Exception
            _lblStatus.Text = $"Error: {ex.Message}"
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' DB 조회: Sweep Versions
    ' ════════════════════════════════════════

    Private Sub LoadVersions(sweepId As Long)
        Try
            Dim sql = $"SELECT rank_no, version_name, total_events, target_hit_rate, stopout_rate, " &
                      $"avg_exit_return, win_day_rate, composite_score, avg_max_return, avg_mae " &
                      $"FROM strategy_sweep_versions WHERE sweep_id = {sweepId} ORDER BY rank_no ASC;"

            Dim rows = ExecuteQuery(sql)

            Dim dt As New DataTable()
            dt.Columns.Add("Rank", GetType(Integer))
            dt.Columns.Add("Version", GetType(String))
            dt.Columns.Add("Events", GetType(Integer))
            dt.Columns.Add("HitRate%", GetType(Decimal))
            dt.Columns.Add("StopRate%", GetType(Decimal))
            dt.Columns.Add("AvgExit%", GetType(Decimal))
            dt.Columns.Add("WinDays%", GetType(Decimal))
            dt.Columns.Add("Composite", GetType(Decimal))
            dt.Columns.Add("AvgMax%", GetType(Decimal))
            dt.Columns.Add("AvgMAE%", GetType(Decimal))

            For Each line In rows
                Dim cols = line.Split(ControlChars.Tab)
                If cols.Length < 10 Then Continue For
                dt.Rows.Add(
                    SafeInt(cols(0)),
                    cols(1),
                    SafeInt(cols(2)),
                    SafeDec(cols(3)),
                    SafeDec(cols(4)),
                    SafeDec(cols(5)),
                    SafeDec(cols(6)),
                    SafeDec(cols(7)),
                    SafeDec(cols(8)),
                    SafeDec(cols(9)))
            Next

            _gridVersions.DataSource = dt
        Catch ex As Exception
            _lblStatus.Text = $"Versions load error: {ex.Message}"
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' DB 조회: Report Text
    ' ════════════════════════════════════════

    Private Sub LoadReport(sweepId As Long)
        Try
            Dim sql = $"SELECT report_text FROM strategy_sweep_runs WHERE sweep_id = {sweepId};"
            Dim rows = ExecuteQuery(sql)
            _txtReport.Text = If(rows.Count > 0, rows(0), "(no report)")
        Catch ex As Exception
            _txtReport.Text = $"Report load error: {ex.Message}"
        End Try
    End Sub

    ' ════════════════════════════════════════
    ' 이벤트 핸들러
    ' ════════════════════════════════════════

    Private Sub _gridSweeps_SelectionChanged(sender As Object, e As EventArgs) Handles _gridSweeps.SelectionChanged
        If _gridSweeps.SelectedRows.Count = 0 Then Return
        Dim row = _gridSweeps.SelectedRows(0)
        Dim sweepId = CLng(row.Cells("ID").Value)
        LoadVersions(sweepId)
        LoadReport(sweepId)
    End Sub

    Private Sub _btnRefresh_Click(sender As Object, e As EventArgs) Handles _btnRefresh.Click
        LoadSweepRuns()
    End Sub

    Private Sub _btnRun_Click(sender As Object, e As EventArgs) Handles _btnRun.Click
        If _isRunning Then
            MessageBox.Show("Sweep is already running.", "Sweep", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim fromDate = _dtpFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        Dim toDate = _dtpTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

        Dim exePath = ResolveResearchAppPath()
        If String.IsNullOrEmpty(exePath) Then
            MessageBox.Show("zTrader.Research.App.exe not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        Dim confirm = MessageBox.Show(
            $"Run parameter sweep: {fromDate} ~ {toDate}?" & vbCrLf &
            $"2,304 combinations (default ranges)" & vbCrLf & vbCrLf &
            $"Executable: {exePath}",
            "Confirm Sweep",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question)

        If confirm <> DialogResult.OK Then Return

        RunSweepAsync(exePath, fromDate, toDate)
    End Sub

    ' ════════════════════════════════════════
    ' Sweep 실행 (CLI 위임)
    ' ════════════════════════════════════════

    Private Sub RunSweepAsync(exePath As String, fromDate As String, toDate As String)
        _isRunning = True
        _btnRun.Enabled = False
        _lblStatus.Text = "Sweep running..."
        _lblStatus.ForeColor = Color.DarkOrange
        _txtReport.Text = ""

        Dim outputBuilder As New StringBuilder()

        ThreadPool.QueueUserWorkItem(
            Sub()
                Try
                    Dim psi As New ProcessStartInfo With {
                        .FileName = exePath,
                        .Arguments = $"--sweep --from-date {fromDate} --to-date {toDate}",
                        .UseShellExecute = False,
                        .CreateNoWindow = True,
                        .RedirectStandardOutput = True,
                        .RedirectStandardError = True,
                        .StandardOutputEncoding = Encoding.UTF8,
                        .WorkingDirectory = Path.GetDirectoryName(exePath)
                    }

                    _sweepProcess = Process.Start(psi)
                    If _sweepProcess Is Nothing Then
                        UpdateUI(Sub()
                                     _lblStatus.Text = "Failed to start process"
                                     _lblStatus.ForeColor = Color.Red
                                     _isRunning = False
                                     _btnRun.Enabled = True
                                 End Sub)
                        Return
                    End If

                    ' Read output line by line
                    Dim reader = _sweepProcess.StandardOutput
                    While Not reader.EndOfStream
                        Dim line = reader.ReadLine()
                        If line Is Nothing Then Continue While
                        outputBuilder.AppendLine(line)

                        ' Update status with progress lines
                        If line.StartsWith("[sweep]") Then
                            Dim progressLine = line
                            UpdateUI(Sub() _lblStatus.Text = progressLine)
                        End If
                    End While

                    _sweepProcess.WaitForExit()

                    Dim exitCode = _sweepProcess.ExitCode
                    Dim fullOutput = outputBuilder.ToString()

                    UpdateUI(Sub()
                                 _isRunning = False
                                 _btnRun.Enabled = True
                                 If exitCode = 0 Then
                                     _lblStatus.Text = "Sweep complete!"
                                     _lblStatus.ForeColor = Color.DarkGreen
                                     _txtReport.Text = fullOutput
                                     LoadSweepRuns()
                                 Else
                                     _lblStatus.Text = $"Sweep failed (exit={exitCode})"
                                     _lblStatus.ForeColor = Color.Red
                                     _txtReport.Text = fullOutput & vbCrLf & _sweepProcess.StandardError.ReadToEnd()
                                 End If
                             End Sub)
                Catch ex As Exception
                    UpdateUI(Sub()
                                 _isRunning = False
                                 _btnRun.Enabled = True
                                 _lblStatus.Text = $"Error: {ex.Message}"
                                 _lblStatus.ForeColor = Color.Red
                             End Sub)
                End Try
            End Sub)
    End Sub

    ' ════════════════════════════════════════
    ' Research App 경로 찾기
    ' ════════════════════════════════════════

    Private Shared Function ResolveResearchAppPath() As String
        ' Try known locations
        Dim candidates = {
            "E:\2026\zTrader\src\zTrader.Research.App\bin\Debug\net10.0-windows\zTrader.Research.App.exe",
            "E:\2026\zTrader\src\zTrader.Research.App\bin\Release\net10.0-windows\zTrader.Research.App.exe",
            "E:\2026\zTrader\src\zTrader.Research.App\bin\Debug\net10.0\zTrader.Research.App.exe"
        }

        For Each path In candidates
            If File.Exists(path) Then Return path
        Next

        Return ""
    End Function

    ' ════════════════════════════════════════
    ' DB 쿼리 실행 (mysql CLI)
    ' ════════════════════════════════════════

    Private Function ExecuteQuery(sql As String) As List(Of String)
        If _config Is Nothing OrElse Not _config.Enabled Then
            Return New List(Of String)()
        End If

        Dim psi As New ProcessStartInfo With {
            .FileName = _config.MySqlCliPath,
            .Arguments = BuildMysqlArgs(sql),
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }

        Using process As Process = Process.Start(psi)
            If process Is Nothing Then Return New List(Of String)()

            Dim stdOut = process.StandardOutput.ReadToEnd()
            process.WaitForExit()

            If process.ExitCode <> 0 Then Return New List(Of String)()

            Return stdOut.
                Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries).
                ToList()
        End Using
    End Function

    Private Function BuildMysqlArgs(sql As String) As String
        Dim args As New List(Of String) From {
            "--batch", "--raw", "--skip-column-names",
            $"--host=""{_config.Host}""",
            $"--port={_config.Port.ToString(CultureInfo.InvariantCulture)}",
            $"--user=""{_config.UserName}""",
            $"--default-character-set=""{_config.Charset}"""
        }

        If Not String.IsNullOrWhiteSpace(_config.Password) Then
            args.Add($"--password=""{_config.Password}""")
        End If

        args.Add($"""{_config.DatabaseName}""")
        args.Add("-e")
        args.Add($"""{sql.Replace("""", "\""")}""")
        Return String.Join(" ", args)
    End Function

    Private Shared Function LoadDbConfig() As Services.ResearchDbMySqlConfig
        Dim current = New DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
        For i = 0 To 5
            If current Is Nothing Then Exit For
            Dim candidate = Path.Combine(current.FullName, "db.config")
            If File.Exists(candidate) Then
                Dim json = File.ReadAllText(candidate, Encoding.UTF8)
                Dim config = JsonConvert.DeserializeObject(Of Services.ResearchDbMySqlConfig)(json)
                If config IsNot Nothing Then
                    If String.IsNullOrWhiteSpace(config.MySqlCliPath) Then config.MySqlCliPath = "mysql"
                    If String.IsNullOrWhiteSpace(config.Host) Then config.Host = "127.0.0.1"
                    If config.Port <= 0 Then config.Port = 3306
                    If String.IsNullOrWhiteSpace(config.DatabaseName) Then config.DatabaseName = "strategy_research"
                    If String.IsNullOrWhiteSpace(config.UserName) Then config.UserName = "root"
                    If String.IsNullOrWhiteSpace(config.Charset) Then config.Charset = "utf8mb4"
                    Return config
                End If
            End If
            current = current.Parent
        Next
        Return Nothing
    End Function

    ' ════════════════════════════════════════
    ' 유틸
    ' ════════════════════════════════════════

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

    Private Shared Function SafeLong(s As String) As Long
        Dim v As Long
        Long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, v)
        Return v
    End Function

    Private Shared Function SafeInt(s As String) As Integer
        Dim v As Integer
        Integer.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, v)
        Return v
    End Function

    Private Shared Function SafeDec(s As String) As Decimal
        Dim v As Decimal
        Decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, v)
        Return v
    End Function

    Private Shared Function SafeDbl(s As String) As Double
        Dim v As Double
        Double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, v)
        Return v
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            If _sweepProcess IsNot Nothing AndAlso Not _sweepProcess.HasExited Then
                Try : _sweepProcess.Kill() : Catch : End Try
            End If
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
