' ═══════════════════════════════════════════════════════════════
' CircuitRenderer.vb — 전략 회로 시각화 렌더러 (GDI+)
' ═══════════════════════════════════════════════════════════════
' 의존: CircuitModels.vb (모든 enum/class 정의는 CircuitModels.vb에만 존재)
' ★ 이 파일에 enum/class를 중복 정의하지 마세요.
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing
Imports System.Drawing.Drawing2D

Namespace SimTrade.Circuit

    Public Class CircuitRenderer

#Region "색상 상수"

        Private Shared ReadOnly BG_COLOR As Color = Color.FromArgb(20, 20, 30)
        Private Shared ReadOnly GRID_COLOR As Color = Color.FromArgb(35, 35, 50)

        Private Shared ReadOnly NODE_FILL_ON As Color = Color.FromArgb(40, 50, 70)
        Private Shared ReadOnly NODE_FILL_OFF As Color = Color.FromArgb(30, 30, 40)
        Private Shared ReadOnly NODE_BORDER_ON As Color = Color.FromArgb(100, 160, 255)
        Private Shared ReadOnly NODE_BORDER_OFF As Color = Color.FromArgb(60, 60, 80)
        Private Shared ReadOnly NODE_BORDER_SELECTED As Color = Color.FromArgb(255, 200, 50)

        Private Shared ReadOnly LED_PASS As Color = Color.FromArgb(0, 255, 100)
        Private Shared ReadOnly LED_FAIL As Color = Color.FromArgb(255, 60, 60)
        Private Shared ReadOnly LED_WARN As Color = Color.FromArgb(255, 200, 0)
        Private Shared ReadOnly LED_OFF As Color = Color.FromArgb(60, 60, 60)
        Private Shared ReadOnly LED_DISABLED As Color = Color.FromArgb(80, 80, 80)

        Private Shared ReadOnly WIRE_ACTIVE As Color = Color.FromArgb(0, 200, 100)
        Private Shared ReadOnly WIRE_INACTIVE As Color = Color.FromArgb(60, 60, 80)
        Private Shared ReadOnly WIRE_BLOCKED As Color = Color.FromArgb(200, 50, 50)
        Private Shared ReadOnly WIRE_WARNING As Color = Color.FromArgb(200, 180, 0)

        Private Shared ReadOnly TEXT_TITLE As Color = Color.FromArgb(220, 230, 255)
        Private Shared ReadOnly TEXT_LABEL As Color = Color.FromArgb(170, 180, 200)
        Private Shared ReadOnly TEXT_VALUE As Color = Color.FromArgb(130, 220, 255)
        Private Shared ReadOnly TEXT_PROBE As Color = Color.FromArgb(255, 255, 150)
        Private Shared ReadOnly TEXT_DIM As Color = Color.FromArgb(90, 100, 120)
        Private Shared ReadOnly TEXT_WARN As Color = Color.FromArgb(255, 180, 0)

        Private Shared ReadOnly GATE_FILL As Color = Color.FromArgb(50, 40, 60)
        Private Shared ReadOnly GATE_BORDER As Color = Color.FromArgb(180, 130, 255)

        Private Shared ReadOnly PARAM_FILL As Color = Color.FromArgb(35, 45, 55)
        Private Shared ReadOnly PARAM_BORDER As Color = Color.FromArgb(80, 120, 160)

        Private Shared ReadOnly ZONE_BORDER As Color = Color.FromArgb(50, 55, 70)
        Private Shared ReadOnly ZONE_LABEL As Color = Color.FromArgb(80, 90, 110)

#End Region

#Region "폰트"

        Private Shared ReadOnly FONT_TITLE As New Font("Consolas", 10, FontStyle.Bold)
        Private Shared ReadOnly FONT_NODE As New Font("Consolas", 8.5F, FontStyle.Bold)
        Private Shared ReadOnly FONT_LABEL As New Font("Consolas", 7.5F, FontStyle.Regular)
        Private Shared ReadOnly FONT_VALUE As New Font("Consolas", 7.5F, FontStyle.Bold)
        Private Shared ReadOnly FONT_PROBE As New Font("Consolas", 7, FontStyle.Italic)
        Private Shared ReadOnly FONT_PARAM As New Font("Consolas", 6.5F, FontStyle.Regular)
        Private Shared ReadOnly FONT_ZONE As New Font("Consolas", 9, FontStyle.Bold)
        Private Shared ReadOnly FONT_GATE As New Font("Consolas", 10, FontStyle.Bold)
        Private Shared ReadOnly FONT_LEGEND As New Font("Consolas", 7, FontStyle.Regular)

