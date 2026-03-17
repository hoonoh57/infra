Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Public Class Kosdaq150SelectionDialog
    Inherits Form

    Private ReadOnly _service As New Services.Kosdaq150SelectionService()
    Private ReadOnly _dtTradingDate As New DateTimePicker()
    Private ReadOnly _dtCutoffTime As New DateTimePicker()
    Private ReadOnly _numMinRisePct As New NumericUpDown()
    Private ReadOnly _numMinTradeAmount As New NumericUpDown()
    Private ReadOnly _btnLoad As New Button()
    Private ReadOnly _btnLoadAll As New Button()
    Private ReadOnly _btnOk As New Button()
    Private ReadOnly _btnCancel As New Button()
    Private ReadOnly _lblStatus As New Label()
    Private ReadOnly _grid As New DataGridView()

    Private _rows As List(Of Services.Kosdaq150CandidateRow) = New List(Of Services.Kosdaq150CandidateRow)()

    Public Sub New()
        Text = "KOSDAQ150 후보 편성"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        Width = 900
        Height = 620

        BuildLayout()
        LoadDefaults()
    End Sub

    Public ReadOnly Property SelectedCodes As String()
        Get
            Return _rows.Select(Function(x) x.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        End Get
    End Property

    Public ReadOnly Property SourceDetail As String
        Get
            Return $"KOSDAQ150 {SelectedTradingDate:yyyy-MM-dd} {SelectedCutoffTime:hh\:mm} >= {SelectedMinRisePct:0.##}% / >= {SelectedMinTradeAmountEok:0.##}억"
        End Get
    End Property

    Public ReadOnly Property SelectedTradingDate As DateTime
        Get
            Return _dtTradingDate.Value.Date
        End Get
    End Property

    Public ReadOnly Property SelectedCutoffTime As TimeSpan
        Get
            Return _dtCutoffTime.Value.TimeOfDay
        End Get
    End Property

    Public ReadOnly Property SelectedMinRisePct As Double
        Get
            Return CDbl(_numMinRisePct.Value)
        End Get
    End Property

    Public ReadOnly Property SelectedMinTradeAmountEok As Double
        Get
            Return CDbl(_numMinTradeAmount.Value)
        End Get
    End Property

    Private Sub BuildLayout()
        Dim root As New TableLayoutPanel() With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .RowCount = 3,
            .Padding = New Padding(10)
        }
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Controls.Add(root)

        Dim filterPanel As New TableLayoutPanel() With {
            .Dock = DockStyle.Top,
            .ColumnCount = 9,
            .AutoSize = True
        }
        For i = 0 To 8
            filterPanel.ColumnStyles.Add(New ColumnStyle(If(i Mod 2 = 0, SizeType.AutoSize, SizeType.Percent), If(i Mod 2 = 0, 0, 50.0F)))
        Next

        _dtTradingDate.Format = DateTimePickerFormat.Custom
        _dtTradingDate.CustomFormat = "yyyy-MM-dd"

        _dtCutoffTime.Format = DateTimePickerFormat.Time
        _dtCutoffTime.ShowUpDown = True

        _numMinRisePct.DecimalPlaces = 2
        _numMinRisePct.Minimum = 0
        _numMinRisePct.Maximum = 100
        _numMinRisePct.Value = 3D

        _numMinTradeAmount.DecimalPlaces = 2
        _numMinTradeAmount.Minimum = 0
        _numMinTradeAmount.Maximum = 100000
        _numMinTradeAmount.Value = 30D

        _btnLoad.Text = "후보 조회"
        AddHandler _btnLoad.Click, AddressOf OnLoadCandidates

        _btnLoadAll.Text = "전체 로드"
        _btnLoadAll.BackColor = Color.FromArgb(50, 80, 120)
        _btnLoadAll.ForeColor = Color.White
        AddHandler _btnLoadAll.Click, AddressOf OnLoadAll

        Dim btnPanel As New FlowLayoutPanel() With {
            .FlowDirection = FlowDirection.LeftToRight,
            .AutoSize = True,
            .WrapContents = False
        }
        btnPanel.Controls.Add(_btnLoadAll)
        btnPanel.Controls.Add(_btnLoad)

        filterPanel.Controls.Add(New Label() With {.AutoSize = True, .Text = "검증일자", .Anchor = AnchorStyles.Left}, 0, 0)
        filterPanel.Controls.Add(_dtTradingDate, 1, 0)
        filterPanel.Controls.Add(New Label() With {.AutoSize = True, .Text = "기준시간", .Anchor = AnchorStyles.Left}, 2, 0)
        filterPanel.Controls.Add(_dtCutoffTime, 3, 0)
        filterPanel.Controls.Add(New Label() With {.AutoSize = True, .Text = "상승률 %", .Anchor = AnchorStyles.Left}, 4, 0)
        filterPanel.Controls.Add(_numMinRisePct, 5, 0)
        filterPanel.Controls.Add(New Label() With {.AutoSize = True, .Text = "거래대금 억", .Anchor = AnchorStyles.Left}, 6, 0)
        filterPanel.Controls.Add(_numMinTradeAmount, 7, 0)
        filterPanel.Controls.Add(btnPanel, 8, 0)

        _lblStatus.AutoSize = True
        _lblStatus.Padding = New Padding(0, 8, 0, 8)

        Dim headerPanel As New Panel() With {.Dock = DockStyle.Top, .Height = 70}
        filterPanel.Parent = headerPanel
        filterPanel.Location = New Point(0, 0)
        _lblStatus.Parent = headerPanel
        _lblStatus.Location = New Point(0, 36)
        root.Controls.Add(headerPanel, 0, 0)

        _grid.Dock = DockStyle.Fill
        _grid.ReadOnly = True
        _grid.AllowUserToAddRows = False
        _grid.AllowUserToDeleteRows = False
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        _grid.MultiSelect = False
        _grid.RowHeadersVisible = False
        _grid.AutoGenerateColumns = False

        _grid.Columns.Add(New DataGridViewTextBoxColumn() With {.HeaderText = "코드", .DataPropertyName = NameOf(Services.Kosdaq150CandidateRow.Code), .Width = 80})
        _grid.Columns.Add(New DataGridViewTextBoxColumn() With {.HeaderText = "종목명", .DataPropertyName = NameOf(Services.Kosdaq150CandidateRow.Name), .Width = 160})
        _grid.Columns.Add(New DataGridViewTextBoxColumn() With {.HeaderText = "전일종가", .DataPropertyName = NameOf(Services.Kosdaq150CandidateRow.PrevClose), .Width = 90, .DefaultCellStyle = New DataGridViewCellStyle() With {.Format = "N0"}})
        _grid.Columns.Add(New DataGridViewTextBoxColumn() With {.HeaderText = "기준가", .DataPropertyName = NameOf(Services.Kosdaq150CandidateRow.CutoffClose), .Width = 90, .DefaultCellStyle = New DataGridViewCellStyle() With {.Format = "N0"}})
        _grid.Columns.Add(New DataGridViewTextBoxColumn() With {.HeaderText = "상승률%", .DataPropertyName = NameOf(Services.Kosdaq150CandidateRow.RisePct), .Width = 90, .DefaultCellStyle = New DataGridViewCellStyle() With {.Format = "0.00"}})
        _grid.Columns.Add(New DataGridViewTextBoxColumn() With {.HeaderText = "누적거래대금(억)", .DataPropertyName = NameOf(Services.Kosdaq150CandidateRow.TradeAmountEok), .Width = 120, .DefaultCellStyle = New DataGridViewCellStyle() With {.Format = "0.00"}})
        root.Controls.Add(_grid, 0, 1)

        Dim buttonPanel As New FlowLayoutPanel() With {
            .Dock = DockStyle.Fill,
            .FlowDirection = FlowDirection.RightToLeft,
            .AutoSize = True
        }

        _btnOk.Text = "확인"
        _btnOk.DialogResult = DialogResult.OK
        _btnOk.Enabled = False
        AddHandler _btnOk.Click, Sub(sender, e) DialogResult = DialogResult.OK

        _btnCancel.Text = "취소"
        _btnCancel.DialogResult = DialogResult.Cancel

        buttonPanel.Controls.Add(_btnCancel)
        buttonPanel.Controls.Add(_btnOk)
        root.Controls.Add(buttonPanel, 0, 2)

        AcceptButton = _btnLoad
        CancelButton = _btnCancel
    End Sub

    Private Sub LoadDefaults()
        _dtTradingDate.Value = Date.Today
        _dtCutoffTime.Value = Date.Today.AddHours(9).AddMinutes(30)
    End Sub

    ''' <summary>
    ''' 전체 로드: KOSDAQ150 유니버스 전체를 전일 종가와 함께 로드.
    ''' 장 시작 전 준비용 — 당일 데이터 없이도 150종목 전체 표시.
    ''' </summary>
    Private Sub OnLoadAll(sender As Object, e As EventArgs)
        Try
            _rows = _service.LoadUniverse(SelectedTradingDate).ToList()
            _grid.DataSource = Nothing
            _grid.DataSource = _rows
            _lblStatus.Text = $"KOSDAQ150 전체 {_rows.Count:N0}종목 (전일 종가 기준)"
            _btnOk.Enabled = _rows.Count > 0
        Catch ex As Exception
            _rows = New List(Of Services.Kosdaq150CandidateRow)()
            _grid.DataSource = Nothing
            _btnOk.Enabled = False
            _lblStatus.Text = "전체 로드 실패"
            MessageBox.Show(Me, ex.Message, "KOSDAQ150 전체 로드", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    ''' <summary>
    ''' 후보 조회: 필터 조건에 맞는 종목만 (장중 또는 과거 데이터 검증용).
    ''' </summary>
    Private Sub OnLoadCandidates(sender As Object, e As EventArgs)
        Try
            _rows = _service.LoadCandidates(SelectedTradingDate, SelectedCutoffTime, SelectedMinRisePct, SelectedMinTradeAmountEok).ToList()
            _grid.DataSource = Nothing
            _grid.DataSource = _rows
            _lblStatus.Text = $"후보 {_rows.Count:N0}종목"
            _btnOk.Enabled = _rows.Count > 0
        Catch ex As Exception
            _rows = New List(Of Services.Kosdaq150CandidateRow)()
            _grid.DataSource = Nothing
            _btnOk.Enabled = False
            _lblStatus.Text = "후보 조회 실패"
            MessageBox.Show(Me, ex.Message, "KOSDAQ150 후보 조회", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub
End Class
