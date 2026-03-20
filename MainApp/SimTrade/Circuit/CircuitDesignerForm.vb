' ═══════════════════════════════════════════════════════════════
' CircuitDesignerForm.vb — 전략 회로 설계기 폼
' ═══════════════════════════════════════════════════════════════
' 회로도를 시각적으로 표시하고, 노드 클릭으로 ON/OFF 전환,
' 더블클릭으로 파라미터 편집, 실시간 신호 흐름을 표시한다.
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports MainApp.SimTrade
Imports MainApp.SimTrade.Circuit

Public Class CircuitDesignerForm
    Inherits Form

    Private _engine As CircuitEngine
    Private _circuit As CircuitDefinition
    Private _settings As SimTradeSettings
    Private _selectedNode As CircuitNode = Nothing

    ' ── UI ──
    Private WithEvents _canvas As New PictureBox()
    Private WithEvents _tmrRefresh As New Timer()
    Private _pnlParams As Panel
    Private _lblInfo As Label
    Private _chkLive As CheckBox

    ' ── 드래그 ──
    Private _isDragging As Boolean = False
    Private _dragOffset As Point

    Public Sub New(settings As SimTradeSettings)
        _settings = settings
        _engine = New CircuitEngine(settings)
        _circuit = CircuitEngine.CreateDefaultCircuit(settings)
        _engine.LoadCircuit(_circuit)

        InitUI()
        _tmrRefresh.Interval = 500
        _tmrRefresh.Start()
    End Sub

    Private Sub InitUI()
        Me.Text = "Strategy Circuit Designer v1.0"
        Me.Size = New Size(1200, 800)
        Me.BackColor = Color.FromArgb(25, 25, 30)
        Me.ForeColor = Color.White
        Me.DoubleBuffered = True

        ' ── 캔버스 (회로도 렌더링) ──
        _canvas.Dock = DockStyle.Fill
        _canvas.BackColor = Color.FromArgb(20, 20, 25)

        ' ── 우측 파라미터 패널 ──
        _pnlParams = New Panel()
        _pnlParams.Dock = DockStyle.Right
        _pnlParams.Width = 280
        _pnlParams.BackColor = Color.FromArgb(35, 35, 40)
        _pnlParams.AutoScroll = True

        _lblInfo = New Label()
        _lblInfo.Text = "노드를 클릭하세요"
        _lblInfo.Dock = DockStyle.Top
        _lblInfo.Height = 30
        _lblInfo.ForeColor = Color.Cyan
        _lblInfo.TextAlign = ContentAlignment.MiddleCenter
        _pnlParams.Controls.Add(_lblInfo)

        ' ── 하단 정보 패널 ──
        Dim pnlBottom As New Panel()
        pnlBottom.Dock = DockStyle.Bottom
        pnlBottom.Height = 40
        pnlBottom.BackColor = Color.FromArgb(40, 40, 45)

        _chkLive = New CheckBox()
        _chkLive.Text = "실시간 업데이트"
        _chkLive.Checked = True
        _chkLive.ForeColor = Color.White
        _chkLive.Location = New Point(10, 8)
        _chkLive.AutoSize = True
        pnlBottom.Controls.Add(_chkLive)

        Dim btnReset As New Button()
        btnReset.Text = "기본값 복원"
        btnReset.Location = New Point(160, 6)
        btnReset.Size = New Size(100, 28)
        btnReset.FlatStyle = FlatStyle.Flat
        btnReset.ForeColor = Color.White
        btnReset.BackColor = Color.FromArgb(60, 60, 65)
        AddHandler btnReset.Click, Sub(s, e) ResetAllParams()
        pnlBottom.Controls.Add(btnReset)

        Me.Controls.Add(_canvas)
        Me.Controls.Add(_pnlParams)
        Me.Controls.Add(pnlBottom)
    End Sub

