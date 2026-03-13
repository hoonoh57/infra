Imports System
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports MainApp.Services

Public Class ResearchDbManagerDialog
    Inherits Form

    Private ReadOnly _service As ResearchDbMaintenanceService = ResearchDbMaintenanceService.Instance

    Private ReadOnly txtUniverseSource As New TextBox()
    Private ReadOnly txtOutputRoot As New TextBox()
    Private ReadOnly chkAutoRun As New CheckBox()
    Private ReadOnly dtpAutoRunTime As New DateTimePicker()
    Private ReadOnly chkDaily As New CheckBox()
    Private ReadOnly chkMinute As New CheckBox()
    Private ReadOnly chkTick30 As New CheckBox()
    Private ReadOnly chkIndexes As New CheckBox()
    Private ReadOnly dtpTradingDate As New DateTimePicker()
    Private ReadOnly dtpRangeStart As New DateTimePicker()
    Private ReadOnly dtpRangeEnd As New DateTimePicker()
    Private ReadOnly txtLog As New TextBox()
    Private ReadOnly lblStatus As New Label()

    Public Sub New()
        Text = "연구 DB 관리"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.SizableToolWindow
        MinimumSize = New Size(980, 720)
        Size = New Size(1080, 820)
        BuildLayout()
        LoadSettings()
        AddHandler _service.LogReceived, AddressOf OnLogReceived
    End Sub

    Private Sub BuildLayout()
        Dim root As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .RowCount = 5,
            .Padding = New Padding(10)
        }
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

        root.Controls.Add(BuildPathGroup(), 0, 0)
        root.Controls.Add(BuildOptionGroup(), 0, 1)
        root.Controls.Add(BuildRunGroup(), 0, 2)
        root.Controls.Add(BuildBottomBar(), 0, 3)

        txtLog.Multiline = True
        txtLog.ReadOnly = True
        txtLog.ScrollBars = ScrollBars.Vertical
        txtLog.Dock = DockStyle.Fill
        txtLog.Font = New Font("Consolas", 9.0F)
        root.Controls.Add(txtLog, 0, 4)

        Controls.Add(root)
    End Sub

    Private Function BuildPathGroup() As Control
        Dim grp As New GroupBox With {.Text = "기본 경로", .Dock = DockStyle.Top, .AutoSize = True}
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 2, .Padding = New Padding(8), .AutoSize = True}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90))

        layout.Controls.Add(New Label With {.Text = "Universe 소스", .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, 0)
        txtUniverseSource.Dock = DockStyle.Fill
        layout.Controls.Add(txtUniverseSource, 1, 0)
        Dim btnBrowseUniverse As New Button With {.Text = "찾아보기", .Dock = DockStyle.Fill}
        AddHandler btnBrowseUniverse.Click, AddressOf OnBrowseUniverse
        layout.Controls.Add(btnBrowseUniverse, 2, 0)

        layout.Controls.Add(New Label With {.Text = "출력 폴더", .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, 1)
        txtOutputRoot.Dock = DockStyle.Fill
        layout.Controls.Add(txtOutputRoot, 1, 1)
        Dim btnBrowseOutput As New Button With {.Text = "폴더 선택", .Dock = DockStyle.Fill}
        AddHandler btnBrowseOutput.Click, AddressOf OnBrowseOutput
        layout.Controls.Add(btnBrowseOutput, 2, 1)

        grp.Controls.Add(layout)
        Return grp
    End Function

    Private Function BuildOptionGroup() As Control
        Dim grp As New GroupBox With {.Text = "자동 업데이트 / 대상 데이터", .Dock = DockStyle.Top, .AutoSize = True}
        Dim layout As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(8), .AutoSize = True, .WrapContents = True}

        chkAutoRun.Text = "장 종료 후 자동 업데이트"
        dtpAutoRunTime.Format = DateTimePickerFormat.Time
        dtpAutoRunTime.ShowUpDown = True
        chkDaily.Text = "일봉 500개"
        chkMinute.Text = "1분봉"
        chkTick30.Text = "30틱봉"
        chkIndexes.Text = "지수 1분봉"

        layout.Controls.Add(chkAutoRun)
        layout.Controls.Add(New Label With {.Text = "실행 시각", .AutoSize = True, .Margin = New Padding(12, 8, 4, 0)})
        layout.Controls.Add(dtpAutoRunTime)
        layout.Controls.Add(chkDaily)
        layout.Controls.Add(chkMinute)
        layout.Controls.Add(chkTick30)
        layout.Controls.Add(chkIndexes)

        grp.Controls.Add(layout)
        Return grp
    End Function

    Private Function BuildRunGroup() As Control
        Dim grp As New GroupBox With {.Text = "실행 모드", .Dock = DockStyle.Top, .AutoSize = True}
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 5, .Padding = New Padding(8), .AutoSize = True}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 240))

        layout.Controls.Add(New Label With {.Text = "전기간 신규생성", .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, 0)
        dtpRangeStart.Format = DateTimePickerFormat.Short
        dtpRangeEnd.Format = DateTimePickerFormat.Short
        Dim rangePanel As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .WrapContents = False}
        rangePanel.Controls.Add(dtpRangeStart)
        rangePanel.Controls.Add(New Label With {.Text = "~", .AutoSize = True, .Margin = New Padding(6, 8, 6, 0)})
        rangePanel.Controls.Add(dtpRangeEnd)
        layout.Controls.Add(rangePanel, 1, 0)
        Dim btnFullRebuild As New Button With {.Text = "전기간 신규생성", .Dock = DockStyle.Fill}
        AddHandler btnFullRebuild.Click, AddressOf OnRunFullRebuild
        layout.Controls.Add(btnFullRebuild, 2, 0)
        layout.Controls.Add(New Label With {.Text = "기존 연구 데이터 테이블을 비운 뒤 지정 기간 전체를 다시 생성", .AutoSize = True, .Anchor = AnchorStyles.Left}, 3, 0)

        layout.Controls.Add(New Label With {.Text = "기간 업데이트", .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, 1)
        layout.Controls.Add(New Label With {.Text = "위의 시작일 ~ 종료일 범위 사용", .AutoSize = True, .Anchor = AnchorStyles.Left}, 1, 1)
        Dim btnRangeUpdate As New Button With {.Text = "기간 업데이트", .Dock = DockStyle.Fill}
        AddHandler btnRangeUpdate.Click, AddressOf OnRunRangeUpdate
        layout.Controls.Add(btnRangeUpdate, 2, 1)
        layout.Controls.Add(New Label With {.Text = "기존 데이터는 유지하고 지정 기간만 다운로드 및 업서트", .AutoSize = True, .Anchor = AnchorStyles.Left}, 3, 1)

        layout.Controls.Add(New Label With {.Text = "지정 업데이트", .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, 2)
        dtpTradingDate.Format = DateTimePickerFormat.Short
        layout.Controls.Add(dtpTradingDate, 1, 2)
        Dim btnDateUpdate As New Button With {.Text = "지정 업데이트", .Dock = DockStyle.Fill}
        AddHandler btnDateUpdate.Click, AddressOf OnRunDateUpdate
        layout.Controls.Add(btnDateUpdate, 2, 2)
        layout.Controls.Add(New Label With {.Text = "선택한 하루만 다운로드 및 업서트", .AutoSize = True, .Anchor = AnchorStyles.Left}, 3, 2)

        layout.Controls.Add(New Label With {.Text = "자동 업데이트", .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, 3)
        layout.Controls.Add(New Label With {.Text = "완료 상태를 보고 이어받기 / 실패 일자 재개", .AutoSize = True, .Anchor = AnchorStyles.Left}, 1, 3)
        Dim btnAutoUpdate As New Button With {.Text = "자동 업데이트", .Dock = DockStyle.Fill}
        AddHandler btnAutoUpdate.Click, AddressOf OnRunAutoUpdate
        layout.Controls.Add(btnAutoUpdate, 2, 3)
        layout.Controls.Add(New Label With {.Text = "운영 모드. 선택 기간과 무관하게 미완/당일 데이터만 이어받음", .AutoSize = True, .Anchor = AnchorStyles.Left}, 3, 3)

        Dim btnRunDaily As New Button With {.Text = "일봉만", .Dock = DockStyle.Fill}
        AddHandler btnRunDaily.Click, Sub(sender, e) RunSingleStage(True, False, False, False)
        layout.Controls.Add(btnRunDaily, 0, 4)
        Dim btnRunMinute As New Button With {.Text = "1분봉만", .Dock = DockStyle.Fill}
        AddHandler btnRunMinute.Click, Sub(sender, e) RunSingleStage(False, True, False, False)
        layout.Controls.Add(btnRunMinute, 1, 4)
        Dim btnRunTick As New Button With {.Text = "30틱봉만", .Dock = DockStyle.Fill}
        AddHandler btnRunTick.Click, Sub(sender, e) RunSingleStage(False, False, True, False)
        layout.Controls.Add(btnRunTick, 2, 4)
        Dim btnRunIndex As New Button With {.Text = "지수만", .Dock = DockStyle.Fill}
        AddHandler btnRunIndex.Click, Sub(sender, e) RunSingleStage(False, False, False, True)
        layout.Controls.Add(btnRunIndex, 3, 4)

        grp.Controls.Add(layout)
        Return grp
    End Function

    Private Function BuildBottomBar() As Control
        Dim panel As New FlowLayoutPanel With {.Dock = DockStyle.Top, .Padding = New Padding(0), .AutoSize = True}
        Dim btnSave As New Button With {.Text = "설정 저장", .AutoSize = True}
        AddHandler btnSave.Click, AddressOf OnSaveSettings
        panel.Controls.Add(btnSave)

        Dim btnOpenFolder As New Button With {.Text = "출력 폴더 열기", .AutoSize = True}
        AddHandler btnOpenFolder.Click, AddressOf OnOpenOutputFolder
        panel.Controls.Add(btnOpenFolder)

        lblStatus.AutoSize = True
        lblStatus.Text = "연구 DB 캔들 다운로드 / 업서트 관리"
        lblStatus.Margin = New Padding(16, 8, 0, 0)
        panel.Controls.Add(lblStatus)
        Return panel
    End Function

    Private Sub LoadSettings()
        Dim settings = _service.GetSettings()
        txtUniverseSource.Text = settings.UniverseSourcePath
        txtOutputRoot.Text = settings.OutputRootPath
        chkAutoRun.Checked = settings.AutoRunEnabled
        chkDaily.Checked = settings.ExportDailyCandles
        chkMinute.Checked = settings.ExportMinuteCandles
        chkTick30.Checked = settings.ExportTick30Candles
        chkIndexes.Checked = settings.ExportMarketIndexes

        Dim parsedTime As DateTime
        If DateTime.TryParse(settings.AutoRunTime, parsedTime) Then
            dtpAutoRunTime.Value = Date.Today.Add(parsedTime.TimeOfDay)
        End If

        Dim parsedDate As DateTime
        If DateTime.TryParse(settings.BackfillStartDate, parsedDate) Then
            dtpRangeStart.Value = parsedDate.Date
        Else
            dtpRangeStart.Value = Date.Today.AddDays(-7)
        End If

        If DateTime.TryParse(settings.BackfillEndDate, parsedDate) Then
            dtpRangeEnd.Value = parsedDate.Date
        Else
            dtpRangeEnd.Value = Date.Today
        End If

        dtpTradingDate.Value = Date.Today
        AppendLog("연구 DB 설정을 불러왔습니다.")
    End Sub

    Private Sub SaveSettingsFromUi()
        Dim settings = _service.GetSettings()
        settings.UniverseSourcePath = txtUniverseSource.Text.Trim()
        settings.OutputRootPath = txtOutputRoot.Text.Trim()
        settings.AutoRunEnabled = chkAutoRun.Checked
        settings.AutoRunTime = dtpAutoRunTime.Value.ToString("HH:mm")
        settings.ExportDailyCandles = chkDaily.Checked
        settings.ExportMinuteCandles = chkMinute.Checked
        settings.ExportTick30Candles = chkTick30.Checked
        settings.ExportMarketIndexes = chkIndexes.Checked
        settings.BackfillStartDate = dtpRangeStart.Value.Date.ToString("yyyy-MM-dd")
        settings.BackfillEndDate = dtpRangeEnd.Value.Date.ToString("yyyy-MM-dd")
        _service.UpdateSettings(settings)
    End Sub

    Private Sub OnBrowseUniverse(sender As Object, e As EventArgs)
        Using dlg As New OpenFileDialog()
            dlg.Filter = "Universe Source (*.sql;*.txt;*.csv)|*.sql;*.txt;*.csv|All Files (*.*)|*.*"
            dlg.FileName = txtUniverseSource.Text
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtUniverseSource.Text = dlg.FileName
            End If
        End Using
    End Sub

    Private Sub OnBrowseOutput(sender As Object, e As EventArgs)
        Using dlg As New FolderBrowserDialog()
            dlg.SelectedPath = txtOutputRoot.Text
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                txtOutputRoot.Text = dlg.SelectedPath
            End If
        End Using
    End Sub

    Private Sub OnSaveSettings(sender As Object, e As EventArgs)
        SaveSettingsFromUi()
        lblStatus.Text = "설정을 저장했습니다."
    End Sub

    Private Sub OnRunFullRebuild(sender As Object, e As EventArgs)
        SaveSettingsFromUi()
        Dim msg = "전기간 신규생성은 기존 연구 데이터 테이블을 비우고 다시 채웁니다. 계속하시겠습니까?"
        If MessageBox.Show(Me, msg, "연구 DB 관리", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        _service.RunFullRebuildAsync(dtpRangeStart.Value.Date, dtpRangeEnd.Value.Date, chkDaily.Checked, chkMinute.Checked, chkTick30.Checked, chkIndexes.Checked)
    End Sub

    Private Sub OnRunRangeUpdate(sender As Object, e As EventArgs)
        SaveSettingsFromUi()
        _service.RunDateRangeUpdateAsync(dtpRangeStart.Value.Date, dtpRangeEnd.Value.Date, chkDaily.Checked, chkMinute.Checked, chkTick30.Checked, chkIndexes.Checked)
    End Sub

    Private Sub OnRunDateUpdate(sender As Object, e As EventArgs)
        SaveSettingsFromUi()
        _service.RunDateUpdateAsync(dtpTradingDate.Value.Date, chkDaily.Checked, chkMinute.Checked, chkTick30.Checked, chkIndexes.Checked)
    End Sub

    Private Sub OnRunAutoUpdate(sender As Object, e As EventArgs)
        SaveSettingsFromUi()
        _service.RunAutoUpdateAsync(chkDaily.Checked, chkMinute.Checked, chkTick30.Checked, chkIndexes.Checked)
    End Sub

    Private Sub RunSingleStage(exportDaily As Boolean, exportMinute As Boolean, exportTick30 As Boolean, exportIndexes As Boolean)
        SaveSettingsFromUi()
        _service.RunDateUpdateAsync(dtpTradingDate.Value.Date, exportDaily, exportMinute, exportTick30, exportIndexes)
    End Sub

    Private Sub OnOpenOutputFolder(sender As Object, e As EventArgs)
        Dim path = txtOutputRoot.Text.Trim()
        If String.IsNullOrWhiteSpace(path) OrElse Not Directory.Exists(path) Then
            MessageBox.Show(Me, "출력 폴더가 아직 없습니다.", "연구 DB 관리", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Process.Start("explorer.exe", path)
    End Sub

    Private Sub OnLogReceived(message As String)
        If IsDisposed Then Return
        If InvokeRequired Then
            BeginInvoke(New Action(Of String)(AddressOf OnLogReceived), message)
            Return
        End If
        AppendLog(message)
        lblStatus.Text = message
    End Sub

    Private Sub AppendLog(message As String)
        If String.IsNullOrWhiteSpace(message) Then Return
        txtLog.AppendText(message & Environment.NewLine)
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        RemoveHandler _service.LogReceived, AddressOf OnLogReceived
        MyBase.OnFormClosed(e)
    End Sub
End Class
