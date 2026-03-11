Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Public Class WatchlistSelectDialog
    Inherits Form

    Private ReadOnly _split As SplitContainer
    Private ReadOnly _groupList As ListView
    Private ReadOnly _stockList As ListView
    Private ReadOnly _lblGroupTitle As Label
    Private ReadOnly _lblStockTitle As Label
    Private ReadOnly _lblStatus As Label
    Private ReadOnly _txtCode As TextBox
    Private ReadOnly _txtComment As TextBox
    Private ReadOnly _btnNewGroup As Button
    Private ReadOnly _btnRenameGroup As Button
    Private ReadOnly _btnDeleteGroup As Button
    Private ReadOnly _btnSaveStock As Button
    Private ReadOnly _btnDeleteStock As Button
    Private ReadOnly _btnClearStock As Button
    Private ReadOnly _btnRefresh As Button
    Private ReadOnly _btnSelect As Button
    Private ReadOnly _btnCancel As Button

    Public Property SelectedCodes As String() = Array.Empty(Of String)()
    Public Property SelectedGroupName As String = ""

    Public Sub New()
        Me.Text = "관심종목 선택"
        Me.Size = New Size(900, 620)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False

        _split = New SplitContainer() With {
            .Dock = DockStyle.Top,
            .Location = New Point(12, 12),
            .Size = New Size(860, 500),
            .SplitterDistance = 380,
            .IsSplitterFixed = False
        }

        _lblGroupTitle = New Label() With {
            .Text = "관심종목 그룹",
            .Dock = DockStyle.Top,
            .Height = 24
        }

        _groupList = New ListView() With {
            .Dock = DockStyle.Fill,
            .View = View.Details,
            .FullRowSelect = True,
            .GridLines = True,
            .HideSelection = False,
            .MultiSelect = False
        }
        _groupList.Columns.Add("그룹명", 170)
        _groupList.Columns.Add("종목수", 70, HorizontalAlignment.Right)
        _groupList.Columns.Add("미리보기", 120)

        _btnNewGroup = New Button() With {.Text = "그룹추가", .Size = New Size(100, 32)}
        _btnRenameGroup = New Button() With {.Text = "그룹명변경", .Size = New Size(100, 32)}
        _btnDeleteGroup = New Button() With {.Text = "그룹삭제", .Size = New Size(100, 32)}

        Dim leftButtons As New FlowLayoutPanel() With {
            .Dock = DockStyle.Bottom,
            .Height = 42,
            .FlowDirection = FlowDirection.LeftToRight,
            .WrapContents = False
        }
        leftButtons.Controls.AddRange({_btnNewGroup, _btnRenameGroup, _btnDeleteGroup})

        Dim leftPanel As New Panel() With {.Dock = DockStyle.Fill}
        leftPanel.Controls.Add(_groupList)
        leftPanel.Controls.Add(leftButtons)
        leftPanel.Controls.Add(_lblGroupTitle)
        _split.Panel1.Controls.Add(leftPanel)

        _lblStockTitle = New Label() With {
            .Text = "그룹 종목 편집",
            .Dock = DockStyle.Top,
            .Height = 24
        }

        _stockList = New ListView() With {
            .Dock = DockStyle.Top,
            .Height = 280,
            .View = View.Details,
            .FullRowSelect = True,
            .GridLines = True,
            .HideSelection = False,
            .MultiSelect = False
        }
        _stockList.Columns.Add("종목코드", 90)
        _stockList.Columns.Add("코멘트", 350)

        Dim editorPanel As New Panel() With {
            .Dock = DockStyle.Fill,
            .Padding = New Padding(0, 8, 0, 0)
        }

        Dim lblCode As New Label() With {.Text = "종목코드", .Location = New Point(0, 8), .Size = New Size(70, 20)}
        _txtCode = New TextBox() With {.Location = New Point(80, 4), .Size = New Size(140, 27)}

        Dim lblComment As New Label() With {.Text = "코멘트", .Location = New Point(0, 42), .Size = New Size(70, 20)}
        _txtComment = New TextBox() With {
            .Location = New Point(80, 40),
            .Size = New Size(370, 90),
            .Multiline = True,
            .ScrollBars = ScrollBars.Vertical
        }

        _btnSaveStock = New Button() With {.Text = "종목저장", .Location = New Point(80, 142), .Size = New Size(100, 32)}
        _btnDeleteStock = New Button() With {.Text = "종목삭제", .Location = New Point(188, 142), .Size = New Size(100, 32)}
        _btnClearStock = New Button() With {.Text = "입력초기화", .Location = New Point(296, 142), .Size = New Size(100, 32)}

        editorPanel.Controls.AddRange({lblCode, _txtCode, lblComment, _txtComment, _btnSaveStock, _btnDeleteStock, _btnClearStock})

        Dim rightPanel As New Panel() With {.Dock = DockStyle.Fill}
        rightPanel.Controls.Add(editorPanel)
        rightPanel.Controls.Add(_stockList)
        rightPanel.Controls.Add(_lblStockTitle)
        _split.Panel2.Controls.Add(rightPanel)

        _lblStatus = New Label() With {
            .Location = New Point(12, 522),
            .Size = New Size(860, 20),
            .Text = "관심종목 그룹을 불러오는 중..."
        }

        _btnRefresh = New Button() With {.Text = "새로고침", .Location = New Point(12, 550), .Size = New Size(100, 35)}
        _btnSelect = New Button() With {.Text = "선택", .Location = New Point(662, 550), .Size = New Size(100, 35), .Enabled = False}
        _btnCancel = New Button() With {.Text = "취소", .Location = New Point(772, 550), .Size = New Size(100, 35), .DialogResult = DialogResult.Cancel}

        AddHandler _groupList.SelectedIndexChanged, AddressOf OnGroupSelectionChanged
        AddHandler _groupList.DoubleClick, Sub(sender, e) ExecuteSelection()
        AddHandler _stockList.SelectedIndexChanged, AddressOf OnStockSelectionChanged
        AddHandler _stockList.DoubleClick, Sub(sender, e) CopySelectedStockToEditor()
        AddHandler _btnNewGroup.Click, AddressOf OnNewGroupClicked
        AddHandler _btnRenameGroup.Click, AddressOf OnRenameGroupClicked
        AddHandler _btnDeleteGroup.Click, AddressOf OnDeleteGroupClicked
        AddHandler _btnSaveStock.Click, AddressOf OnSaveStockClicked
        AddHandler _btnDeleteStock.Click, AddressOf OnDeleteStockClicked
        AddHandler _btnClearStock.Click, Sub(sender, e) ClearStockEditor()
        AddHandler _btnRefresh.Click, Sub(sender, e) LoadGroups(SelectedGroupName)
        AddHandler _btnSelect.Click, Sub(sender, e) ExecuteSelection()

        Me.Controls.Add(_split)
        Me.Controls.Add(_lblStatus)
        Me.Controls.Add(_btnRefresh)
        Me.Controls.Add(_btnSelect)
        Me.Controls.Add(_btnCancel)
        Me.AcceptButton = _btnSelect
        Me.CancelButton = _btnCancel
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        LoadGroups()
    End Sub

    Private Sub LoadGroups(Optional groupNameToSelect As String = "")
        _groupList.Items.Clear()
        _stockList.Items.Clear()
        ClearStockEditor()
        _btnSelect.Enabled = False

        Dim groups = WatchlistService.I.GetGroups()
        If groups Is Nothing OrElse groups.Count = 0 Then
            SelectedGroupName = ""
            _lblStockTitle.Text = "그룹 종목 편집"
            _lblStatus.Text = "관심종목 그룹이 없습니다."
            Return
        End If

        For Each group In groups
            Dim preview = String.Join(", ", group.Stocks.Select(Function(s) s.Code).Take(2))
            If group.Stocks.Count > 2 Then preview &= "..."

            Dim item As New ListViewItem(group.Name)
            item.SubItems.Add(group.Stocks.Count.ToString())
            item.SubItems.Add(preview)
            item.Tag = group
            _groupList.Items.Add(item)
        Next

        Dim itemToSelect = _groupList.Items.Cast(Of ListViewItem)().
            FirstOrDefault(Function(item) String.Equals(item.Text, groupNameToSelect, StringComparison.OrdinalIgnoreCase))
        If itemToSelect Is Nothing Then itemToSelect = _groupList.Items(0)
        itemToSelect.Selected = True
        itemToSelect.Focused = True
        itemToSelect.EnsureVisible()

        _lblStatus.Text = $"{groups.Count}개 그룹 로드됨"
    End Sub

    Private Sub OnGroupSelectionChanged(sender As Object, e As EventArgs)
        If _groupList.SelectedItems.Count = 0 Then
            SelectedGroupName = ""
            _stockList.Items.Clear()
            _lblStockTitle.Text = "그룹 종목 편집"
            _btnSelect.Enabled = False
            ClearStockEditor()
            Return
        End If

        Dim group = TryCast(_groupList.SelectedItems(0).Tag, WatchlistGroup)
        If group Is Nothing Then Return

        SelectedGroupName = group.Name
        _lblStockTitle.Text = $"그룹 종목 편집 - {group.Name}"
        RefreshStocks(group)
        ClearStockEditor()
    End Sub

    Private Sub RefreshStocks(group As WatchlistGroup, Optional codeToSelect As String = "")
        _stockList.Items.Clear()

        For Each stock In group.Stocks
            Dim item As New ListViewItem(stock.Code)
            item.SubItems.Add(stock.Comment)
            item.Tag = stock
            _stockList.Items.Add(item)
        Next

        If _stockList.Items.Count > 0 Then
            Dim selectedItem = _stockList.Items.Cast(Of ListViewItem)().
                FirstOrDefault(Function(item) String.Equals(item.Text, codeToSelect, StringComparison.OrdinalIgnoreCase))
            If selectedItem IsNot Nothing Then
                selectedItem.Selected = True
                selectedItem.Focused = True
                selectedItem.EnsureVisible()
            End If
        End If

        _btnSelect.Enabled = group.Stocks.Count > 0
        _lblStatus.Text = $"{group.Name} 그룹: {group.Stocks.Count}종목"
    End Sub

    Private Sub OnStockSelectionChanged(sender As Object, e As EventArgs)
        CopySelectedStockToEditor()
    End Sub

    Private Sub CopySelectedStockToEditor()
        If _stockList.SelectedItems.Count = 0 Then Return

        Dim stock = TryCast(_stockList.SelectedItems(0).Tag, WatchlistStock)
        If stock Is Nothing Then Return

        _txtCode.Text = stock.Code
        _txtComment.Text = stock.Comment
    End Sub

    Private Sub OnNewGroupClicked(sender As Object, e As EventArgs)
        Dim name = InputBox("신규 그룹명을 입력하세요.", "그룹 추가", "")
        If String.IsNullOrWhiteSpace(name) Then Return

        Try
            Dim group = WatchlistService.I.CreateGroup(name)
            LoadGroups(group.Name)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "그룹 추가", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub OnRenameGroupClicked(sender As Object, e As EventArgs)
        If _groupList.SelectedItems.Count = 0 Then
            MessageBox.Show(Me, "수정할 그룹을 먼저 선택하세요.", "그룹명 변경", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim oldName = _groupList.SelectedItems(0).Text
        Dim newName = InputBox("변경할 그룹명을 입력하세요.", "그룹명 변경", oldName)
        If String.IsNullOrWhiteSpace(newName) Then Return

        Try
            WatchlistService.I.RenameGroup(oldName, newName)
            LoadGroups(newName)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "그룹명 변경", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub OnDeleteGroupClicked(sender As Object, e As EventArgs)
        If _groupList.SelectedItems.Count = 0 Then
            MessageBox.Show(Me, "삭제할 그룹을 먼저 선택하세요.", "그룹 삭제", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim groupName = _groupList.SelectedItems(0).Text
        Dim confirm = MessageBox.Show(Me, $"[{groupName}] 그룹을 삭제하시겠습니까?", "그룹 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        If confirm <> DialogResult.Yes Then Return

        WatchlistService.I.DeleteGroup(groupName)
        LoadGroups()
    End Sub

    Private Sub OnSaveStockClicked(sender As Object, e As EventArgs)
        If String.IsNullOrWhiteSpace(SelectedGroupName) Then
            MessageBox.Show(Me, "먼저 그룹을 선택하거나 생성하세요.", "종목 저장", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Try
            WatchlistService.I.UpsertStock(SelectedGroupName, _txtCode.Text, _txtComment.Text)
            Dim code = _txtCode.Text.Trim()
            LoadGroups(SelectedGroupName)
            SelectStock(code)
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "종목 저장", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub OnDeleteStockClicked(sender As Object, e As EventArgs)
        If String.IsNullOrWhiteSpace(SelectedGroupName) Then
            MessageBox.Show(Me, "먼저 그룹을 선택하세요.", "종목 삭제", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim code = _txtCode.Text.Trim()
        If code = "" AndAlso _stockList.SelectedItems.Count > 0 Then
            code = _stockList.SelectedItems(0).Text
        End If
        If code = "" Then
            MessageBox.Show(Me, "삭제할 종목을 선택하세요.", "종목 삭제", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        WatchlistService.I.RemoveStock(SelectedGroupName, code)
        LoadGroups(SelectedGroupName)
    End Sub

    Private Sub SelectStock(code As String)
        If code = "" Then Return

        Dim selectedItem = _groupList.SelectedItems.Cast(Of ListViewItem)().FirstOrDefault()
        Dim selectedGroup = If(selectedItem IsNot Nothing, TryCast(selectedItem.Tag, WatchlistGroup), Nothing)
        If selectedGroup Is Nothing Then Return

        RefreshStocks(selectedGroup, code)
        Dim target = _stockList.Items.Cast(Of ListViewItem)().
            FirstOrDefault(Function(item) String.Equals(item.Text, code, StringComparison.OrdinalIgnoreCase))
        If target IsNot Nothing Then
            target.Selected = True
            target.Focused = True
            target.EnsureVisible()
            CopySelectedStockToEditor()
        End If
    End Sub

    Private Sub ClearStockEditor()
        _txtCode.Text = ""
        _txtComment.Text = ""
    End Sub

    Private Sub ExecuteSelection()
        If _groupList.SelectedItems.Count = 0 Then Return

        Dim group = TryCast(_groupList.SelectedItems(0).Tag, WatchlistGroup)
        If group Is Nothing OrElse group.Stocks Is Nothing OrElse group.Stocks.Count = 0 Then
            _lblStatus.Text = "선택한 그룹에 종목이 없습니다."
            Return
        End If

        SelectedGroupName = group.Name
        SelectedCodes = group.Stocks.Select(Function(s) s.Code).Where(Function(code) code <> "").ToArray()
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub
End Class
