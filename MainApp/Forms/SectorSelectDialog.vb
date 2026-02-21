' ═══════════════════════════════════════════════════════════════
' SectorSelectDialog.vb — 섹터/테마 선택 다이얼로그
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports [Shared]

Public Class SectorSelectDialog
    Inherits Form

    Public Enum SectorMode
        주도섹터 = 0
        테마 = 1
    End Enum

    Private _listView As ListView
    Private _btnExecute As Button
    Private _btnCancel As Button
    Private _lblStatus As Label
    Private _mode As SectorMode

    Public Property SelectedCode As String = ""
    Public Property SelectedName As String = ""

    Public Sub New(mode As SectorMode)
        _mode = mode
        Me.Text = If(mode = SectorMode.주도섹터, "주도섹터 선택", "테마 선택")
        Me.Size = New Size(450, 500)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False

        InitControls()
    End Sub

    Private Sub InitControls()
        _listView = New ListView()
        _listView.View = View.Details
        _listView.FullRowSelect = True
        _listView.GridLines = True
        _listView.MultiSelect = False
        _listView.Location = New Point(12, 12)
        _listView.Size = New Size(410, 370)
        _listView.Columns.Add("코드", 80)
        _listView.Columns.Add("이름", 310)
        AddHandler _listView.DoubleClick, Sub(s, e)
                                              If _listView.SelectedItems.Count > 0 Then Execute()
                                          End Sub

        _lblStatus = New Label()
        _lblStatus.Location = New Point(12, 390)
        _lblStatus.Size = New Size(410, 20)
        _lblStatus.Text = "목록을 불러오는 중..."

        _btnExecute = New Button()
        _btnExecute.Text = "선택"
        _btnExecute.Location = New Point(210, 415)
        _btnExecute.Size = New Size(100, 35)
        _btnExecute.Enabled = False
        AddHandler _btnExecute.Click, Sub(s, e) Execute()

        _btnCancel = New Button()
        _btnCancel.Text = "취소"
        _btnCancel.Location = New Point(322, 415)
        _btnCancel.Size = New Size(100, 35)
        _btnCancel.DialogResult = DialogResult.Cancel

        AddHandler _listView.SelectedIndexChanged, Sub(s, e)
                                                       _btnExecute.Enabled = _listView.SelectedItems.Count > 0
                                                   End Sub

        Me.Controls.AddRange({_listView, _lblStatus, _btnExecute, _btnCancel})
        Me.AcceptButton = _btnExecute
        Me.CancelButton = _btnCancel
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        LoadList()
    End Sub

    Private Sub LoadList()
        _listView.Items.Clear()
        _lblStatus.Text = "목록 조회 중..."

        Dim resultTopic = Topics.SECTOR_LIST_RESULT

        MessageBus.I.On(resultTopic, AddressOf OnListResult)

        If _mode = SectorMode.주도섹터 Then
            MessageBus.I.Emit(Topics.SECTOR_LIST_REQUEST)
        Else
            ' 테마는 별도 요청
            MessageBus.I.Emit(Topics.THEME_STOCKS_REQUEST, "themeCode", "")
        End If
    End Sub

    Private Sub OnListResult(m As Msg)
        MessageBus.I.Off(Topics.SECTOR_LIST_RESULT, AddressOf OnListResult)

        If Me.InvokeRequired Then
            Me.BeginInvoke(Sub() OnListResult(m))
            Return
        End If

        Dim rows = m.DictList("rows")
        If rows Is Nothing OrElse rows.Count = 0 Then
            _lblStatus.Text = "목록이 없습니다."
            Return
        End If

        For Each row In rows
            Dim code = If(row.ContainsKey("code"), row("code")?.ToString(), "")
            Dim name = If(row.ContainsKey("name"), row("name")?.ToString(), "")
            Dim lvi As New ListViewItem(code)
            lvi.SubItems.Add(name)
            _listView.Items.Add(lvi)
        Next

        _lblStatus.Text = $"{rows.Count}개 항목 로드됨"
    End Sub

    Private Sub Execute()
        If _listView.SelectedItems.Count = 0 Then Return

        SelectedCode = _listView.SelectedItems(0).Text
        SelectedName = _listView.SelectedItems(0).SubItems(1).Text

        AppLogger.I.Info($"섹터/테마 선택: [{SelectedCode}] {SelectedName}", "Sector")
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

End Class