#End Region

#Region "렌더링 상수"

        Private Const LED_RADIUS As Integer = 5
        Private Const NODE_CORNER_RADIUS As Integer = 6
        Private Const WIRE_WIDTH As Single = 2.0F
        Private Const WIRE_ACTIVE_WIDTH As Single = 2.5F
        Private Const GRID_SPACING As Integer = 20
        Private Const PARAM_HEIGHT As Integer = 16
        Private Const PARAM_WIDTH As Integer = 60
        Private Const GATE_SIZE As Integer = 36
        Private Const ARROW_SIZE As Integer = 6
        Private Const LEGEND_Y_OFFSET As Integer = 30

#End Region

#Region "필드"

        Private _definition As CircuitDefinition
        Private _evalResult As CircuitEvalResult
        Private _selectedNodeId As String = ""
        Private _hoveredNodeId As String = ""
        Private _offset As PointF = PointF.Empty
        Private _zoom As Single = 1.0F
        Private _showGrid As Boolean = True
        Private _showProbeText As Boolean = True
        Private _showParamBoxes As Boolean = True
        Private _showLegend As Boolean = True
        Private _animPhase As Integer = 0

#End Region

#Region "속성"

        Public Property Definition As CircuitDefinition
            Get
                Return _definition
            End Get
            Set(value As CircuitDefinition)
                _definition = value
            End Set
        End Property

        Public Property EvalResult As CircuitEvalResult
            Get
                Return _evalResult
            End Get
            Set(value As CircuitEvalResult)
                _evalResult = value
            End Set
        End Property

        Public Property SelectedNodeId As String
            Get
                Return _selectedNodeId
            End Get
            Set(value As String)
                _selectedNodeId = If(value, "")
            End Set
        End Property

        Public Property HoveredNodeId As String
            Get
                Return _hoveredNodeId
            End Get
            Set(value As String)
                _hoveredNodeId = If(value, "")
            End Set
        End Property

        Public Property Offset As PointF
            Get
                Return _offset
            End Get
            Set(value As PointF)
                _offset = value
            End Set
        End Property

        Public Property Zoom As Single
            Get
                Return _zoom
            End Get
            Set(value As Single)
                _zoom = Math.Max(0.3F, Math.Min(3.0F, value))
            End Set
        End Property

        Public Property ShowGrid As Boolean
            Get
                Return _showGrid
            End Get
            Set(value As Boolean)
                _showGrid = value
            End Set
        End Property

        Public Property ShowProbeText As Boolean
            Get
                Return _showProbeText
            End Get
            Set(value As Boolean)
                _showProbeText = value
            End Set
        End Property

        Public Property ShowLegend As Boolean
            Get
                Return _showLegend
            End Get
            Set(value As Boolean)
                _showLegend = value
            End Set
        End Property

        Public Property AnimPhase As Integer
            Get
                Return _animPhase
            End Get
            Set(value As Integer)
                _animPhase = value Mod 360
            End Set
        End Property

#End Region

#Region "메인 렌더링"

        Public Sub Render(g As Graphics, bounds As Rectangle)
            If _definition Is Nothing Then Return

            g.SmoothingMode = SmoothingMode.AntiAlias
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
            g.InterpolationMode = InterpolationMode.HighQualityBicubic

            Using bgBrush As New SolidBrush(BG_COLOR)
                g.FillRectangle(bgBrush, bounds)
            End Using

            If _showGrid Then DrawGrid(g, bounds)

            Dim oldTransform = g.Transform
            g.TranslateTransform(_offset.X, _offset.Y)
            g.ScaleTransform(_zoom, _zoom)

            DrawZones(g)
            DrawAllWires(g)
            DrawAllGates(g)
            DrawAllNodes(g)

            g.Transform = oldTransform
            If _showLegend Then DrawLegend(g, bounds)
        End Sub

#End Region

#Region "그리드"

        Private Sub DrawGrid(g As Graphics, bounds As Rectangle)
            Using pen As New Pen(GRID_COLOR, 0.5F)
                Dim step_ = CInt(GRID_SPACING * _zoom)
                If step_ < 5 Then Return

                Dim sx = CInt(_offset.X Mod step_)
                Dim sy = CInt(_offset.Y Mod step_)

                For x = sx To bounds.Width Step step_
                    g.DrawLine(pen, x, 0, x, bounds.Height)
                Next
                For yy = sy To bounds.Height Step step_
                    g.DrawLine(pen, 0, yy, bounds.Width, yy)
                Next
            End Using
        End Sub

