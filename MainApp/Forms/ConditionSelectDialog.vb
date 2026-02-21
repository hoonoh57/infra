' ═══════════════════════════════════════════════════════════════
' ConditionSelectDialog.vb — 조건검색 선택 다이얼로그
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Windows.Forms
Imports [Shared]

Public Class ConditionSelectDialog
    Inherits Form

    Private _listView As ListView
    Private _btnExecute As Button
    Private _btnCancel As Button
    Private _btnRefresh As Button
    Private _lblStatus As Label

    Public Property SelectedConditionName As String = ""
    Public Property SelectedConditionIndex As Integer = -1

    Public Sub New()
        Me.Text = "조건검색 선택"
        Me.Size = New Size(450, 500)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False

        InitControls()
    End Sub

    Private Sub InitControls()
        ' ── ListView ──
        _listView = New ListView()
        _listView.View = View.Details
        _listView.FullRowSelect = True
        _listView.GridLines = True
        _listView.MultiSelect = False
        _listView.Location = New Point(12, 12)
        _listView.Size = New Size(410, 370)
        _listView.Columns.Add("인덱스", 60)
        _listView.Columns.Add("조건식명", 330)
        AddHandler _listView.DoubleClick, Sub(s, e)
                                              If _listView.SelectedItems.Count > 0 Then
                                                  ExecuteCondition()
                                              End If
                                          End Sub

        ' ── 상태 ──
        _lblStatus = New Label()
        _lblStatus.Location = New Point(12, 390)
        _lblStatus.Size = New Size(410, 20)
        _lblStatus.Text = "조건식 목록을 불러오는 중..."

        ' ── 버튼 ──
        _btnRefresh = New Button()
        _btnRefresh.Text = "새로고침"
        _btnRefresh.Location = New Point(12, 415)
        _btnRefresh.Size = New Size(100, 35)
        AddHandler _btnRefresh.Click, Sub(s, e) LoadConditions()

        _btnExecute = New Button()
        _btnExecute.Text = "조건 실행"
        _btnExecute.Location = New Point(210, 415)
        _btnExecute.Size = New Size(100, 35)
        _btnExecute.Enabled = False
        AddHandler _btnExecute.Click, Sub(s, e) ExecuteCondition()

        _btnCancel = New Button()
        _btnCancel.Text = "취소"
        _btnCancel.Location = New Point(322, 415)
        _btnCancel.Size = New Size(100, 35)
        _btnCancel.DialogResult = DialogResult.Cancel

        AddHandler _listView.SelectedIndexChanged, Sub(s, e)
                                                       _btnExecute.Enabled = _listView.SelectedItems.Count > 0
                                                   End Sub

        Me.Controls.AddRange({_listView, _lblStatus, _btnRefresh, _btnExecute, _btnCancel})
        Me.AcceptButton = _btnExecute
        Me.CancelButton = _btnCancel
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        LoadConditions()
    End Sub

    Private Sub LoadConditions()
        _listView.Items.Clear()
        _lblStatus.Text = "조건식 목록 조회 중..."
        _btnRefresh.Enabled = False

        ' 키움 조건검색 목록 요청
        MessageBus.I.On(Topics.CONDITION_LIST_RESULT, AddressOf OnConditionListResult)
        MessageBus.I.Emit(Topics.CONDITION_LIST_REQUEST)
    End Sub

    Private Sub OnConditionListResult(m As Msg)
        ' 구독 해제 (1회성)
        MessageBus.I.Off(Topics.CONDITION_LIST_RESULT, AddressOf OnConditionListResult)

        If Me.InvokeRequired Then
            Me.BeginInvoke(Sub() OnConditionListResult(m))
            Return
        End If

        _btnRefresh.Enabled = True

        If Not m.Bool("success") Then
            _lblStatus.Text = $"조건식 목록 실패: {m.Str("message")}"
            Return
        End If

        ' 조건식 목록은 List(Of Dictionary) 또는 직접 파싱
        Dim conditions = m.DictList("conditions")
        If conditions Is Nothing OrElse conditions.Count = 0 Then
            ' 대안: rows 키
            conditions = m.DictList("rows")
        End If

        If conditions Is Nothing OrElse conditions.Count = 0 Then
            _lblStatus.Text = "조건식이 없습니다."
            Return
        End If

        For Each cond In conditions
            Dim idx = If(cond.ContainsKey("index"), cond("index")?.ToString(), "0")
            Dim name = If(cond.ContainsKey("name"), cond("name")?.ToString(), "")
            Dim lvi As New ListViewItem(idx)
            lvi.SubItems.Add(name)
            lvi.Tag = New With {.Index = Integer.Parse(idx), .Name = name}
            _listView.Items.Add(lvi)
        Next

        _lblStatus.Text = $"조건식 {conditions.Count}개 로드됨"
    End Sub

    Private Sub ExecuteCondition()
        If _listView.SelectedItems.Count = 0 Then Return

        Dim tag = _listView.SelectedItems(0).Tag
        SelectedConditionIndex = CInt(CallByName(tag, "Index", CallType.Get))
        SelectedConditionName = CStr(CallByName(tag, "Name", CallType.Get))

        AppLogger.I.Info($"조건식 선택: [{SelectedConditionIndex}] {SelectedConditionName}", "Condition")
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

End Class