#Region "렌더링"

    Private Sub OnCanvasPaint(sender As Object, e As PaintEventArgs) Handles _canvas.Paint
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(Color.FromArgb(20, 20, 25))

        If _circuit Is Nothing Then Return

        ' ── 와이어 그리기 ──
        For Each wire In _circuit.Wires
            Dim fromNode = _circuit.GetNode(wire.FromNodeId)
            Dim toNode = _circuit.GetNode(wire.ToNodeId)
            If fromNode Is Nothing OrElse toNode Is Nothing Then Continue For

            Dim wireColor As Color
            Select Case wire.State
                Case WireState.Active : wireColor = Color.FromArgb(0, 220, 0)
                Case WireState.Blocked : wireColor = Color.FromArgb(220, 50, 50)
                Case WireState.Warning : wireColor = Color.FromArgb(220, 180, 0)
                Case Else : wireColor = Color.FromArgb(80, 80, 80)
            End Select

            Using pen As New Pen(wireColor, If(wire.State = WireState.Active, 2.5F, 1.5F))
                Dim p1 = New Point(fromNode.X + fromNode.Width, fromNode.CenterPoint.Y)
                Dim p2 = New Point(toNode.X, toNode.CenterPoint.Y)
                Dim midX = (p1.X + p2.X) \ 2
                g.DrawBezier(pen, p1, New Point(midX, p1.Y), New Point(midX, p2.Y), p2)
            End Using
        Next

        ' ── 노드 그리기 ──
        For Each node In _circuit.Nodes
            DrawNode(g, node)
        Next
    End Sub

    Private Sub DrawNode(g As Graphics, node As CircuitNode)
        Dim rect As New Rectangle(node.X, node.Y, node.Width, node.Height)

        ' 배경
        Dim bgColor = If(Not node.Enabled, Color.FromArgb(50, 50, 55),
                      If(node.IsTriggered, Color.FromArgb(20, 80, 20),
                         Color.FromArgb(40, 50, 70)))
        If _selectedNode IsNot Nothing AndAlso _selectedNode.Id = node.Id Then
            bgColor = Color.FromArgb(70, 90, 130)
        End If

        Using brush As New SolidBrush(bgColor)
            g.FillRoundedRectangle(brush, rect, 8)
        End Using

        ' 테두리
        Dim borderColor = If(node.IsTriggered AndAlso node.Enabled, Color.Lime,
                          If(Not node.Enabled, Color.Gray, Color.FromArgb(100, 140, 200)))
        Using pen As New Pen(borderColor, If(_selectedNode IsNot Nothing AndAlso _selectedNode.Id = node.Id, 2.5F, 1.0F))
            g.DrawRoundedRectangle(pen, rect, 8)
        End Using

        ' ON/OFF 인디케이터
        Dim ledColor = If(node.Enabled, If(node.IsTriggered, Color.Lime, Color.FromArgb(100, 100, 100)), Color.Red)
        Dim ledRect As New Rectangle(node.X + 5, node.Y + 5, 10, 10)
        Using ledBrush As New SolidBrush(ledColor)
            g.FillEllipse(ledBrush, ledRect)
        End Using

        ' 이름
        Using font As New Font("맑은 고딕", 9, FontStyle.Bold)
            Using textBrush As New SolidBrush(Color.White)
                g.DrawString(node.Name, font, textBrush, node.X + 20, node.Y + 5)
            End Using
        End Using

        ' 프로브 값
        If node.ProbeText <> "" Then
            Using font As New Font("Consolas", 8)
                Dim probeColor = If(node.IsTriggered, Color.LightGreen, Color.FromArgb(180, 180, 180))
                Using textBrush As New SolidBrush(probeColor)
                    g.DrawString(node.ProbeText, font, textBrush, node.X + 5, node.Y + node.Height - 18)
                End Using
            End Using
        End If
    End Sub

#End Region

#Region "마우스 이벤트"

    Private Sub OnCanvasMouseDown(sender As Object, e As MouseEventArgs) Handles _canvas.MouseDown
        _selectedNode = HitTest(e.Location)
        If _selectedNode IsNot Nothing Then
            ShowNodeParams(_selectedNode)
            _isDragging = True
            _dragOffset = New Point(e.X - _selectedNode.X, e.Y - _selectedNode.Y)
        End If
        _canvas.Invalidate()
    End Sub

    Private Sub OnCanvasMouseMove(sender As Object, e As MouseEventArgs) Handles _canvas.MouseMove
        If _isDragging AndAlso _selectedNode IsNot Nothing Then
            _selectedNode.X = e.X - _dragOffset.X
            _selectedNode.Y = e.Y - _dragOffset.Y
            _canvas.Invalidate()
        End If
    End Sub

    Private Sub OnCanvasMouseUp(sender As Object, e As MouseEventArgs) Handles _canvas.MouseUp
        _isDragging = False
    End Sub

    Private Sub OnCanvasDoubleClick(sender As Object, e As EventArgs) Handles _canvas.DoubleClick
        If _selectedNode IsNot Nothing AndAlso Not _selectedNode.Locked Then
            ' ON/OFF 토글
            _selectedNode.Enabled = Not _selectedNode.Enabled
            ShowNodeParams(_selectedNode)
            _canvas.Invalidate()
        End If
    End Sub

    Private Function HitTest(pt As Point) As CircuitNode
        For i = _circuit.Nodes.Count - 1 To 0 Step -1
            Dim n = _circuit.Nodes(i)
            Dim rect As New Rectangle(n.X, n.Y, n.Width, n.Height)
            If rect.Contains(pt) Then Return n
        Next
        Return Nothing
    End Function

#End Region

