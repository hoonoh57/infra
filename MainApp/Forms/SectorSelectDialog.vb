' ═══════════════════════════════════════════════════════════════
' SectorSelectDialog.vb — 상승률 상위 섹터/테마 선택 다이얼로그
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Globalization
Imports System.Linq
Imports System.Windows.Forms
Imports [Shared]

Public Class SectorSelectDialog
    Inherits Form

    Public Enum SectorMode
        주도섹터 = 0
        테마 = 1
    End Enum

    Private ReadOnly _mode As SectorMode
    Private ReadOnly _split As SplitContainer
    Private ReadOnly _leftTitle As Label
    Private ReadOnly _rightTitle As Label
    Private ReadOnly _sectorList As ListView
    Private ReadOnly _stockList As ListView
    Private ReadOnly _lblStatus As Label
    Private ReadOnly _btnExecute As Button
    Private ReadOnly _btnCancel As Button

    Public Property SelectedCode As String = ""
    Public Property SelectedName As String = ""
    Public Property SelectedCodes As String() = Array.Empty(Of String)()

    Public Sub New(mode As SectorMode)
        _mode = mode

        Me.Text = If(mode = SectorMode.주도섹터, "상승률 상위 섹터", "상승률 상위 테마")
        Me.Size = New Size(980, 560)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False

        _split = New SplitContainer() With {
            .Dock = DockStyle.Top,
            .Location = New Point(12, 12),
            .Size = New Size(940, 450),
            .SplitterDistance = 420,
            .IsSplitterFixed = False
        }

        _leftTitle = New Label() With {
            .Text = "상승률 상위 섹터/테마",
            .Dock = DockStyle.Top,
            .Height = 24
        }

        _rightTitle = New Label() With {
            .Text = "선택한 섹터의 종목",
            .Dock = DockStyle.Top,
            .Height = 24
        }

        _sectorList = New ListView() With {
            .Dock = DockStyle.Fill,
            .View = View.Details,
            .FullRowSelect = True,
            .GridLines = True,
            .HideSelection = False,
            .MultiSelect = False
        }
        _sectorList.Columns.Add("코드", 70)
        _sectorList.Columns.Add("이름", 150)
        _sectorList.Columns.Add("1일%", 65, HorizontalAlignment.Right)
        _sectorList.Columns.Add("5일%", 65, HorizontalAlignment.Right)
        _sectorList.Columns.Add("상승비율", 70, HorizontalAlignment.Right)
        _sectorList.Columns.Add("종목수", 60, HorizontalAlignment.Right)

        _stockList = New ListView() With {
            .Dock = DockStyle.Fill,
            .View = View.Details,
            .FullRowSelect = True,
            .GridLines = True,
            .HideSelection = False,
            .MultiSelect = False
        }
        _stockList.Columns.Add("코드", 75)
        _stockList.Columns.Add("종목명", 140)
        _stockList.Columns.Add("등락률", 70, HorizontalAlignment.Right)
        _stockList.Columns.Add("현재가", 90, HorizontalAlignment.Right)
        _stockList.Columns.Add("전일대비", 80, HorizontalAlignment.Right)
        _stockList.Columns.Add("거래량", 95, HorizontalAlignment.Right)

        Dim leftPanel As New Panel() With {.Dock = DockStyle.Fill}
        leftPanel.Controls.Add(_sectorList)
        leftPanel.Controls.Add(_leftTitle)
        _split.Panel1.Controls.Add(leftPanel)

        Dim rightPanel As New Panel() With {.Dock = DockStyle.Fill}
        rightPanel.Controls.Add(_stockList)
        rightPanel.Controls.Add(_rightTitle)
        _split.Panel2.Controls.Add(rightPanel)

        _lblStatus = New Label() With {
            .Location = New Point(12, 470),
            .Size = New Size(940, 20),
            .Text = "상승률 상위 섹터를 조회하는 중..."
        }

        _btnExecute = New Button() With {
            .Text = "추가",
            .Location = New Point(740, 500),
            .Size = New Size(100, 35),
            .Enabled = False
        }

        _btnCancel = New Button() With {
            .Text = "취소",
            .Location = New Point(852, 500),
            .Size = New Size(100, 35),
            .DialogResult = DialogResult.Cancel
        }

        AddHandler _sectorList.SelectedIndexChanged, AddressOf OnSectorSelectionChanged
        AddHandler _sectorList.DoubleClick, AddressOf OnSectorDoubleClick
        AddHandler _stockList.DoubleClick, Sub(sender, e) ExecuteSelection()
        AddHandler _btnExecute.Click, Sub(sender, e) ExecuteSelection()

        Me.Controls.Add(_split)
        Me.Controls.Add(_lblStatus)
        Me.Controls.Add(_btnExecute)
        Me.Controls.Add(_btnCancel)
        Me.AcceptButton = _btnExecute
        Me.CancelButton = _btnCancel
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        MessageBus.I.On(Topics.SECTOR_LIST_RESULT, AddressOf OnSectorListResult)
        MessageBus.I.On(Topics.THEME_STOCKS_RESULT, AddressOf OnThemeStocksResult)
        LoadSectorList()
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        MessageBus.I.Off(Topics.SECTOR_LIST_RESULT, AddressOf OnSectorListResult)
        MessageBus.I.Off(Topics.THEME_STOCKS_RESULT, AddressOf OnThemeStocksResult)
        MyBase.OnFormClosed(e)
    End Sub

    Private Sub LoadSectorList()
        _sectorList.Items.Clear()
        _stockList.Items.Clear()
        _btnExecute.Enabled = False
        _rightTitle.Text = "선택한 섹터의 종목"
        _lblStatus.Text = "상승률 상위 섹터를 조회하는 중..."
        MessageBus.I.Emit(Topics.SECTOR_LIST_REQUEST, "riseType", "1")
    End Sub

    Private Sub OnSectorListResult(m As Msg)
        If Me.IsDisposed Then Return
        If Me.InvokeRequired Then
            Me.BeginInvoke(Sub() OnSectorListResult(m))
            Return
        End If

        _sectorList.Items.Clear()

        Dim rows = m.DictList("rows")
        If rows Is Nothing OrElse rows.Count = 0 Then
            _lblStatus.Text = "상승률 상위 섹터 정보가 없습니다."
            Return
        End If

        For Each row In rows
            Dim code = GetRowValue(row, "code")
            Dim name = GetRowValue(row, "name")
            Dim lvi As New ListViewItem(code)
            lvi.SubItems.Add(name)
            lvi.SubItems.Add(FormatNumber(row, "changeRate", "F2"))
            lvi.SubItems.Add(FormatNumber(row, "changeRate5d", "F2"))
            lvi.SubItems.Add(FormatNumber(row, "upRatio", "F0"))
            lvi.SubItems.Add(FormatNumber(row, "stockCount", "F0"))
            lvi.Tag = row
            _sectorList.Items.Add(lvi)
        Next

        _lblStatus.Text = $"상승률 상위 섹터 {rows.Count}개 로드됨"

        If _sectorList.Items.Count > 0 Then
            _sectorList.Items(0).Selected = True
            _sectorList.Select()
        End If
    End Sub

    Private Sub OnSectorSelectionChanged(sender As Object, e As EventArgs)
        If _sectorList.SelectedItems.Count = 0 Then
            _stockList.Items.Clear()
            _btnExecute.Enabled = False
            Return
        End If

        Dim item = _sectorList.SelectedItems(0)
        SelectedCode = item.Text
        SelectedName = item.SubItems(1).Text
        _rightTitle.Text = $"{SelectedName} 구성 종목"
        _lblStatus.Text = $"{SelectedName} 종목을 조회하는 중..."
        _stockList.Items.Clear()
        _btnExecute.Enabled = False

        MessageBus.I.Emit(Topics.THEME_STOCKS_REQUEST, "themeCode", SelectedCode)
    End Sub

    Private Sub OnSectorDoubleClick(sender As Object, e As EventArgs)
        If _sectorList.SelectedItems.Count = 0 Then Return
        If _stockList.Items.Count > 0 Then
            ExecuteSelection()
        End If
    End Sub

    Private Sub OnThemeStocksResult(m As Msg)
        If Me.IsDisposed Then Return
        If Me.InvokeRequired Then
            Me.BeginInvoke(Sub() OnThemeStocksResult(m))
            Return
        End If

        Dim themeCode = m.Str("themeCode", "")
        If themeCode <> "" AndAlso SelectedCode <> "" AndAlso Not String.Equals(themeCode, SelectedCode, StringComparison.OrdinalIgnoreCase) Then
            Return
        End If

        _stockList.Items.Clear()

        Dim rows = m.DictList("rows")
        If rows Is Nothing OrElse rows.Count = 0 Then
            _lblStatus.Text = $"{SelectedName} 종목 정보가 없습니다."
            _btnExecute.Enabled = False
            Return
        End If

        For Each row In rows
            Dim lvi As New ListViewItem(GetRowValue(row, "code"))
            lvi.SubItems.Add(GetRowValue(row, "name"))
            lvi.SubItems.Add(FormatNumber(row, "changeRate", "F2"))
            lvi.SubItems.Add(FormatInteger(row, "price"))
            lvi.SubItems.Add(FormatInteger(row, "change"))
            lvi.SubItems.Add(FormatInteger(row, "volume"))
            lvi.Tag = row
            _stockList.Items.Add(lvi)
        Next

        SelectedCodes = rows.
            Select(Function(r) GetRowValue(r, "code")).
            Where(Function(code) code <> "").
            Distinct(StringComparer.OrdinalIgnoreCase).
            ToArray()

        _btnExecute.Enabled = SelectedCodes.Length > 0
        _lblStatus.Text = $"{SelectedName} 종목 {SelectedCodes.Length}개 로드됨"
    End Sub

    Private Sub ExecuteSelection()
        If String.IsNullOrWhiteSpace(SelectedCode) OrElse SelectedCodes Is Nothing OrElse SelectedCodes.Length = 0 Then Return

        AppLogger.I.Info($"상승률 상위 섹터 선택: [{SelectedCode}] {SelectedName} / {SelectedCodes.Length}종목", "Sector")
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Shared Function GetRowValue(row As Dictionary(Of String, String), key As String) As String
        If row Is Nothing OrElse Not row.ContainsKey(key) Then Return ""
        Return If(row(key), "").Trim()
    End Function

    Private Shared Function FormatNumber(row As Dictionary(Of String, String), key As String, format As String) As String
        Dim value As Double
        If Double.TryParse(GetRowValue(row, key), NumberStyles.Any, CultureInfo.InvariantCulture, value) Then
            Return value.ToString(format, CultureInfo.InvariantCulture)
        End If

        If Double.TryParse(GetRowValue(row, key), value) Then
            Return value.ToString(format)
        End If

        Return ""
    End Function

    Private Shared Function FormatInteger(row As Dictionary(Of String, String), key As String) As String
        Dim value As Long
        If Long.TryParse(GetRowValue(row, key), NumberStyles.Any, CultureInfo.InvariantCulture, value) Then
            Return value.ToString("N0", CultureInfo.InvariantCulture)
        End If

        If Long.TryParse(GetRowValue(row, key), value) Then
            Return value.ToString("N0")
        End If

        Return ""
    End Function

End Class
