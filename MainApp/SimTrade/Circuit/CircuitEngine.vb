' ═══════════════════════════════════════════════════════════════
' CircuitEngine.vb — 전략 회로 실행 엔진
' ═══════════════════════════════════════════════════════════════
' CircuitDefinition을 받아 StockState에 대해 실행하고,
' 각 노드의 True/False 상태와 최종 매수/매도 출력을 반환한다.
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade.Circuit

    Public Class CircuitEngine

        Private ReadOnly _settings As SimTradeSettings
        Private _circuit As CircuitDefinition

        Public Sub New(settings As SimTradeSettings)
            _settings = settings
        End Sub

        ''' <summary>회로 정의를 로드한다.</summary>
        Public Sub LoadCircuit(circuit As CircuitDefinition)
            _circuit = circuit
        End Sub

        ''' <summary>현재 회로를 반환한다.</summary>
        Public ReadOnly Property Circuit As CircuitDefinition
            Get
                Return _circuit
            End Get
        End Property

        ''' <summary>
        ''' 회로를 실행한다.
        ''' 입력 노드 → 지표 노드 → 조건 노드 → 게이트 → 출력 순서로 평가.
        ''' Enabled=False인 노드는 항상 True를 반환 (바이패스).
        ''' </summary>
        Public Function Evaluate(state As StockState, holdingCount As Integer,
                                  cash As Long, equity As Long) As CircuitEvalResult
            Dim result As New CircuitEvalResult()
            If _circuit Is Nothing Then Return result

            ' 1단계: 모든 지표 노드 평가
            For Each node In _circuit.Nodes.Where(Function(n) n.NodeType = NodeType.Indicator)
                EvaluateIndicatorNode(node, state)
            Next

            ' 2단계: 모든 조건 노드 평가
            For Each node In _circuit.Nodes.Where(Function(n) n.NodeType = NodeType.Condition)
                EvaluateConditionNode(node, state)
            Next

            ' 3단계: 필터 노드 평가
            For Each node In _circuit.Nodes.Where(Function(n) n.NodeType = NodeType.Filter)
                EvaluateFilterNode(node, state)
                If node.IsTriggered AndAlso node.Enabled Then
                    result.ActiveFilterBlocks.Add(node.Id)
                End If
            Next

            ' 4단계: 게이트 노드 평가 (위상 정렬 순서)
            For Each node In _circuit.Nodes.Where(Function(n) n.NodeType = NodeType.Gate)
                EvaluateGateNode(node)
            Next

            ' 5단계: 매도 우선순위 평가
            For Each node In _circuit.Nodes.Where(Function(n) n.NodeType = NodeType.SellPriority).
                                                  OrderBy(Function(n) n.SellPriority)
                EvaluateSellNode(node, state)
                If node.IsTriggered AndAlso node.Enabled Then
                    result.SellSignal = True
                    result.SellPriority = node.SellPriority
                    Exit For  ' 첫 번째 트리거된 우선순위에서 중단
                End If
            Next

            ' 6단계: 출력 노드 집계
            Dim buyGate = _circuit.GetNode("GATE_BUY")
            If buyGate IsNot Nothing Then
                result.BuySignal = buyGate.IsTriggered AndAlso result.ActiveFilterBlocks.Count = 0
            End If

            ' 조건 충족 수 계산
            For Each node In _circuit.Nodes.Where(Function(n) n.NodeType = NodeType.Condition AndAlso n.Category = "매수조건")
                result.BuyConditionsTotal += 0  ' 이미 7로 초기화
                If node.IsTriggered OrElse Not node.Enabled Then
                    result.BuyConditionsMet += 1
                End If
                result.NodeResults(node.Id) = node.IsTriggered
            Next

            ' 와이어 상태 갱신
            UpdateWireStates()

            result.EvalTime = DateTime.Now
            Return result
        End Function

#Region "노드별 평가 로직"

        Private Sub EvaluateIndicatorNode(node As CircuitNode, state As StockState)
            node.LastEvalTime = DateTime.Now
            If Not node.Enabled Then
                node.CurrentValue = Nothing
                node.ProbeText = "OFF"
                Return
            End If

            Select Case node.Id
                Case "IND_ST"
                    node.CurrentValue = state.ST_Direction
                    node.IsTriggered = (state.ST_Direction > 0)
                    node.ProbeText = $"ST={state.ST_Direction:F0}"

                Case "IND_JMA"
                    node.CurrentValue = state.JMA_Direction
                    node.IsTriggered = (state.JMA_Direction > 0)
                    node.ProbeText = $"JMA={state.JMA_Direction:F0} Turn={state.JMA_TurnBar}"

                Case "IND_TICK"
                    node.CurrentValue = state.TickSum_Normalized
                    node.IsTriggered = Not Double.IsNaN(state.TickSum_Normalized)
                    node.ProbeText = $"Tick={state.TickSum_Normalized:F1}"

                Case "IND_OBV"
                    node.CurrentValue = state.OBV_Direction
                    node.IsTriggered = (state.OBV_Direction > 0)
                    node.ProbeText = $"OBV={state.OBV_Direction:F0}"

                Case "IND_RSI"
                    node.CurrentValue = state.RSI_Value
                    node.IsTriggered = Not Double.IsNaN(state.RSI_Value)
                    node.ProbeText = $"RSI={state.RSI_Value:F0}"

                Case "IND_MACD"
                    node.CurrentValue = state.MACD_Histogram
                    node.IsTriggered = (state.MACD_Histogram > 0)
                    node.ProbeText = $"MACD_H={state.MACD_Histogram:F2}"

                Case "IND_VOL"
                    node.CurrentValue = state.Volume_Ratio
                    node.IsTriggered = (state.Volume_Ratio >= 100)
                    node.ProbeText = $"Vol={state.Volume_Ratio:F0}%"
            End Select
        End Sub

        Private Sub EvaluateConditionNode(node As CircuitNode, state As StockState)
            node.LastEvalTime = DateTime.Now
            If Not node.Enabled Then
                node.IsTriggered = True  ' 바이패스: OFF면 무조건 통과
                node.ProbeText = "BYPASS"
                Return
            End If

            Select Case node.Id
                Case "C1_ST"
                    node.IsTriggered = (state.ST_Direction > 0)
                    node.ProbeText = If(node.IsTriggered, "ST▲", "ST▼")

                Case "C2_JMA"
                    Dim confirmBars = CInt(If(node.GetParam("ConfirmBars")?.Value, 2))
                    node.IsTriggered = (state.JMA_Direction > 0) AndAlso
                                       (state.JMA_TurnBar >= 0 AndAlso state.JMA_TurnBar <= confirmBars)
                    node.ProbeText = $"JMA▲ Turn={state.JMA_TurnBar}/{confirmBars}"

                Case "C3_TICK"
                    Dim threshold = CDbl(If(node.GetParam("Threshold")?.Value, 5.0))
                    node.IsTriggered = Not Double.IsNaN(state.TickSum_Normalized) AndAlso
                                       state.TickSum_Normalized >= threshold AndAlso
                                       Not Double.IsNaN(state.TickMA5_Normalized) AndAlso
                                       state.TickSum_Normalized > state.TickMA5_Normalized
                    node.ProbeText = $"Tick={state.TickSum_Normalized:F1}≥{threshold:F1}"

                Case "C4_OBV"
                    node.IsTriggered = (state.OBV_Direction > 0)
                    node.ProbeText = If(node.IsTriggered, "OBV▲", "OBV▼")

                Case "C5_CONFIRM"
                    ' C1~C4 동시 충족 (입력 와이어에서 판단)
                    Dim inputs = _circuit.GetInputWires(node.Id)
                    node.IsTriggered = inputs.All(Function(w)
                                                      Dim src = _circuit.GetNode(w.FromNodeId)
                                                      Return src IsNot Nothing AndAlso (src.IsTriggered OrElse Not src.Enabled)
                                                  End Function)
                    node.ProbeText = If(node.IsTriggered, "동시충족●", "동시충족○")

                Case "C6_MACD"
                    node.IsTriggered = (state.MACD_Histogram > 0)
                    node.ProbeText = $"MACD_H={state.MACD_Histogram:F2}"

                Case "C7_VOL"
                    node.IsTriggered = (state.Volume_Ratio >= 100)
                    node.ProbeText = $"Vol={state.Volume_Ratio:F0}%"
            End Select
        End Sub

        Private Sub EvaluateFilterNode(node As CircuitNode, state As StockState)
            node.LastEvalTime = DateTime.Now
            If Not node.Enabled Then
                node.IsTriggered = False  ' OFF → 필터 비활성 = 통과
                node.ProbeText = "OFF"
                Return
            End If
            ' 필터는 IsTriggered=True가 "위험 감지"
            ' 실제 구현은 FilterEngine 로직 재사용
            node.ProbeText = "Active"
        End Sub

        Private Sub EvaluateGateNode(node As CircuitNode)
            node.LastEvalTime = DateTime.Now
            Dim inputs = _circuit.GetInputWires(node.Id)

            Select Case node.GateType
                Case GateType.AND_Gate
                    node.IsTriggered = inputs.All(Function(w)
                                                      Dim src = _circuit.GetNode(w.FromNodeId)
                                                      Return src IsNot Nothing AndAlso (src.IsTriggered OrElse Not src.Enabled)
                                                  End Function)
                Case GateType.OR_Gate
                    node.IsTriggered = inputs.Any(Function(w)
                                                      Dim src = _circuit.GetNode(w.FromNodeId)
                                                      Return src IsNot Nothing AndAlso src.IsTriggered AndAlso src.Enabled
                                                  End Function)
                Case GateType.NOT_Gate
                    If inputs.Count > 0 Then
                        Dim src = _circuit.GetNode(inputs(0).FromNodeId)
                        node.IsTriggered = Not (src IsNot Nothing AndAlso src.IsTriggered)
                    End If
            End Select

            node.ProbeText = If(node.IsTriggered, "●PASS", "○FAIL")
        End Sub

        Private Sub EvaluateSellNode(node As CircuitNode, state As StockState)
            node.LastEvalTime = DateTime.Now
            If Not node.Enabled Then
                node.IsTriggered = False
                node.ProbeText = "OFF"
                Return
            End If
            ' P0~P8 개별 로직은 SignalEvaluator와 동일
            node.ProbeText = $"{node.SellPriority}"
        End Sub

#End Region

#Region "와이어 상태 갱신"

        Private Sub UpdateWireStates()
            If _circuit Is Nothing Then Return
            For Each wire In _circuit.Wires
                Dim src = _circuit.GetNode(wire.FromNodeId)
                If src Is Nothing Then
                    wire.State = WireState.Inactive
                ElseIf Not src.Enabled Then
                    wire.State = WireState.Inactive
                ElseIf src.IsTriggered Then
                    wire.State = WireState.Active
                Else
                    wire.State = WireState.Blocked
                End If
            Next
        End Sub

#End Region

#Region "기본 회로 생성"

        ''' <summary>v4.0 원칙서 기반 기본 회로 생성</summary>
        Public Shared Function CreateDefaultCircuit(settings As SimTradeSettings) As CircuitDefinition
            Dim c As New CircuitDefinition()
            c.Name = "v4.0 7조건 AND + P0~P8"
            c.Version = "4.2"

            ' ═══ 열 1: 지표 노드 (x=50) ═══
            Dim y = 20
            c.Nodes.Add(MakeIndicator("IND_ST", "SuperTrend", 50, y, settings))
            y += 80
            c.Nodes.Add(MakeIndicator("IND_JMA", "JMA", 50, y, settings))
            y += 80
            c.Nodes.Add(MakeIndicator("IND_TICK", "TickIntensity", 50, y, settings))
            y += 80
            c.Nodes.Add(MakeIndicator("IND_OBV", "OBV", 50, y, settings))
            y += 80
            c.Nodes.Add(MakeIndicator("IND_RSI", "RSI", 50, y, settings))
            y += 80
            c.Nodes.Add(MakeIndicator("IND_MACD", "MACD", 50, y, settings))
            y += 80
            c.Nodes.Add(MakeIndicator("IND_VOL", "Volume", 50, y, settings))

            ' ═══ 열 2: 매수 조건 노드 (x=280) ═══
            y = 20
            c.Nodes.Add(MakeCondition("C1_ST", "C1: ST▲", 280, y, "매수조건")) : y += 80
            c.Nodes.Add(MakeCondition("C2_JMA", "C2: JMA전환", 280, y, "매수조건",
                New CircuitParam() With {.Key = "ConfirmBars", .Label = "확인봉수", .DataType = ParamDataType.IntNumber,
                    .Value = settings.ConfirmBars_JMA, .DefaultValue = 2, .MinValue = 1, .MaxValue = 10,
                    .SettingsProperty = "ConfirmBars_JMA"})) : y += 80
            c.Nodes.Add(MakeCondition("C3_TICK", "C3: TickSum", 280, y, "매수조건",
                New CircuitParam() With {.Key = "Threshold", .Label = "임계값", .DataType = ParamDataType.DecNumber,
                    .Value = settings.TICKINT_Threshold, .DefaultValue = 5.0, .MinValue = 0.5, .MaxValue = 50.0,
                    .StepValue = 0.5, .SettingsProperty = "TICKINT_Threshold"})) : y += 80
            c.Nodes.Add(MakeCondition("C4_OBV", "C4: OBV▲", 280, y, "매수조건")) : y += 80
            c.Nodes.Add(MakeCondition("C5_CONFIRM", "C5: 동시확인", 280, y, "매수조건")) : y += 80
            c.Nodes.Add(MakeCondition("C6_MACD", "C6: MACD GC", 280, y, "매수조건")) : y += 80
            c.Nodes.Add(MakeCondition("C7_VOL", "C7: Volume>MA", 280, y, "매수조건"))

            ' ═══ 열 3: AND 게이트 (x=500) ═══
            Dim buyGate As New CircuitNode() With {
                .Id = "GATE_BUY", .Name = "BUY AND", .NodeType = NodeType.Gate,
                .GateType = GateType.AND_Gate, .Category = "게이트",
                .X = 500, .Y = 240, .Width = 120, .Height = 100}
            c.Nodes.Add(buyGate)

            ' ═══ 열 3: 필터 노드 (x=500, 아래쪽) ═══
            y = 400
            c.Nodes.Add(MakeFilter("F_GAP", "갭상승", 500, y)) : y += 60
            c.Nodes.Add(MakeFilter("F_FAKE", "페이크돌파", 500, y)) : y += 60
            c.Nodes.Add(MakeFilter("F_VI", "VI근접", 500, y)) : y += 60
            c.Nodes.Add(MakeFilter("F_SPREAD", "스프레드", 500, y)) : y += 60
            c.Nodes.Add(MakeFilter("F_VOLUME", "거래대금", 500, y)) : y += 60
            c.Nodes.Add(MakeFilter("F_TIME", "시간제한", 500, y))

            ' ═══ 열 4: 출력 (x=700) ═══
            c.Nodes.Add(New CircuitNode() With {
                .Id = "OUT_BUY", .Name = "매수 신호", .NodeType = NodeType.Output,
                .Category = "출력", .X = 700, .Y = 260, .Width = 120, .Height = 50})

            ' ═══ 와이어 연결 ═══
            ' 지표 → 조건
            c.Wires.Add(New CircuitWire() With {.Id = "W_ST", .FromNodeId = "IND_ST", .ToNodeId = "C1_ST"})
            c.Wires.Add(New CircuitWire() With {.Id = "W_JMA", .FromNodeId = "IND_JMA", .ToNodeId = "C2_JMA"})
            c.Wires.Add(New CircuitWire() With {.Id = "W_TICK", .FromNodeId = "IND_TICK", .ToNodeId = "C3_TICK"})
            c.Wires.Add(New CircuitWire() With {.Id = "W_OBV", .FromNodeId = "IND_OBV", .ToNodeId = "C4_OBV"})
            c.Wires.Add(New CircuitWire() With {.Id = "W_MACD", .FromNodeId = "IND_MACD", .ToNodeId = "C6_MACD"})
            c.Wires.Add(New CircuitWire() With {.Id = "W_VOL", .FromNodeId = "IND_VOL", .ToNodeId = "C7_VOL"})

            ' C1~C4 → C5(동시확인)
            c.Wires.Add(New CircuitWire() With {.Id = "W_C1C5", .FromNodeId = "C1_ST", .ToNodeId = "C5_CONFIRM"})
            c.Wires.Add(New CircuitWire() With {.Id = "W_C2C5", .FromNodeId = "C2_JMA", .ToNodeId = "C5_CONFIRM"})
            c.Wires.Add(New CircuitWire() With {.Id = "W_C3C5", .FromNodeId = "C3_TICK", .ToNodeId = "C5_CONFIRM"})
            c.Wires.Add(New CircuitWire() With {.Id = "W_C4C5", .FromNodeId = "C4_OBV", .ToNodeId = "C5_CONFIRM"})

            ' C1~C7 → BUY AND
            For Each cId In {"C1_ST", "C2_JMA", "C3_TICK", "C4_OBV", "C5_CONFIRM", "C6_MACD", "C7_VOL"}
                c.Wires.Add(New CircuitWire() With {.Id = $"W_{cId}_BUY", .FromNodeId = cId, .ToNodeId = "GATE_BUY"})
            Next

            ' BUY AND → 출력
            c.Wires.Add(New CircuitWire() With {.Id = "W_BUY_OUT", .FromNodeId = "GATE_BUY", .ToNodeId = "OUT_BUY"})

            Return c
        End Function

        Private Shared Function MakeIndicator(id As String, name As String, x As Integer, y As Integer,
                                               settings As SimTradeSettings) As CircuitNode
            Return New CircuitNode() With {
                .Id = id, .Name = name, .NodeType = NodeType.Indicator,
                .Category = "지표", .X = x, .Y = y, .Locked = True}
        End Function

        Private Shared Function MakeCondition(id As String, name As String, x As Integer, y As Integer,
                                               category As String, Optional param As CircuitParam = Nothing) As CircuitNode
            Dim n As New CircuitNode() With {
                .Id = id, .Name = name, .NodeType = NodeType.Condition,
                .Category = category, .X = x, .Y = y}
            If param IsNot Nothing Then n.Params.Add(param)
            Return n
        End Function

        Private Shared Function MakeFilter(id As String, name As String, x As Integer, y As Integer) As CircuitNode
            Return New CircuitNode() With {
                .Id = id, .Name = name, .NodeType = NodeType.Filter,
                .Category = "필터", .X = x, .Y = y, .Width = 140, .Height = 45}
        End Function

#End Region

    End Class

End Namespace