#Region "파라미터 패널"

    Private Sub ShowNodeParams(node As CircuitNode)
        ' 기존 컨트롤 제거 (lblInfo 제외)
        Dim toRemove = _pnlParams.Controls.Cast(Of Control).Where(Function(c) c IsNot _lblInfo).ToList()
        For Each c In toRemove : _pnlParams.Controls.Remove(c) : Next

        _lblInfo.Text = $"{node.Name} ({If(node.Enabled, "ON", "OFF")})"

        Dim y = 40

        ' ON/OFF 스위치
        If Not node.Locked Then
            Dim chk As New CheckBox()
            chk.Text = "활성화"
            chk.Checked = node.Enabled
            chk.Location = New Point(10, y)
            chk.ForeColor = Color.White
            chk.AutoSize = True
            AddHandler chk.CheckedChanged, Sub(s, e)
                                               node.Enabled = chk.Checked
                                               _lblInfo.Text = $"{node.Name} ({If(node.Enabled, "ON", "OFF")})"
                                               _canvas.Invalidate()
                                           End Sub
            _pnlParams.Controls.Add(chk)
            y += 30
        End If

        ' 파라미터 컨트롤
        For Each param In node.Params
            Dim lbl As New Label()
            lbl.Text = param.Label
            lbl.Location = New Point(10, y + 3)
            lbl.Size = New Size(100, 20)
            lbl.ForeColor = Color.White
            _pnlParams.Controls.Add(lbl)

            Select Case param.DataType
                Case ParamDataType.IntNumber, ParamDataType.DecNumber
                    Dim nud As New NumericUpDown()
                    nud.Location = New Point(120, y)
                    nud.Size = New Size(100, 25)
                    nud.Minimum = If(param.MinValue IsNot Nothing, CDec(param.MinValue), 0)
                    nud.Maximum = If(param.MaxValue IsNot Nothing, CDec(param.MaxValue), 1000)
                    nud.Value = CDec(If(param.Value, param.DefaultValue))
                    nud.DecimalPlaces = If(param.DataType = ParamDataType.DecNumber, 1, 0)
                    nud.Increment = If(param.StepValue IsNot Nothing, CDec(param.StepValue), 1D)
                    nud.BackColor = Color.FromArgb(50, 50, 55)
                    nud.ForeColor = Color.White
                    Dim capturedParam = param
                    AddHandler nud.ValueChanged, Sub(s, e)
                                                     capturedParam.Value = nud.Value
                                                     _canvas.Invalidate()
                                                 End Sub
                    _pnlParams.Controls.Add(nud)

                Case ParamDataType.Bool
                    Dim chk As New CheckBox()
                    chk.Checked = CBool(If(param.Value, param.DefaultValue))
                    chk.Location = New Point(120, y)
                    chk.ForeColor = Color.White
                    Dim capturedParam = param
                    AddHandler chk.CheckedChanged, Sub(s, e) capturedParam.Value = chk.Checked
                    _pnlParams.Controls.Add(chk)
            End Select

            y += 35
        Next

        ' 프로브 표시
        If node.ProbeText <> "" Then
            Dim lblProbe As New Label()
            lblProbe.Text = $"[프로브] {node.ProbeText}"
            lblProbe.Location = New Point(10, y + 10)
            lblProbe.Size = New Size(260, 20)
            lblProbe.ForeColor = Color.LightGreen
            _pnlParams.Controls.Add(lblProbe)
        End If
    End Sub

    Private Sub ResetAllParams()
        For Each node In _circuit.Nodes
            For Each param In node.Params
                param.Reset()
            Next
            node.Enabled = True
        Next
        _canvas.Invalidate()
        If _selectedNode IsNot Nothing Then ShowNodeParams(_selectedNode)
    End Sub

#End Region

#Region "타이머 갱신"

    Private Sub OnRefresh(sender As Object, e As EventArgs) Handles _tmrRefresh.Tick
        If _chkLive IsNot Nothing AndAlso _chkLive.Checked Then
            _canvas.Invalidate()
        End If
    End Sub

#End Region

End Class

''' <summary>Graphics 확장: 둥근 사각형</summary>
Module GraphicsExtensions
    <System.Runtime.CompilerServices.Extension()>
    Public Sub FillRoundedRectangle(g As Graphics, brush As Brush, rect As Rectangle, radius As Integer)
        Using path As New GraphicsPath()
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90)
            path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90)
            path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90)
            path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90)
            path.CloseFigure()
            g.FillPath(brush, path)
        End Using
    End Sub

    <System.Runtime.CompilerServices.Extension()>
    Public Sub DrawRoundedRectangle(g As Graphics, pen As Pen, rect As Rectangle, radius As Integer)
        Using path As New GraphicsPath()
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90)
            path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90)
            path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90)
            path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90)
            path.CloseFigure()
            g.DrawPath(pen, path)
        End Using
    End Sub
End Module