#End Region

#Region "영역 구분 (Zones)"

        Private Sub DrawZones(g As Graphics)
            If _definition.Nodes Is Nothing OrElse _definition.Nodes.Count = 0 Then Return

            ' Zone은 Category로 대체 (Zone이 비어있으면 Category 사용)
            Dim zoneGroups As New Dictionary(Of String, List(Of CircuitNode))(StringComparer.OrdinalIgnoreCase)
            For Each nd In _definition.Nodes
                Dim zone = If(String.IsNullOrEmpty(nd.Zone), If(String.IsNullOrEmpty(nd.Category), "기타", nd.Category), nd.Zone)
                If Not zoneGroups.ContainsKey(zone) Then zoneGroups(zone) = New List(Of CircuitNode)()
                zoneGroups(zone).Add(nd)
            Next

            Using pen As New Pen(ZONE_BORDER, 1.0F)
                pen.DashStyle = DashStyle.Dot
                Using brush As New SolidBrush(ZONE_LABEL)
                    For Each kvp In zoneGroups
                        Dim nodes = kvp.Value
                        If nodes.Count = 0 Then Continue For

                        Dim minX = nodes.Min(Function(n) n.X) - 15
                        Dim minY = nodes.Min(Function(n) n.Y) - 25
                        Dim maxX = nodes.Max(Function(n) n.X + n.Width) + 15
                        Dim maxY = nodes.Max(Function(n) n.Y + n.Height) + 20

                        Dim zoneRect As New Rectangle(minX, minY, maxX - minX, maxY - minY)
                        g.DrawRectangle(pen, zoneRect)
                        g.DrawString(kvp.Key, FONT_ZONE, brush, CSng(minX + 5), CSng(minY + 3))
                    Next
                End Using
            End Using
        End Sub

#End Region

#Region "와이어 렌더링"

        Private Sub DrawAllWires(g As Graphics)
            If _definition.Wires Is Nothing Then Return
            For Each wire In _definition.Wires
                DrawWire(g, wire)
            Next
        End Sub

        Private Sub DrawWire(g As Graphics, wire As CircuitWire)
            ' ★ 수정: FromNodeId / ToNodeId 사용 (SourceNodeId/TargetNodeId 아님)
            Dim srcNode = FindNode(wire.FromNodeId)
            Dim tgtNode = FindNode(wire.ToNodeId)
            If srcNode Is Nothing OrElse tgtNode Is Nothing Then Return

            Dim srcPt As New PointF(CSng(srcNode.X + srcNode.Width), CSng(srcNode.Y + srcNode.Height \ 2))
            Dim tgtPt As New PointF(CSng(tgtNode.X), CSng(tgtNode.Y + tgtNode.Height \ 2))

            Dim wireColor = GetWireColor(wire)
            Dim wireWidth = If(wire.State = WireState.Active, WIRE_ACTIVE_WIDTH, WIRE_WIDTH)

            Using pen As New Pen(wireColor, wireWidth)
                pen.StartCap = LineCap.Round
                pen.EndCap = LineCap.ArrowAnchor

                Dim midX = (srcPt.X + tgtPt.X) / 2.0F
                Dim cp1 As New PointF(midX, srcPt.Y)
                Dim cp2 As New PointF(midX, tgtPt.Y)
                g.DrawBezier(pen, srcPt, cp1, cp2, tgtPt)

                If wire.State = WireState.Active Then
                    DrawWirePulse(g, srcPt, cp1, cp2, tgtPt, wireColor)
                End If
            End Using

            If Not String.IsNullOrEmpty(wire.Label) Then
                Dim labelPt As New PointF((srcPt.X + tgtPt.X) / 2.0F - 15.0F,
                                          Math.Min(srcPt.Y, tgtPt.Y) - 12.0F)
                Using brush As New SolidBrush(TEXT_DIM)
                    g.DrawString(wire.Label, FONT_PARAM, brush, labelPt)
                End Using
            End If
        End Sub

        Private Sub DrawWirePulse(g As Graphics, p0 As PointF, p1 As PointF, p2 As PointF, p3 As PointF, baseColor As Color)
            Dim t = CSng((_animPhase Mod 100) / 100.0)
            Dim pt = BezierPoint(p0, p1, p2, p3, t)

            Dim pulseColor = Color.FromArgb(200,
                Math.Min(255, CInt(baseColor.R) + 80),
                Math.Min(255, CInt(baseColor.G) + 80),
                Math.Min(255, CInt(baseColor.B) + 80))
            Using brush As New SolidBrush(pulseColor)
                g.FillEllipse(brush, pt.X - 3.0F, pt.Y - 3.0F, 6.0F, 6.0F)
            End Using
        End Sub

        Private Shared Function BezierPoint(p0 As PointF, p1 As PointF, p2 As PointF, p3 As PointF, t As Single) As PointF
            Dim u = 1.0F - t
            Dim tt = t * t
            Dim uu = u * u
            Dim uuu = uu * u
            Dim ttt = tt * t
            Return New PointF(
                uuu * p0.X + 3.0F * uu * t * p1.X + 3.0F * u * tt * p2.X + ttt * p3.X,
                uuu * p0.Y + 3.0F * uu * t * p1.Y + 3.0F * u * tt * p2.Y + ttt * p3.Y)
        End Function

        Private Function GetWireColor(wire As CircuitWire) As Color
            ' ★ 수정: WireState.Disabled 제거 (enum에 없음)
            Select Case wire.State
                Case WireState.Active : Return WIRE_ACTIVE
                Case WireState.Inactive : Return WIRE_INACTIVE
                Case WireState.Blocked : Return WIRE_BLOCKED
                Case WireState.Warning : Return WIRE_WARNING
                Case Else : Return WIRE_INACTIVE
            End Select
        End Function

