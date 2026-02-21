' ═══════════════════════════════════════════════════════════════
' StockInfoForm.vb — 종목정보 FastGrid 도킹 폼
' ═══════════════════════════════════════════════════════════════
' DataGridView 기반 실시간 종목 테이블.
' 더블클릭 → 차트 열기, 우클릭 → 컨텍스트 메뉴.
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports [Shared]
Imports WeifenLuo.WinFormsUI.Docking

Public Class StockInfoForm
    Inherits DockFormBase

    Private WithEvents _grid As DataGridView
    Private WithEvents _toolbar As ToolStrip
    Private _btnFilter As ToolStripButton
    Private _btnClear As ToolStripButton
    Private _btnExport As ToolStripButton
    Private _lblCount As ToolStripLabel
    Private _cboSource As ToolStripComboBox

    ' 컬럼 인덱스 캐시
    Private _colCode As Integer = 0
    Private _colName As Integer = 1
    Private _colSource As Integer = 2
    Private _colPrice As Integer = 3
    Private _colChange As Integer = 4
    Private _colChangeRate As Integer = 5
    Private _colVolume As Integer = 6
    Private _colHigh As Integer = 7
    Private _colLow As Integer = 8
    Private _colStrength As Integer = 9
    Private _colState As Integer = 10
    Private _colCandles As Integer = 11

    Public Sub New()
        Me.Text = "종목정보"
        Me.DockAreas = DockAreas.DockLeft Or DockAreas.DockRight Or DockAreas.Float Or DockAreas.Document
        Me.ShowHint = DockState.DockLeft
        InitControls()
        SubscribeBus()
    End Sub

    Public Overrides ReadOnly Property DefaultDockState As DockState
        Get
            Return DockState.DockLeft
        End Get
    End Property

    Private Sub InitControls()
        ' ── 툴바 ──
        _toolbar = New ToolStrip()
        _toolbar.GripStyle = ToolStripGripStyle.Hidden

        _cboSource = New ToolStripComboBox()
        _cboSource.Items.AddRange({"전체", "조건검색", "주도섹터", "프로그램순매수", "관심종목", "코스피추종", "코스닥추종"})
        _cboSource.SelectedIndex = 0
        _cboSource.DropDownStyle = ComboBoxStyle.DropDownList
        AddHandler _cboSource.SelectedIndexChanged, Sub(s, e) RefreshGrid()

        _btnFilter = New ToolStripButton("필터 적용")
        AddHandler _btnFilter.Click, Sub(s, e)
                                         StockInfoManager.I.ApplyFilter()
                                         RefreshGrid()
                                     End Sub

        _btnClear = New ToolStripButton("전체 삭제")
        AddHandler _btnClear.Click, Sub(s, e)
                                        If MessageBox.Show("모든 종목을 삭제하시겠습니까?", "확인",
                                                           MessageBoxButtons.YesNo) = DialogResult.Yes Then
                                            StockInfoManager.I.Clear()
                                            RefreshGrid()
                                        End If
                                    End Sub

        _lblCount = New ToolStripLabel("0종목")

        _toolbar.Items.AddRange({New ToolStripLabel("소스:"), _cboSource,
                                  New ToolStripSeparator(), _btnFilter,
                                  New ToolStripSeparator(), _btnClear,
                                  New ToolStripSeparator(), _lblCount})

        ' ── DataGridView ──
        _grid = New DataGridView()
        _grid.Dock = DockStyle.Fill
        _grid.ReadOnly = True
        _grid.AllowUserToAddRows = False
        _grid.AllowUserToDeleteRows = False
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        _grid.MultiSelect = False
        _grid.RowHeadersVisible = False
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        _grid.BackgroundColor = Color.FromArgb(30, 30, 30)
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(40, 40, 40)
        _grid.DefaultCellStyle.ForeColor = Color.White
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(60, 60, 80)
        _grid.DefaultCellStyle.Font = New Font("맑은 고딕", 9)
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(50, 50, 50)
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
        _grid.EnableHeadersVisualStyles = False
        _grid.GridColor = Color.FromArgb(60, 60, 60)

        ' 컬럼 정의
        _grid.Columns.Add("Code", "코드")           ' 0
        _grid.Columns.Add("Name", "종목명")          ' 1
        _grid.Columns.Add("Source", "소스")          ' 2
        _grid.Columns.Add("Price", "현재가")          ' 3
        _grid.Columns.Add("Change", "전일비")         ' 4
        _grid.Columns.Add("ChangeRate", "등락률%")    ' 5
        _grid.Columns.Add("Volume", "거래량")         ' 6
        _grid.Columns.Add("High", "고가")            ' 7
        _grid.Columns.Add("Low", "저가")             ' 8
        _grid.Columns.Add("Strength", "체결강도")     ' 9
        _grid.Columns.Add("State", "상태")           ' 10
        _grid.Columns.Add("Candles", "캔들")          ' 11

        _grid.Columns(0).Width = 70
        _grid.Columns(1).Width = 100
        _grid.Columns(2).Width = 80
        _grid.Columns(3).Width = 75
        _grid.Columns(4).Width = 65
        _grid.Columns(5).Width = 65
        _grid.Columns(6).Width = 80
        _grid.Columns(7).Width = 70
        _grid.Columns(8).Width = 70
        _grid.Columns(9).Width = 60
        _grid.Columns(10).Width = 60
        _grid.Columns(11).Width = 50

        ' 오른쪽 정렬 (숫자)
        For i = 3 To 11
            _grid.Columns(i).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight
        Next

        ' 더블클릭 → 차트 열기
        AddHandler _grid.CellDoubleClick, Sub(s, e)
                                              If e.RowIndex < 0 Then Return
                                              Dim code = CStr(_grid.Rows(e.RowIndex).Cells(0).Value)
                                              Dim name = CStr(_grid.Rows(e.RowIndex).Cells(1).Value)
                                              AppLogger.I.Info($"차트 열기 요청: {code} {name}", "StockInfo")
                                              ' TODO: MainShell.ShowDocumentForm(Of ChartForm)(...)
                                          End Sub

        Me.Controls.Add(_grid)
        Me.Controls.Add(_toolbar)
    End Sub

    ' ─── Bus 구독 ───

    Private Sub SubscribeBus()
        MessageBus.I.On(Topics.STOCKINFO_ADDED, Sub(m) SafeUI(Sub() RefreshGrid()))
        MessageBus.I.On(Topics.STOCKINFO_REMOVED, Sub(m) SafeUI(Sub() RefreshGrid()))
        MessageBus.I.On(Topics.STOCKINFO_CLEAR, Sub(m) SafeUI(Sub() RefreshGrid()))
        MessageBus.I.On(Topics.STOCKINFO_FILTER_APPLIED, Sub(m) SafeUI(Sub() RefreshGrid()))

        ' 실시간 업데이트 (틱마다 전체 갱신은 비효율 → 개별 행 업데이트)
        MessageBus.I.On(Topics.STOCKINFO_UPDATED, Sub(m)
                                                      Dim code = m.Str("code")
                                                      If code <> "" Then SafeUI(Sub() UpdateRow(code))
                                                  End Sub)

        ' Data Ready 알림
        MessageBus.I.On(Topics.STOCKINFO_DATA_READY, Sub(m)
                                                         SafeUI(Sub()
                                                                    _lblCount.Text = $"{StockInfoManager.I.ReadyCount}/{StockInfoManager.I.Count} Ready"
                                                                End Sub)
                                                     End Sub)
    End Sub

    ' ─── 그리드 갱신 ───

    Private Sub RefreshGrid()
        _grid.SuspendLayout()

        Dim filterSource = _cboSource.Text
        Dim items = StockInfoManager.I.GetAll()

        ' 소스 필터
        If filterSource <> "전체" Then
            Dim src As DataSourceType = DataSourceType.수동추가
            [Enum].TryParse(filterSource, True, src)
            items = items.Where(Function(x) x.HasSource(src)).ToList()
        End If

        ' 행 수 맞추기
        While _grid.Rows.Count > items.Count
            _grid.Rows.RemoveAt(_grid.Rows.Count - 1)
        End While
        While _grid.Rows.Count < items.Count
            _grid.Rows.Add()
        End While

        For i = 0 To items.Count - 1
            FillRow(_grid.Rows(i), items(i))
        Next

        _lblCount.Text = $"{StockInfoManager.I.ReadyCount}/{StockInfoManager.I.Count} Ready"
        _grid.ResumeLayout()
    End Sub

    Private Sub UpdateRow(code As String)
        Dim item = StockInfoManager.I.GetItem(code)
        If item Is Nothing Then Return

        For Each row As DataGridViewRow In _grid.Rows
            If CStr(row.Cells(0).Value) = code Then
                FillRow(row, item)
                Exit For
            End If
        Next
    End Sub

    Private Sub FillRow(row As DataGridViewRow, item As StockInfoItem)
        row.Cells(_colCode).Value = item.Code
        row.Cells(_colName).Value = item.Name
        row.Cells(_colSource).Value = item.SourceText()
        row.Cells(_colPrice).Value = item.Price.ToString("N0")
        row.Cells(_colChange).Value = item.Change.ToString("N0")
        row.Cells(_colChangeRate).Value = item.ChangeRate.ToString("0.00")
        row.Cells(_colVolume).Value = item.Volume.ToString("N0")
        row.Cells(_colHigh).Value = item.High.ToString("N0")
        row.Cells(_colLow).Value = item.Low.ToString("N0")
        row.Cells(_colStrength).Value = item.Strength.ToString("0.0")
        row.Cells(_colState).Value = item.State.ToString()
        row.Cells(_colCandles).Value = item.CandleCount.ToString()

        ' 등락률 색상
        If item.ChangeRate > 0 Then
            row.Cells(_colPrice).Style.ForeColor = Color.Red
            row.Cells(_colChange).Style.ForeColor = Color.Red
            row.Cells(_colChangeRate).Style.ForeColor = Color.Red
        ElseIf item.ChangeRate < 0 Then
            row.Cells(_colPrice).Style.ForeColor = Color.RoyalBlue
            row.Cells(_colChange).Style.ForeColor = Color.RoyalBlue
            row.Cells(_colChangeRate).Style.ForeColor = Color.RoyalBlue
        Else
            row.Cells(_colPrice).Style.ForeColor = Color.White
            row.Cells(_colChange).Style.ForeColor = Color.White
            row.Cells(_colChangeRate).Style.ForeColor = Color.White
        End If

        ' 상태 색상
        Select Case item.State
            Case DataReadyState.Ready
                row.Cells(_colState).Style.ForeColor = Color.LimeGreen
            Case DataReadyState.Filtered
                row.Cells(_colState).Style.ForeColor = Color.Gray
            Case DataReadyState.CandleLoaded
                row.Cells(_colState).Style.ForeColor = Color.Yellow
            Case Else
                row.Cells(_colState).Style.ForeColor = Color.Orange
        End Select
    End Sub

    Private Sub SafeUI(action As Action)
        If Me.InvokeRequired Then
            Try : Me.BeginInvoke(action) : Catch : End Try
        Else
            action()
        End If
    End Sub

End Class
