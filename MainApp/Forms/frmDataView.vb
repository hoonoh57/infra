' frmDataView.vb — 차트 데이터 배열 뷰어

Imports System.Drawing
Imports System.Data
Imports System.Windows.Forms
Imports WeifenLuo.WinFormsUI.Docking

Public Class frmDataView
    Inherits DockFormBase

    Private WithEvents _toolbar As ToolStrip
    Private WithEvents _cboArrays As ToolStripComboBox
    Private WithEvents _btnRefresh As ToolStripButton
    Private WithEvents _grid As DataGridView

    Private _stockCode As String = ""
    Private _tables As New Dictionary(Of String, DataTable)(StringComparer.OrdinalIgnoreCase)

    Public Sub New()
        Me.Text = "데이터보기"
        Me.DockAreas = DockAreas.DockBottom Or DockAreas.DockRight Or DockAreas.Float Or DockAreas.Document
        Me.ShowHint = DockState.DockBottom
        InitControls()
    End Sub

    Public Overrides ReadOnly Property DefaultDockState As DockState
        Get
            Return DockState.DockBottom
        End Get
    End Property

    Private Sub InitControls()
        _toolbar = New ToolStrip()
        _toolbar.GripStyle = ToolStripGripStyle.Hidden

        _cboArrays = New ToolStripComboBox()
        _cboArrays.DropDownStyle = ComboBoxStyle.DropDownList
        _cboArrays.Width = 260
        AddHandler _cboArrays.SelectedIndexChanged, AddressOf OnArrayChanged

        _btnRefresh = New ToolStripButton("갱신")
        AddHandler _btnRefresh.Click, Sub()
                                          OnArrayChanged(Nothing, EventArgs.Empty)
                                      End Sub

        _toolbar.Items.AddRange({
            New ToolStripLabel("배열:"),
            _cboArrays,
            New ToolStripSeparator(),
            _btnRefresh
        })

        _grid = New DataGridView()
        _grid.Dock = DockStyle.Fill
        _grid.ReadOnly = True
        _grid.AllowUserToAddRows = False
        _grid.AllowUserToDeleteRows = False
        _grid.RowHeadersVisible = False
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
        _grid.BackgroundColor = Color.FromArgb(24, 26, 32)
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(30, 32, 40)
        _grid.DefaultCellStyle.ForeColor = Color.White
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(60, 70, 90)
        _grid.DefaultCellStyle.Font = New Font("Consolas", 9)
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 48, 58)
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White
        _grid.EnableHeadersVisualStyles = False
        _grid.GridColor = Color.FromArgb(60, 64, 75)

        Me.Controls.Add(_grid)
        Me.Controls.Add(_toolbar)
    End Sub

    Public Sub SetData(stockCode As String, arrays As List(Of ChartDataArray))
        _stockCode = stockCode
        _tables.Clear()

        If arrays IsNot Nothing Then
            For Each arr In arrays
                If arr Is Nothing Then Continue For
                If String.IsNullOrWhiteSpace(arr.Name) Then Continue For
                If arr.Table Is Nothing Then Continue For
                _tables(arr.Name) = arr.Table
            Next
        End If

        _cboArrays.Items.Clear()
        For Each k In _tables.Keys
            _cboArrays.Items.Add(k)
        Next

        If _cboArrays.Items.Count > 0 Then
            _cboArrays.SelectedIndex = 0
        Else
            _grid.DataSource = Nothing
        End If

        Me.Text = $"데이터보기 [{_stockCode}]"
    End Sub

    Private Sub OnArrayChanged(sender As Object, e As EventArgs)
        Dim selectedName = If(_cboArrays.SelectedItem, "").ToString()
        If selectedName = "" Then
            _grid.DataSource = Nothing
            Return
        End If

        If _tables.ContainsKey(selectedName) Then
            _grid.DataSource = _tables(selectedName)
        End If
    End Sub
End Class