#End Region

#Region "게이트 렌더링"

        Private Sub DrawAllGates(g As Graphics)
            ' ★ 수정: Gates 컬렉션 사용 (CircuitDefinition에 추가됨)
            If _definition.Gates Is Nothing Then Return
            For Each gate In _definition.Gates
                DrawGate(g, gate)
            Next
        End Sub

        Private Sub DrawGate(g As Graphics, gate As CircuitGate)
            Dim rect As New Rectangle(gate.X, gate.Y, GATE_SIZE, GATE_SIZE)

            Using path = CreateRoundedRect(rect, 4)
                Using fill As New SolidBrush(GATE_FILL)
                    g.FillPath(fill, path)
                End Using
                Using border As New Pen(GATE_BORDER, 1.5F)
                    g.DrawPath(border, path)
                End Using
            End Using

            ' ★ 수정: AND_Gate / OR_Gate / NOT_Gate 사용
            Dim gateText As String
            Select Case gate.GateType
                Case GateType.AND_Gate : gateText = "AND"
                Case GateType.OR_Gate : gateText = "OR"
                Case GateType.NOT_Gate : gateText = "NOT"
                Case Else : gateText = "?"
            End Select

            Dim textSize = g.MeasureString(gateText, FONT_GATE)
            Dim textX = CSng(gate.X) + (CSng(GATE_SIZE) - textSize.Width) / 2.0F
            Dim textY = CSng(gate.Y) + (CSng(GATE_SIZE) - textSize.Height) / 2.0F

            Dim gatePassed = GetGateResult(gate.Id)
            Dim textColor = If(gatePassed, LED_PASS, LED_FAIL)
            Using brush As New SolidBrush(textColor)
                g.DrawString(gateText, FONT_GATE, brush, textX, textY)
            End Using

            If Not String.IsNullOrEmpty(gate.Label) Then
                Using brush As New SolidBrush(TEXT_LABEL)
                    Dim labelSize = g.MeasureString(gate.Label, FONT_LABEL)
                    g.DrawString(gate.Label, FONT_LABEL, brush,
                        CSng(gate.X) + (CSng(GATE_SIZE) - labelSize.Width) / 2.0F,
                        CSng(gate.Y) + CSng(GATE_SIZE) + 2.0F)
                End Using
            End If
        End Sub

#End Region

