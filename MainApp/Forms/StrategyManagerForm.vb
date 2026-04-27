Imports System.Drawing
Imports System.Windows.Forms
Imports System.Linq
Imports Microsoft.VisualBasic
Imports MainApp.Models
Imports MainApp.Services

Public Class StrategyManagerForm
    Inherits Form

    Private _tvStrategies As TreeView
    Private _tvLogic As TreeView
    Private _rtbChat As RichTextBox
    Private _txtPrompt As TextBox
    Private _btnApply As Button

    Private _store As StrategyStore
    Private _onApply As Action(Of StrategyDefinition)
    Private _onApplyHardcoded As Action(Of IStrategy)
    Private _selectedStrategyId As String = ""

    ' 하드코딩 IStrategy 목록
    Private ReadOnly _builtInStrategies As New List(Of IStrategy) From {
        New ZeroLossChartStrategy()
    }

    Public Sub New(Optional onApply As Action(Of StrategyDefinition) = Nothing,
                   Optional onApplyHardcoded As Action(Of IStrategy) = Nothing)
        _onApply = onApply
        _onApplyHardcoded = onApplyHardcoded
        _store = StrategyPersistenceService.LoadStore()
        AddFactoryBuiltInStrategies()
        EnsureDefaultData()
        InitializeUI()
        RefreshStrategyTree()
    End Sub

    Private Sub AddFactoryBuiltInStrategies()
        Dim strategies As List(Of IStrategy) = BuiltInStrategyFactory.GetAllStrategies()
        If strategies Is Nothing Then Return

        For Each strategy As IStrategy In strategies
            If strategy Is Nothing Then Continue For
            Dim exists As Boolean = _builtInStrategies.Any(Function(item As IStrategy) String.Equals(item.Name, strategy.Name, StringComparison.OrdinalIgnoreCase))
            If Not exists Then
                _builtInStrategies.Add(strategy)
            End If
        Next
    End Sub

    Private ReadOnly Property AllStrategies As List(Of StrategyDefinition)
        Get
            Return _store.Strategies
        End Get
    End Property

    Private ReadOnly Property AllGroups As List(Of StrategyGroup)
        Get
            Return _store.Groups
        End Get
    End Property

    Private Sub EnsureDefaultData()
        If AllGroups Is Nothing Then _store.Groups = New List(Of StrategyGroup)()
        If AllStrategies Is Nothing Then _store.Strategies = New List(Of StrategyDefinition)()

        If AllGroups.Count = 0 Then
            AllGroups.Add(New StrategyGroup With {
                .GroupId = "default",
                .GroupName = "기본 그룹",
                .Description = "Default strategy group",
                .DisplayOrder = 0
            })
        End If

        If AllStrategies.Count = 0 Then
            Dim s As New StrategyDefinition("기본 돌파 전략", "슈퍼트렌드 돌파 기본", New List(Of LogicGate), New List(Of LogicGate), "")
            s.GroupId = AllGroups(0).GroupId
            s.BuyRules = New List(Of LogicGate) From {
                New LogicGate("Entry", LogicalOperator.AND, New List(Of ConditionCell) From {
                    New ConditionCell("C1", "Price CrossUp SuperTrend", "Price", ComparisonOperator.CrossUp, "SuperTrend")
                })
            }
            s.SellRules = New List(Of LogicGate) From {
                New LogicGate("Exit", LogicalOperator.OR, New List(Of ConditionCell) From {
                    New ConditionCell("C2", "Price CrossDown SuperTrend", "Price", ComparisonOperator.CrossDown, "SuperTrend")
                })
            }
            AllStrategies.Add(s)
        End If

        SaveAll()
    End Sub

    Private Sub InitializeUI()
        Me.Text = "전략 관리자 (Group / Clone / Delete)"
        Me.Size = New Size(1080, 760)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.BackColor = Color.FromArgb(28, 28, 38)
        Me.ForeColor = Color.White

        Dim mainSplit As New SplitContainer With {.Dock = DockStyle.Fill, .Orientation = Orientation.Vertical, .FixedPanel = FixedPanel.Panel1}
        Dim rightSplit As New SplitContainer With {.Dock = DockStyle.Fill, .Orientation = Orientation.Horizontal}

        Dim leftPanel As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
        _tvStrategies = New TreeView With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(40, 40, 50),
            .ForeColor = Color.White,
            .BorderStyle = BorderStyle.None,
            .HideSelection = False,
            .Font = New Font("맑은 고딕", 10)
        }
        AddHandler _tvStrategies.AfterSelect, AddressOf OnStrategyTreeSelect

        Dim toolRow As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 78, .FlowDirection = FlowDirection.LeftToRight}
        Dim btnAddGroup As New Button With {.Text = "새 그룹", .Width = 95, .Height = 32}
        Dim btnAddStrat As New Button With {.Text = "새 전략", .Width = 95, .Height = 32}
        Dim btnClone As New Button With {.Text = "복제", .Width = 95, .Height = 32}
        Dim btnDelete As New Button With {.Text = "삭제", .Width = 95, .Height = 32}
        AddHandler btnAddGroup.Click, Sub() AddGroup()
        AddHandler btnAddStrat.Click, Sub() AddStrategy()
        AddHandler btnClone.Click, Sub() CloneStrategy()
        AddHandler btnDelete.Click, Sub() DeleteSelected()
        toolRow.Controls.AddRange({btnAddGroup, btnAddStrat, btnClone, btnDelete})

        leftPanel.Controls.Add(_tvStrategies)
        leftPanel.Controls.Add(toolRow)
        mainSplit.Panel1.Controls.Add(leftPanel)

        Dim logicPanel As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
        _tvLogic = New TreeView With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(30, 30, 40),
            .ForeColor = Color.Lime,
            .BorderStyle = BorderStyle.None,
            .Font = New Font("Consolas", 10)
        }
        logicPanel.Controls.Add(New Label With {.Text = "전략 논리 구조 (검증용 Transparency View)", .Dock = DockStyle.Top, .Height = 24, .ForeColor = Color.Cyan})
        logicPanel.Controls.Add(_tvLogic)
        rightSplit.Panel1.Controls.Add(logicPanel)

        Dim chatPanel As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
        _rtbChat = New RichTextBox With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(20, 20, 30),
            .ForeColor = Color.FromArgb(200, 200, 200),
            .ReadOnly = True,
            .BorderStyle = BorderStyle.None
        }

        Dim promptContainer As New Panel With {.Dock = DockStyle.Bottom, .Height = 84, .Padding = New Padding(0, 6, 0, 0)}
        _txtPrompt = New TextBox With {.Dock = DockStyle.Fill, .Multiline = True, .BackColor = Color.FromArgb(45, 45, 55), .ForeColor = Color.White, .BorderStyle = BorderStyle.FixedSingle}
        Dim btnSend As New Button With {.Text = "AI 설계 요청", .Dock = DockStyle.Right, .Width = 120, .BackColor = Color.FromArgb(60, 100, 60), .FlatStyle = FlatStyle.Flat}
        AddHandler btnSend.Click, AddressOf RequestAIDesign
        promptContainer.Controls.Add(_txtPrompt)
        promptContainer.Controls.Add(btnSend)

        chatPanel.Controls.Add(_rtbChat)
        chatPanel.Controls.Add(promptContainer)
        chatPanel.Controls.Add(New Label With {.Text = "AI 전략 비서", .Dock = DockStyle.Top, .Height = 24, .ForeColor = Color.Orange})
        rightSplit.Panel2.Controls.Add(chatPanel)

        Dim bottomPanel As New Panel With {.Dock = DockStyle.Bottom, .Height = 48, .Padding = New Padding(10)}
        _btnApply = New Button With {.Text = "차트 적용", .Dock = DockStyle.Right, .Width = 140, .FlatStyle = FlatStyle.Flat, .BackColor = Color.FromArgb(100, 60, 100)}
        AddHandler _btnApply.Click, AddressOf ApplySelected
        bottomPanel.Controls.Add(_btnApply)

        mainSplit.Panel2.Controls.Add(rightSplit)
        Me.Controls.Add(mainSplit)
        Me.Controls.Add(bottomPanel)

        AddHandler Me.Load, Sub()
                                Try
                                    mainSplit.SplitterDistance = 280
                                    rightSplit.SplitterDistance = Me.Height - 330
                                Catch
                                End Try
                            End Sub

        LogChat("AI: 전략 그룹을 만든 뒤 전략을 복제/수정하며 파생 전략을 빠르게 실험하세요.")
    End Sub

    Private Sub RefreshStrategyTree()
        Dim selectedId = _selectedStrategyId
        _tvStrategies.Nodes.Clear()

        Dim groupList = AllGroups.OrderBy(Function(g) g.DisplayOrder).ThenBy(Function(g) g.GroupName).ToList()
        For Each g In groupList
            Dim gNode = _tvStrategies.Nodes.Add(g.GroupName)
            gNode.Tag = g
            gNode.ForeColor = Color.DeepSkyBlue

            Dim stratList = AllStrategies.Where(Function(s) s.GroupId = g.GroupId).
                OrderBy(Function(s) s.DisplayOrder).
                ThenBy(Function(s) s.Name).ToList()
            For Each s In stratList
                Dim label = If(s.Version > 1, $"{s.Name} (v{s.Version})", s.Name)
                Dim sNode = gNode.Nodes.Add(label)
                sNode.Tag = s
                If s.StrategyId = selectedId Then
                    _tvStrategies.SelectedNode = sNode
                End If
            Next
            gNode.Expand()
        Next

        ' ── 하드코딩 전략 그룹 ──
        If _builtInStrategies.Count > 0 Then
            Dim builtInNode = _tvStrategies.Nodes.Add("하드코딩 전략")
            builtInNode.ForeColor = Color.Orange
            For Each strat In _builtInStrategies
                Dim sNode = builtInNode.Nodes.Add(strat.DisplayName)
                sNode.Tag = strat  ' IStrategy 객체 직접 저장
                sNode.ForeColor = Color.Gold
            Next
            builtInNode.Expand()
        End If

        If _tvStrategies.SelectedNode Is Nothing AndAlso _tvStrategies.Nodes.Count > 0 Then
            Dim firstGroup = _tvStrategies.Nodes(0)
            If firstGroup.Nodes.Count > 0 Then
                _tvStrategies.SelectedNode = firstGroup.Nodes(0)
            Else
                _tvStrategies.SelectedNode = firstGroup
            End If
        End If
    End Sub

    Private Sub OnStrategyTreeSelect(sender As Object, e As TreeViewEventArgs)
        ShowSelectedItem()
    End Sub

    Private Sub ShowSelectedItem()
        _tvLogic.Nodes.Clear()
        Dim node = _tvStrategies.SelectedNode
        If node Is Nothing Then Return

        ' ── 하드코딩 IStrategy 선택 ──
        Dim hardcoded = TryCast(node.Tag, IStrategy)
        If hardcoded IsNot Nothing Then
            _txtPrompt.Text = ""
            Dim root = _tvLogic.Nodes.Add(hardcoded.DisplayName)
            root.Nodes.Add($"Type: 하드코딩 (IStrategy)")
            root.Nodes.Add($"Name: {hardcoded.Name}")
            Dim reqInd = hardcoded.RequiredIndicators()
            root.Nodes.Add($"Required Indicators: {If(reqInd.Count = 0, "없음 (OHLCV만 사용)", String.Join(", ", reqInd))}")
            root.ExpandAll()
            Return
        End If

        Dim s = TryCast(node.Tag, StrategyDefinition)
        If s Is Nothing Then
            Dim g = TryCast(node.Tag, StrategyGroup)
            If g IsNot Nothing Then
                _txtPrompt.Text = ""
                Dim root = _tvLogic.Nodes.Add($"Group: {g.GroupName}")
                root.Nodes.Add($"Strategies: {AllStrategies.Where(Function(x) x.GroupId = g.GroupId).Count()}")
                root.ExpandAll()
            End If
            Return
        End If

        _selectedStrategyId = s.StrategyId
        _txtPrompt.Text = If(s.NaturalLanguagePrompt, "")

        Dim rootNode = _tvLogic.Nodes.Add(s.Name)
        rootNode.Nodes.Add($"Description: {s.Description}")
        rootNode.Nodes.Add($"Mode: {s.Mode}, Active: {s.IsActive}")

        Dim buyNode = rootNode.Nodes.Add("BUY Rules")
        For Each gate In s.BuyRules
            Dim gNode = buyNode.Nodes.Add($"[{gate.Operator}] {gate.Name}")
            gNode.Tag = gate
            For Each cond In gate.Conditions
                Dim cNode = gNode.Nodes.Add(FormatCondition(cond))
                cNode.Tag = cond
            Next
        Next

        Dim sellNode = rootNode.Nodes.Add("SELL Rules")
        For Each gate In s.SellRules
            Dim gNode = sellNode.Nodes.Add($"[{gate.Operator}] {gate.Name}")
            gNode.Tag = gate
            For Each cond In gate.Conditions
                Dim cNode = gNode.Nodes.Add(FormatCondition(cond))
                cNode.Tag = cond
            Next
        Next

        _tvLogic.ExpandAll()
    End Sub

    Private Shared Function FormatCondition(cond As ConditionCell) As String
        If cond Is Nothing Then Return "(null)"
        Dim rhs = If(Not String.IsNullOrWhiteSpace(cond.IndicatorB), cond.IndicatorB, If(cond.ConstantValue.HasValue, cond.ConstantValue.Value.ToString(), "null"))
        Dim inv = If(cond.IsInverted, "NOT ", "")
        Return $"{inv}{cond.IndicatorA} {cond.Operator} {rhs} (lb:{cond.Lookback}, off:{cond.Offset})"
    End Function

    Private Function CurrentGroupId() As String
        Dim node = _tvStrategies.SelectedNode
        If node Is Nothing Then Return AllGroups(0).GroupId

        Dim g = TryCast(node.Tag, StrategyGroup)
        If g IsNot Nothing Then Return g.GroupId

        Dim s = TryCast(node.Tag, StrategyDefinition)
        If s IsNot Nothing Then Return s.GroupId

        Return AllGroups(0).GroupId
    End Function

    Private Sub AddGroup()
        Dim name = Interaction.InputBox("새 그룹명을 입력하세요.", "새 그룹", "New Group")
        name = If(name, "").Trim()
        If name = "" Then Return

        If AllGroups.Any(Function(g) String.Equals(g.GroupName, name, StringComparison.OrdinalIgnoreCase)) Then
            MessageBox.Show("같은 이름의 그룹이 있습니다.")
            Return
        End If

        AllGroups.Add(New StrategyGroup With {
            .GroupName = name,
            .DisplayOrder = AllGroups.Count + 1
        })
        SaveAll()
        RefreshStrategyTree()
    End Sub

    Private Sub AddStrategy()
        Dim groupId = CurrentGroupId()
        Dim name = Interaction.InputBox("새 전략명을 입력하세요.", "새 전략", "New Strategy")
        name = If(name, "").Trim()
        If name = "" Then Return

        Dim s As New StrategyDefinition(name, "New strategy", New List(Of LogicGate), New List(Of LogicGate), "")
        s.GroupId = groupId
        s.DisplayOrder = AllStrategies.Count + 1
        s.BuyRules = New List(Of LogicGate) From {New LogicGate("Entry", LogicalOperator.AND, New List(Of ConditionCell))}
        s.SellRules = New List(Of LogicGate) From {New LogicGate("Exit", LogicalOperator.OR, New List(Of ConditionCell))}

        AllStrategies.Add(s)
        _selectedStrategyId = s.StrategyId
        SaveAll()
        RefreshStrategyTree()
    End Sub

    Private Sub CloneStrategy()
        Dim node = _tvStrategies.SelectedNode
        If node Is Nothing Then Return
        Dim src = TryCast(node.Tag, StrategyDefinition)
        If src Is Nothing Then Return

        Dim clone = DeepCloneStrategy(src)
        clone.StrategyId = Guid.NewGuid().ToString("N")
        clone.BaseStrategyId = src.StrategyId
        clone.Version = Math.Max(1, src.Version) + 1
        clone.Name = src.Name & "_copy"
        clone.DisplayOrder = AllStrategies.Count + 1

        AllStrategies.Add(clone)
        _selectedStrategyId = clone.StrategyId
        SaveAll()
        RefreshStrategyTree()
    End Sub

    Private Sub DeleteSelected()
        Dim node = _tvStrategies.SelectedNode
        If node Is Nothing Then Return

        Dim s = TryCast(node.Tag, StrategyDefinition)
        If s IsNot Nothing Then
            If MessageBox.Show($"전략 '{s.Name}'을(를) 삭제할까요?", "삭제", MessageBoxButtons.YesNo) <> DialogResult.Yes Then Return
            AllStrategies.RemoveAll(Function(x) x.StrategyId = s.StrategyId)
            _selectedStrategyId = ""
            SaveAll()
            RefreshStrategyTree()
            Return
        End If

        Dim g = TryCast(node.Tag, StrategyGroup)
        If g IsNot Nothing Then
            Dim cnt = AllStrategies.Where(Function(x) x.GroupId = g.GroupId).Count()
            If MessageBox.Show($"그룹 '{g.GroupName}'과 하위 전략 {cnt}개를 삭제할까요?", "그룹 삭제", MessageBoxButtons.YesNo) <> DialogResult.Yes Then Return
            AllStrategies.RemoveAll(Function(x) x.GroupId = g.GroupId)
            AllGroups.RemoveAll(Function(x) x.GroupId = g.GroupId)
            If AllGroups.Count = 0 Then
                AllGroups.Add(New StrategyGroup With {.GroupId = "default", .GroupName = "Default", .DisplayOrder = 0})
            End If
            _selectedStrategyId = ""
            SaveAll()
            RefreshStrategyTree()
        End If
    End Sub

    Private Async Sub RequestAIDesign(sender As Object, e As EventArgs)
        Dim prompt = _txtPrompt.Text.Trim()
        If String.IsNullOrEmpty(prompt) Then Return

        LogChat($"USER: {prompt}")
        _txtPrompt.Clear()
        LogChat("AI: 요청 내용을 분석하고 전략 조건을 생성하는 중...")
        Await Threading.Tasks.Task.Delay(400)

        Dim newStrategy = StrategyBridge.CreateFromNaturalLanguage(prompt)
        If newStrategy Is Nothing Then
            LogChat("AI: 전략 변환 실패. 문장을 더 구체화해 주세요.")
            Return
        End If

        newStrategy.GroupId = CurrentGroupId()
        If String.IsNullOrWhiteSpace(newStrategy.StrategyId) Then newStrategy.StrategyId = Guid.NewGuid().ToString("N")
        newStrategy.DisplayOrder = AllStrategies.Count + 1

        AllStrategies.Add(newStrategy)
        _selectedStrategyId = newStrategy.StrategyId
        SaveAll()
        RefreshStrategyTree()

        LogChat($"AI: '{newStrategy.Name}' 전략을 생성했습니다.")
    End Sub

    Public Sub SelectStrategyByName(name As String)
        Dim s = AllStrategies.FirstOrDefault(Function(x) String.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
        If s Is Nothing Then Return
        _selectedStrategyId = s.StrategyId
        RefreshStrategyTree()
    End Sub

    Private Sub ApplySelected(sender As Object, e As EventArgs)
        Dim node = _tvStrategies.SelectedNode
        If node Is Nothing Then Return

        ' 하드코딩 IStrategy 적용
        Dim hardcoded = TryCast(node.Tag, IStrategy)
        If hardcoded IsNot Nothing Then
            _onApplyHardcoded?.Invoke(hardcoded)
            Me.Close()
            Return
        End If

        Dim s = TryCast(node.Tag, StrategyDefinition)
        If s Is Nothing Then Return
        _onApply?.Invoke(s)
        Me.Close()
    End Sub

    Private Sub SaveAll()
        StrategyPersistenceService.SaveStore(_store)
    End Sub

    Private Shared Function DeepCloneStrategy(src As StrategyDefinition) As StrategyDefinition
        Dim clone As New StrategyDefinition With {
            .Name = src.Name,
            .Description = src.Description,
            .NaturalLanguagePrompt = src.NaturalLanguagePrompt,
            .RequiredDataDays = src.RequiredDataDays,
            .IsActive = src.IsActive,
            .Mode = src.Mode,
            .GroupId = src.GroupId,
            .DisplayOrder = src.DisplayOrder,
            .BaseStrategyId = src.BaseStrategyId,
            .Version = src.Version
        }

        clone.BuyRules = src.BuyRules.Select(Function(g) DeepCloneGate(g)).ToList()
        clone.SellRules = src.SellRules.Select(Function(g) DeepCloneGate(g)).ToList()
        Return clone
    End Function

    Private Shared Function DeepCloneGate(src As LogicGate) As LogicGate
        Dim g As New LogicGate With {
            .Name = src.Name,
            .Operator = src.Operator,
            .IsActive = src.IsActive,
            .Conditions = src.Conditions.Select(Function(c) DeepCloneCondition(c)).ToList()
        }
        Return g
    End Function

    Private Shared Function DeepCloneCondition(src As ConditionCell) As ConditionCell
        Return New ConditionCell With {
            .Id = src.Id,
            .Description = src.Description,
            .IndicatorA = src.IndicatorA,
            .Operator = src.Operator,
            .IndicatorB = src.IndicatorB,
            .ConstantValue = src.ConstantValue,
            .IsActive = src.IsActive,
            .Offset = src.Offset,
            .Lookback = src.Lookback,
            .IsInverted = src.IsInverted
        }
    End Function

    Private Sub LogChat(msg As String)
        _rtbChat.AppendText(msg & Environment.NewLine & Environment.NewLine)
        _rtbChat.SelectionStart = _rtbChat.Text.Length
        _rtbChat.ScrollToCaret()
    End Sub
End Class
