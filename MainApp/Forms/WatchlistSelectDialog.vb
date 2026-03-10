Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Public Class WatchlistSelectDialog
    Inherits Form

    Private _listView As ListView
    Private _btnSelect As Button
    Private _btnCancel As Button
    Private _btnRefresh As Button
    Private _lblStatus As Label

    Public Property SelectedCodes As String() = Array.Empty(Of String)()
    Public Property SelectedGroupName As String = ""

    Public Sub New()
        Me.Text = "관심종목 선택"
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
        _listView.Columns.Add("그룹명", 200)
        _listView.Columns.Add("종목수", 70)
        _listView.Columns.Add("미리보기", 140)
        AddHandler _listView.DoubleClick, Sub(s, e)
                                              If _listView.SelectedItems.Count > 0 Then ExecuteSelection()
                                          End Sub

        _lblStatus = New Label()
        _lblStatus.Location = New Point(12, 390)
        _lblStatus.Size = New Size(410, 20)
        _lblStatus.Text = "관심종목 그룹을 불러오는 중..."

        _btnRefresh = New Button()
        _btnRefresh.Text = "새로고침"
        _btnRefresh.Location = New Point(12, 415)
        _btnRefresh.Size = New Size(100, 35)
        AddHandler _btnRefresh.Click, Sub(s, e) LoadGroups()

        _btnSelect = New Button()
        _btnSelect.Text = "선택"
        _btnSelect.Location = New Point(210, 415)
        _btnSelect.Size = New Size(100, 35)
        _btnSelect.Enabled = False
        AddHandler _btnSelect.Click, Sub(s, e) ExecuteSelection()

        _btnCancel = New Button()
        _btnCancel.Text = "취소"
        _btnCancel.Location = New Point(322, 415)
        _btnCancel.Size = New Size(100, 35)
        _btnCancel.DialogResult = DialogResult.Cancel

        AddHandler _listView.SelectedIndexChanged, Sub(s, e)
                                                       _btnSelect.Enabled = _listView.SelectedItems.Count > 0
                                                   End Sub

        Me.Controls.AddRange({_listView, _lblStatus, _btnRefresh, _btnSelect, _btnCancel})
        Me.AcceptButton = _btnSelect
        Me.CancelButton = _btnCancel
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        LoadGroups()
    End Sub

    Private Sub LoadGroups()
        _listView.Items.Clear()
        _btnSelect.Enabled = False

        Dim groups = WatchlistService.I.GetGroups()
        If groups Is Nothing OrElse groups.Count = 0 Then
            _lblStatus.Text = "관심종목 그룹이 없습니다."
            Return
        End If

        For Each group In groups
            Dim preview = String.Join(", ", group.Codes.Take(2))
            If group.Codes.Count > 2 Then preview &= "..."

            Dim item As New ListViewItem(group.Name)
            item.SubItems.Add(group.Codes.Count.ToString())
            item.SubItems.Add(preview)
            item.Tag = group
            _listView.Items.Add(item)
        Next

        _lblStatus.Text = $"{groups.Count}개 그룹 로드됨"
    End Sub

    Private Sub ExecuteSelection()
        If _listView.SelectedItems.Count = 0 Then Return

        Dim group = TryCast(_listView.SelectedItems(0).Tag, WatchlistGroup)
        If group Is Nothing OrElse group.Codes Is Nothing OrElse group.Codes.Count = 0 Then
            _lblStatus.Text = "선택한 그룹에 종목이 없습니다."
            Return
        End If

        SelectedGroupName = group.Name
        SelectedCodes = group.Codes.ToArray()
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub
End Class