#Region "노드 렌더링"

        Private Sub DrawAllNodes(g As Graphics)
            If _definition.Nodes Is Nothing Then Return
            For Each nd In _definition.Nodes
                DrawNode(g, nd)
            Next
        End Sub

        Private Sub DrawNode(g As Graphics, node As CircuitNode)
            Dim rect As New Rectangle(node.X, node.Y, node.Width, node.Height)
            Dim isSelected = (node.Id = _selectedNodeId)
            Dim isHovered = (node.Id = _hoveredNodeId)
            Dim isEnabled = node.Enabled

            ' ── IC칩 본체 ──
            Using path = CreateRoundedRect(rect, NODE_CORNER_RADIUS)
                Dim fillColor = If(isEnabled, NODE_FILL_ON, NODE_FILL_OFF)
                If isHovered Then fillColor = Color.FromArgb(
                    Math.Min(255, CInt(fillColor.R) + 15),
                    Math.Min(255, CInt(fillColor.G) + 15),
                    Math.Min(255, CInt(fillColor.B) + 20))

                Using gradBrush As New LinearGradientBrush(rect, fillColor,
                    Color.FromArgb(Math.Max(0, CInt(fillColor.R) - 10),
                                   Math.Max(0, CInt(fillColor.G) - 10),
                                   Math.Max(0, CInt(fillColor.B) - 15)),
                    LinearGradientMode.Vertical)
                    g.FillPath(gradBrush, path)
                End Using

                Dim borderColor = If(isSelected, NODE_BORDER_SELECTED,
                                  If(isEnabled, NODE_BORDER_ON, NODE_BORDER_OFF))
                Dim borderWidth = If(isSelected, 2.5F, If(isHovered, 1.8F, 1.0F))
                Using borderPen As New Pen(borderColor, borderWidth)
                    g.DrawPath(borderPen, path)
                End Using
            End Using

            DrawICNotch(g, rect)

            ' ── LED ──
            Dim ledColor = GetNodeLEDColor(node)
            Dim ledX = rect.Right - LED_RADIUS * 2 - 4
            Dim ledY = rect.Top + 4
            DrawLED(g, ledX, ledY, ledColor)

            ' ── ON/OFF 스위치 ──
            DrawSwitch(g, rect.X + 4, rect.Y + 4, isEnabled)

            ' ── 노드 제목 (★ 수정: Name 사용, DisplayName 아님) ──
            Using brush As New SolidBrush(If(isEnabled, TEXT_TITLE, TEXT_DIM))
                g.DrawString(node.Name, FONT_NODE, brush, CSng(rect.X + 20), CSng(rect.Y + 4))
            End Using

            ' ── 노드 타입 아이콘 ──
            Dim typeIcon = GetNodeTypeIcon(node.NodeType)
            Using brush As New SolidBrush(TEXT_DIM)
                g.DrawString(typeIcon, FONT_LABEL, brush, CSng(rect.X + 4), CSng(rect.Y + 19))
            End Using

            ' ── 프로브 텍스트 ──
            If _showProbeText AndAlso Not String.IsNullOrEmpty(node.ProbeText) Then
                Dim probeY = rect.Y + rect.Height - 28
                Using brush As New SolidBrush(TEXT_PROBE)
                    Dim probeRect As New RectangleF(CSng(rect.X + 4), CSng(probeY), CSng(rect.Width - 8), 14.0F)
                    g.DrawString(node.ProbeText, FONT_PROBE, brush, probeRect)
                End Using
            End If

            ' ── 상태 텍스트 ──
            Dim stateText = GetNodeStateText(node)
            If Not String.IsNullOrEmpty(stateText) Then
                Dim stateColor = If(GetNodePassed(node), TEXT_VALUE, TEXT_WARN)
                Using brush As New SolidBrush(stateColor)
                    Dim stateRect As New RectangleF(CSng(rect.X + 4), CSng(rect.Y + 18), CSng(rect.Width - 8), 14.0F)
                    g.DrawString(stateText, FONT_VALUE, brush, stateRect)
                End Using
            End If

            ' ── 파라미터 박스 ──
            If _showParamBoxes AndAlso node.Params IsNot Nothing AndAlso node.Params.Count > 0 Then
                DrawParamBoxes(g, node)
            End If

            ' ── 비활성 오버레이 ──
            If Not isEnabled Then
                Using overlay As New SolidBrush(Color.FromArgb(120, 0, 0, 0))
                    Using path = CreateRoundedRect(rect, NODE_CORNER_RADIUS)
                        g.FillPath(overlay, path)
                    End Using
                End Using
                Using xPen As New Pen(Color.FromArgb(150, 255, 80, 80), 2.0F)
                    g.DrawLine(xPen, rect.X + 5, rect.Y + 5, rect.Right - 5, rect.Bottom - 5)
                    g.DrawLine(xPen, rect.Right - 5, rect.Y + 5, rect.X + 5, rect.Bottom - 5)
                End Using
            End If
        End Sub

        Private Sub DrawICNotch(g As Graphics, rect As Rectangle)
            Using pen As New Pen(Color.FromArgb(80, 100, 130), 1.0F)
                Dim notchX = rect.X + rect.Width \ 2 - 4
                g.DrawArc(pen, notchX, rect.Y - 2, 8, 4, 0, 180)
            End Using
        End Sub

        Private Sub DrawLED(g As Graphics, x As Integer, y As Integer, color As Color)
            Dim ledRect As New Rectangle(x, y, LED_RADIUS * 2, LED_RADIUS * 2)

            If color <> LED_OFF AndAlso color <> LED_DISABLED Then
                Dim glowRect As New Rectangle(x - 3, y - 3, LED_RADIUS * 2 + 6, LED_RADIUS * 2 + 6)
                Using glowBrush As New SolidBrush(Color.FromArgb(40, color))
                    g.FillEllipse(glowBrush, glowRect)
                End Using
            End If

            Using ledBrush As New SolidBrush(color)
                g.FillEllipse(ledBrush, ledRect)
            End Using

            Dim hlRect As New Rectangle(x + 2, y + 1, LED_RADIUS, Math.Max(1, LED_RADIUS - 1))
            Using hlBrush As New SolidBrush(Color.FromArgb(80, 255, 255, 255))
                g.FillEllipse(hlBrush, hlRect)
            End Using
        End Sub

        Private Sub DrawSwitch(g As Graphics, x As Integer, y As Integer, isOn As Boolean)
            Dim swRect As New Rectangle(x, y, 14, 8)
            Dim swColor = If(isOn, Color.FromArgb(0, 180, 80), Color.FromArgb(120, 50, 50))

            Using brush As New SolidBrush(swColor)
                g.FillRectangle(brush, swRect)
            End Using
            Using pen As New Pen(Color.FromArgb(100, 120, 140), 0.5F)
                g.DrawRectangle(pen, swRect)
            End Using

            Dim knobX = If(isOn, x + 8, x + 1)
            Using knobBrush As New SolidBrush(Color.White)
                g.FillRectangle(knobBrush, knobX, y + 1, 5, 6)
            End Using
        End Sub

