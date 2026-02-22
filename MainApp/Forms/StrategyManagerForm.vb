Imports System.Drawing
Imports System.Windows.Forms
Imports MainApp.Models
Imports MainApp.Services

' ─────────────────────────────────────────────────────────────────
' Form 클래스
' ─────────────────────────────────────────────────────────────────
Public Class StrategyManagerForm
    Inherits Form

    Private _lstStrategies As ListBox
    Private _tvLogic As TreeView
    Private _rtbChat As RichTextBox
    Private _txtPrompt As TextBox
    Private _btnApply As Button
    Private _allStrategies As New List(Of StrategyDefinition)
    Private _onApply As Action(Of StrategyDefinition)

    Public Sub New(Optional onApply As Action(Of StrategyDefinition) = Nothing)
        _onApply = onApply

        ' 데이터 로드
        _allStrategies = StrategyPersistenceService.LoadStrategies()

        If _allStrategies.Count = 0 Then
            ' 샘플 데이터 초기화 (기존 데이터 없을 때)
            _allStrategies.Add(New StrategyDefinition("기본 돌파 전략", "슈퍼트렌드 상향 돌파 시 매수",
                New List(Of LogicGate) From {New LogicGate("Entry", LogicalOperator.AND, New List(Of ConditionCell) From {
                    New ConditionCell("C1", "Price CrossUp SuperTrend", "Price", ComparisonOperator.CrossUp, "SuperTrend")
                })},
                New List(Of LogicGate) From {New LogicGate("Exit", LogicalOperator.AND, New List(Of ConditionCell) From {
                    New ConditionCell("C2", "Price CrossDown SuperTrend", "Price", ComparisonOperator.CrossDown, "SuperTrend")
                })}))
        End If

        InitializeUI()
        RefreshStrategyList()
    End Sub

    Private Sub InitializeUI()
        Me.Text = "전략 관리자 (AI Assistant & CRUD)"
        Me.Size = New Size(1000, 700)
        Me.StartPosition = FormStartPosition.CenterParent
        Me.BackColor = Color.FromArgb(28, 28, 38)
        Me.ForeColor = Color.White

        Dim mainSplit As New SplitContainer With {.Dock = DockStyle.Fill, .Orientation = Orientation.Vertical, .FixedPanel = FixedPanel.Panel1}
        Dim rightSplit As New SplitContainer With {.Dock = DockStyle.Fill, .Orientation = Orientation.Horizontal}

        ' --- 왼쪽: 전략 리스트 ---
        Dim leftPanel As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
        _lstStrategies = New ListBox With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(40, 40, 50),
            .ForeColor = Color.White,
            .BorderStyle = BorderStyle.None,
            .Font = New Font("맑은 고딕", 10),
            .DrawMode = DrawMode.OwnerDrawFixed,
            .ItemHeight = 35
        }
        AddHandler _lstStrategies.DrawItem, AddressOf OnLstStrategiesDrawItem
        AddHandler _lstStrategies.SelectedIndexChanged, Sub() ShowSelectedStrategy()

        Dim btnAdd As New Button With {.Text = "새 전략", .Dock = DockStyle.Bottom, .Height = 35, .FlatStyle = FlatStyle.Flat}
        AddHandler btnAdd.Click, Sub() AddNewStrategy()

        leftPanel.Controls.Add(_lstStrategies)
        leftPanel.Controls.Add(btnAdd)
        mainSplit.Panel1.Controls.Add(leftPanel)

        ' --- 오른쪽 위: 논리 투명화 보기 (TreeView) ---
        Dim logicPanel As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
        _tvLogic = New TreeView With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(30, 30, 40),
            .ForeColor = Color.Lime,
            .BorderStyle = BorderStyle.None,
            .Font = New Font("Consolas", 10)
        }
        logicPanel.Controls.Add(New Label With {.Text = "전략 논리 구조 (Transparency View)", .Dock = DockStyle.Top, .Height = 25, .ForeColor = Color.Cyan})
        logicPanel.Controls.Add(_tvLogic)
        rightSplit.Panel1.Controls.Add(logicPanel)

        ' --- 오른쪽 아래: AI 대화창 ---
        Dim chatPanel As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
        _rtbChat = New RichTextBox With {
            .Dock = DockStyle.Fill,
            .BackColor = Color.FromArgb(20, 20, 30),
            .ForeColor = Color.FromArgb(200, 200, 200),
            .ReadOnly = True,
            .BorderStyle = BorderStyle.None
        }

        Dim promptContainer As New Panel With {.Dock = DockStyle.Bottom, .Height = 80, .Padding = New Padding(0, 5, 0, 0)}
        _txtPrompt = New TextBox With {.Dock = DockStyle.Fill, .Multiline = True, .BackColor = Color.FromArgb(45, 45, 55), .ForeColor = Color.White, .BorderStyle = BorderStyle.FixedSingle}
        Dim btnSend As New Button With {.Text = "AI 설계 요청", .Dock = DockStyle.Right, .Width = 100, .BackColor = Color.FromArgb(60, 100, 60), .FlatStyle = FlatStyle.Flat}
        AddHandler btnSend.Click, AddressOf RequestAIDesign

        AddHandler _txtPrompt.TextChanged, Sub()
                                               If _txtPrompt.Text.Contains("상승") OrElse _txtPrompt.Text.Contains("돌파") OrElse _txtPrompt.Text.Contains("이탈") Then
                                                   _txtPrompt.ForeColor = Color.Cyan
                                               Else
                                                   _txtPrompt.ForeColor = Color.White
                                               End If
                                           End Sub

        promptContainer.Controls.Add(_txtPrompt)
        promptContainer.Controls.Add(btnSend)

        chatPanel.Controls.Add(_rtbChat)
        chatPanel.Controls.Add(promptContainer)
        chatPanel.Controls.Add(New Label With {.Text = "AI 전략 비서 (Natural Language Design)", .Dock = DockStyle.Top, .Height = 25, .ForeColor = Color.Orange})
        rightSplit.Panel2.Controls.Add(chatPanel)

        ' --- 하단 버튼 ---
        Dim bottomPanel As New Panel With {.Dock = DockStyle.Bottom, .Height = 50, .Padding = New Padding(10)}
        _btnApply = New Button With {.Text = "차트에 적용 및 검증", .Dock = DockStyle.Right, .Width = 150, .FlatStyle = FlatStyle.Flat, .BackColor = Color.FromArgb(100, 60, 100)}
        AddHandler _btnApply.Click, AddressOf ApplySelected
        bottomPanel.Controls.Add(_btnApply)

        mainSplit.Panel2.Controls.Add(rightSplit)
        Me.Controls.Add(mainSplit)
        Me.Controls.Add(bottomPanel)

        AddHandler Me.Load, Sub()
                                Try
                                    mainSplit.SplitterDistance = 220
                                    rightSplit.SplitterDistance = Me.Height - 320
                                Catch
                                End Try
                            End Sub

        LogChat("AI: 안녕하세요! 원하시는 매매 철학을 말씀해 주시면 정밀한 논리 게이트로 설계해 드립니다.")
    End Sub

    Private Sub RefreshStrategyList()
        _lstStrategies.Items.Clear()
        For Each s In _allStrategies
            _lstStrategies.Items.Add(s.Name)
        Next
    End Sub

    Private Sub ShowSelectedStrategy()
        Dim idx = _lstStrategies.SelectedIndex
        If idx < 0 Then Return

        Dim s = _allStrategies(idx)

        If Not String.IsNullOrEmpty(s.NaturalLanguagePrompt) Then
            _txtPrompt.Text = s.NaturalLanguagePrompt
        End If

        _tvLogic.Nodes.Clear()
        Dim root = _tvLogic.Nodes.Add(s.Name)

        Dim buyNode = root.Nodes.Add("매수 규칙 (Buy Gates)")
        For Each gate In s.BuyRules
            Dim gNode = buyNode.Nodes.Add($"{gate.Name} ({gate.Operator.ToString()})")
            For Each cond In gate.Conditions
                gNode.Nodes.Add(cond.Description)
            Next
        Next

        Dim sellNode = root.Nodes.Add("매도 규칙 (Sell Gates)")
        For Each gate In s.SellRules
            Dim gNode = sellNode.Nodes.Add($"{gate.Name} ({gate.Operator.ToString()})")
            For Each cond In gate.Conditions
                gNode.Nodes.Add(cond.Description)
            Next
        Next
        _tvLogic.ExpandAll()
    End Sub

    Private Sub AddNewStrategy()
        _txtPrompt.Text = "새로운 전략의 특징을 입력하세요..."
        _txtPrompt.Focus()
    End Sub

    Private Async Sub RequestAIDesign()
        Dim prompt = _txtPrompt.Text.Trim()
        If String.IsNullOrEmpty(prompt) Then Return

        LogChat($"USER: {prompt}")
        _txtPrompt.Clear()

        LogChat("AI: 요청하신 내용을 분석 중입니다... [원자적 조건 추출 중]")

        Await System.Threading.Tasks.Task.Delay(800)

        ' Ported StrategyBridge 사용
        Dim newStrategy = Services.StrategyBridge.CreateFromNaturalLanguage(prompt)

        If newStrategy IsNot Nothing Then
            _allStrategies.Add(newStrategy)
            RefreshStrategyList()
            _lstStrategies.SelectedIndex = _allStrategies.Count - 1
            StrategyPersistenceService.SaveStrategies(_allStrategies) ' 자동 저장
            LogChat($"AI: '{newStrategy.Name}' 전략 설계가 완료되었습니다. 논리 구조를 확인해 보세요.")
        Else
            LogChat("AI: 죄송합니다. 해당 내용을 전략 논리로 해석하지 못했습니다. 조금 더 구체적으로 말씀해 주시겠어요?")
        End If
    End Sub

    Public Sub SelectStrategyByName(name As String)
        For i As Integer = 0 To _lstStrategies.Items.Count - 1
            If _lstStrategies.Items(i).ToString() = name Then
                _lstStrategies.SelectedIndex = i
                Exit For
            End If
        Next
    End Sub

    Private Sub ApplySelected()
        Dim idx = _lstStrategies.SelectedIndex
        If idx >= 0 Then
            _onApply?.Invoke(_allStrategies(idx))
            Me.Close()
        End If
    End Sub

    Private Sub OnLstStrategiesDrawItem(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 Then Return

        e.DrawBackground()
        Dim isSelected = (e.State And DrawItemState.Selected) = DrawItemState.Selected

        Using brush As New SolidBrush(If(isSelected, Color.FromArgb(100, 60, 100), Color.FromArgb(40, 40, 50)))
            e.Graphics.FillRectangle(brush, e.Bounds)
        End Using

        Dim text = _lstStrategies.Items(e.Index).ToString()
        Dim font = _lstStrategies.Font
        Dim textBrush = If(isSelected, Brushes.Yellow, Brushes.White)

        Dim stringSize = e.Graphics.MeasureString(text, font)
        Dim y = e.Bounds.Y + (e.Bounds.Height - stringSize.Height) / 2

        e.Graphics.DrawString(text, font, textBrush, e.Bounds.X + 5, y)
        e.DrawFocusRectangle()
    End Sub

    Private Sub LogChat(msg As String)
        _rtbChat.AppendText(msg & Environment.NewLine & Environment.NewLine)
        _rtbChat.SelectionStart = _rtbChat.Text.Length
        _rtbChat.ScrollToCaret()
    End Sub
End Class