#End Region

#Region "파라미터 박스"

        Private Sub DrawParamBoxes(g As Graphics, node As CircuitNode)
            Dim startY = node.Y + node.Height + 3
            Dim baseX = node.X
            Dim maxShow = Math.Min(node.Params.Count - 1, 3)

            For i = 0 To maxShow
                Dim param As CircuitParam = node.Params(i)
                Dim pRect As New Rectangle(baseX, startY + i * (PARAM_HEIGHT + 2), PARAM_WIDTH, PARAM_HEIGHT)

                Using fill As New SolidBrush(PARAM_FILL)
                    g.FillRectangle(fill, pRect)
                End Using
                Using border As New Pen(PARAM_BORDER, 0.5F)
                    g.DrawRectangle(border, pRect)
                End Using

                Dim iconX = pRect.X + 2
                Dim iconY = pRect.Y + 2
                ' ★ 수정: ParamDataType.DecNumber / IntNumber 사용
                If param.DataType = ParamDataType.DecNumber OrElse param.DataType = ParamDataType.IntNumber Then
                    DrawResistorIcon(g, iconX, iconY, 10, PARAM_HEIGHT - 4)
                Else
                    DrawCapacitorIcon(g, iconX, iconY, 10, PARAM_HEIGHT - 4)
                End If

                ' ★ 수정: Label / Value 사용 (Name/CurrentValue 아님)
                Dim valText = If(param.Value IsNot Nothing, param.Value.ToString(), "–")
                Dim labelText = $"{param.Label}:{valText}"
                Using brush As New SolidBrush(TEXT_LABEL)
                    g.DrawString(labelText, FONT_PARAM, brush, CSng(pRect.X + 14), CSng(pRect.Y + 2))
                End Using
            Next

            If node.Params.Count > 4 Then
                Using brush As New SolidBrush(TEXT_DIM)
                    g.DrawString($"...+{node.Params.Count - 4}", FONT_PARAM, brush,
                        CSng(baseX), CSng(startY + 4 * (PARAM_HEIGHT + 2)))
                End Using
            End If
        End Sub

        Private Sub DrawResistorIcon(g As Graphics, x As Integer, y As Integer, w As Integer, h As Integer)
            Using pen As New Pen(PARAM_BORDER, 0.8F)
                Dim midY = CSng(y + h \ 2)
                Dim pts = {
                    New PointF(CSng(x), midY),
                    New PointF(CSng(x) + CSng(w) * 0.2F, CSng(y)),
                    New PointF(CSng(x) + CSng(w) * 0.4F, CSng(y + h)),
                    New PointF(CSng(x) + CSng(w) * 0.6F, CSng(y)),
                    New PointF(CSng(x) + CSng(w) * 0.8F, CSng(y + h)),
                    New PointF(CSng(x + w), midY)
                }
                g.DrawLines(pen, pts)
            End Using
        End Sub

        Private Sub DrawCapacitorIcon(g As Graphics, x As Integer, y As Integer, w As Integer, h As Integer)
            Using pen As New Pen(PARAM_BORDER, 0.8F)
                Dim midX = x + w \ 2
                Dim midY = y + h \ 2
                g.DrawLine(pen, x, midY, midX - 2, midY)
                g.DrawLine(pen, midX - 2, y + 1, midX - 2, y + h - 1)
                g.DrawLine(pen, midX + 2, y + 1, midX + 2, y + h - 1)
                g.DrawLine(pen, midX + 2, midY, x + w, midY)
            End Using
        End Sub

#End Region

#Region "범례"

        Private Sub DrawLegend(g As Graphics, bounds As Rectangle)
            Dim x = 10
            Dim y = bounds.Height - LEGEND_Y_OFFSET

            Using bgBrush As New SolidBrush(Color.FromArgb(180, CInt(BG_COLOR.R), CInt(BG_COLOR.G), CInt(BG_COLOR.B)))
                g.FillRectangle(bgBrush, x - 2, y - 2, 500, 24)
            End Using

            DrawLED(g, x, y, LED_PASS)
            DrawLegendLabel(g, x + 14, y, "통과")
            x += 50
            DrawLED(g, x, y, LED_FAIL)
            DrawLegendLabel(g, x + 14, y, "실패")
            x += 50
            DrawLED(g, x, y, LED_WARN)
            DrawLegendLabel(g, x + 14, y, "경고")
            x += 50
            DrawLED(g, x, y, LED_OFF)
            DrawLegendLabel(g, x + 14, y, "OFF")
            x += 50

            x += 10
            Using pen As New Pen(WIRE_ACTIVE, 2.0F)
                g.DrawLine(pen, x, y + 5, x + 20, y + 5)
            End Using
            DrawLegendLabel(g, x + 24, y, "활성")
            x += 55
            Using pen As New Pen(WIRE_BLOCKED, 2.0F)
                g.DrawLine(pen, x, y + 5, x + 20, y + 5)
            End Using
            DrawLegendLabel(g, x + 24, y, "차단")
            x += 55
            Using pen As New Pen(WIRE_INACTIVE, 2.0F)
                g.DrawLine(pen, x, y + 5, x + 20, y + 5)
            End Using
            DrawLegendLabel(g, x + 24, y, "비활성")
        End Sub

        Private Sub DrawLegendLabel(g As Graphics, x As Integer, y As Integer, text As String)
            Using brush As New SolidBrush(TEXT_LABEL)
                g.DrawString(text, FONT_LEGEND, brush, CSng(x), CSng(y))
            End Using
        End Sub

#End Region

#Region "히트 테스트"

        Public Function HitTestNode(clientX As Integer, clientY As Integer) As String
            If _definition Is Nothing OrElse _definition.Nodes Is Nothing Then Return Nothing

            Dim worldX = CSng((clientX - _offset.X) / _zoom)
            Dim worldY = CSng((clientY - _offset.Y) / _zoom)

            For i = _definition.Nodes.Count - 1 To 0 Step -1
                Dim nd As CircuitNode = _definition.Nodes(i)
                If worldX >= nd.X AndAlso worldX <= nd.X + nd.Width AndAlso
                   worldY >= nd.Y AndAlso worldY <= nd.Y + nd.Height Then
                    Return nd.Id
                End If
            Next

            Return Nothing
        End Function

        Public Function HitTestGate(clientX As Integer, clientY As Integer) As String
            If _definition Is Nothing OrElse _definition.Gates Is Nothing Then Return Nothing

            Dim worldX = CSng((clientX - _offset.X) / _zoom)
            Dim worldY = CSng((clientY - _offset.Y) / _zoom)

            For Each gate In _definition.Gates
                If worldX >= gate.X AndAlso worldX <= gate.X + GATE_SIZE AndAlso
                   worldY >= gate.Y AndAlso worldY <= gate.Y + GATE_SIZE Then
                    Return gate.Id
                End If
            Next

            Return Nothing
        End Function

        Public Function HitTestParam(clientX As Integer, clientY As Integer) As Tuple(Of String, Integer)
            If _definition Is Nothing OrElse _definition.Nodes Is Nothing Then Return Nothing

            Dim worldX = CSng((clientX - _offset.X) / _zoom)
            Dim worldY = CSng((clientY - _offset.Y) / _zoom)

            For Each nd As CircuitNode In _definition.Nodes
                If nd.Params Is Nothing Then Continue For
                Dim startY = nd.Y + nd.Height + 3
                For i = 0 To Math.Min(nd.Params.Count - 1, 3)
                    Dim pRect As New RectangleF(CSng(nd.X), CSng(startY + i * (PARAM_HEIGHT + 2)),
                                                CSng(PARAM_WIDTH), CSng(PARAM_HEIGHT))
                    If worldX >= pRect.X AndAlso worldX <= pRect.Right AndAlso
                       worldY >= pRect.Y AndAlso worldY <= pRect.Bottom Then
                        Return New Tuple(Of String, Integer)(nd.Id, i)
                    End If
                Next
            Next

            Return Nothing
        End Function

#End Region

#Region "좌표 변환"

        Public Function ClientToWorld(clientPt As PointF) As PointF
            Return New PointF(
                CSng((clientPt.X - _offset.X) / _zoom),
                CSng((clientPt.Y - _offset.Y) / _zoom))
        End Function

        Public Function WorldToClient(worldPt As PointF) As PointF
            Return New PointF(
                worldPt.X * _zoom + _offset.X,
                worldPt.Y * _zoom + _offset.Y)
        End Function

#End Region

#Region "내부 헬퍼"

        Private Function FindNode(id As String) As CircuitNode
            If _definition Is Nothing OrElse _definition.Nodes Is Nothing Then Return Nothing
            Return _definition.Nodes.FirstOrDefault(Function(n) n.Id = id)
        End Function

        Private Function GetNodeLEDColor(node As CircuitNode) As Color
            If Not node.Enabled Then Return LED_DISABLED

            ' ★ 수정: NodeResults는 Dictionary(Of String, NodeEvalResult)
            If _evalResult IsNot Nothing AndAlso _evalResult.NodeResults IsNot Nothing Then
                Dim nodeResult As NodeEvalResult = Nothing
                If _evalResult.NodeResults.TryGetValue(node.Id, nodeResult) Then
                    Select Case nodeResult.Status
                        Case NodeStatus.Pass : Return LED_PASS
                        Case NodeStatus.Fail : Return LED_FAIL
                        Case NodeStatus.Warn : Return LED_WARN
                        Case NodeStatus.Off : Return LED_OFF
                    End Select
                End If
            End If

            Return LED_OFF
        End Function

        Private Function GetNodePassed(node As CircuitNode) As Boolean
            If _evalResult Is Nothing OrElse _evalResult.NodeResults Is Nothing Then Return False
            Dim nodeResult As NodeEvalResult = Nothing
            If _evalResult.NodeResults.TryGetValue(node.Id, nodeResult) Then
                Return nodeResult.Status = NodeStatus.Pass
            End If
            Return False
        End Function

        Private Function GetNodeStateText(node As CircuitNode) As String
            If _evalResult Is Nothing OrElse _evalResult.NodeResults Is Nothing Then Return ""
            Dim nodeResult As NodeEvalResult = Nothing
            If _evalResult.NodeResults.TryGetValue(node.Id, nodeResult) Then
                Return nodeResult.ValueText
            End If
            Return ""
        End Function

        Private Function GetGateResult(gateId As String) As Boolean
            If _evalResult Is Nothing OrElse _evalResult.GateResults Is Nothing Then Return False
            Dim result As Boolean = False
            _evalResult.GateResults.TryGetValue(gateId, result)
            Return result
        End Function

        Private Function GetNodeTypeIcon(nt As NodeType) As String
            Select Case nt
                Case NodeType.Input : Return "[IN]"
                Case NodeType.Indicator : Return "[IND]"
                Case NodeType.Condition : Return "[C]"
                Case NodeType.Filter : Return "[F]"
                Case NodeType.Output : Return "[OUT]"
                Case NodeType.SellPriority : Return "[P]"
                Case Else : Return "[?]"
            End Select
        End Function

        Private Shared Function CreateRoundedRect(rect As Rectangle, radius As Integer) As GraphicsPath
            Dim path As New GraphicsPath()
            Dim d = radius * 2
            If d > rect.Width Then d = rect.Width
            If d > rect.Height Then d = rect.Height

            path.AddArc(rect.X, rect.Y, d, d, 180, 90)
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
            path.CloseFigure()
            Return path
        End Function

#End Region

#Region "리소스 해제"

        Public Shared Sub DisposeStaticResources()
            FONT_TITLE.Dispose()
            FONT_NODE.Dispose()
            FONT_LABEL.Dispose()
            FONT_VALUE.Dispose()
            FONT_PROBE.Dispose()
            FONT_PARAM.Dispose()
            FONT_ZONE.Dispose()
            FONT_GATE.Dispose()
            FONT_LEGEND.Dispose()
        End Sub

#End Region

    End Class

End Namespace
