' ═══════════════════════════════════════════════════════════════
' CircuitModels.vb — 전략 로직 회로 모델 정의
' ═══════════════════════════════════════════════════════════════
' 전자 회로도 비유:
'   CircuitNode   = IC 칩 (연산 블록)
'   CircuitParam  = 저항/콘덴서 (조절 가능한 값)
'   CircuitGate   = AND/OR 게이트 (조건 결합)
'   CircuitWire   = 도선 (데이터 흐름)
'   CircuitProbe  = 테스트 프로브 (실시간 값)
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing

Namespace SimTrade.Circuit

#Region "열거형"

    Public Enum NodeType
        Indicator       ' 지표 계산 (ST, JMA, RSI, MACD, OBV, TickSum, Volume)
        Condition        ' 조건 판단 (C1~C7, RSI범위, 스프레드 등)
        Filter           ' 위험 필터 (GAP, FAKE, VI, SPREAD, VOLUME, TIME)
        Gate             ' 논리 게이트 (AND, OR, NOT)
        SellPriority     ' 매도 우선순위 (P0~P8)
        Output           ' 최종 출력 (매수신호, 매도신호)
        Input            ' 입력 소스 (캔들, 틱, 호가)
    End Enum

    Public Enum GateType
        AND_Gate     ' 모든 입력 True → True
        OR_Gate      ' 하나라도 True → True
        NOT_Gate     ' 반전
        PRIORITY     ' 첫 True 입력의 우선순위 반환
    End Enum

    Public Enum ParamDataType
        IntNumber
        DecNumber
        Bool
        TimeSpan
        Choice
    End Enum

    Public Enum WireState
        Inactive     ' 신호 없음 (회색)
        Active       ' True 신호 (초록)
        Blocked      ' False 신호 (빨강)
        Warning      ' Observe 트리거 (노랑)
    End Enum

#End Region

#Region "CircuitParam — 부품 값 (저항/콘덴서)"

    ''' <summary>노드에 부착되는 조절 가능한 파라미터</summary>
    Public Class CircuitParam
        Public Property Key As String = ""
        Public Property Label As String = ""
        Public Property DataType As ParamDataType = ParamDataType.DecNumber
        Public Property Value As Object = Nothing
        Public Property DefaultValue As Object = Nothing
        Public Property MinValue As Object = Nothing
        Public Property MaxValue As Object = Nothing
        Public Property StepValue As Object = Nothing
        Public Property Choices As String() = Nothing
        Public Property Tooltip As String = ""
        Public Property SettingsProperty As String = ""  ' SimTradeSettings 프로퍼티명

        ''' <summary>현재 값을 기본값으로 리셋</summary>
        Public Sub Reset()
            Value = DefaultValue
        End Sub

        Public Function Clone() As CircuitParam
            Dim p As New CircuitParam()
            p.Key = Key : p.Label = Label : p.DataType = DataType
            p.Value = Value : p.DefaultValue = DefaultValue
            p.MinValue = MinValue : p.MaxValue = MaxValue
            p.StepValue = StepValue : p.Choices = Choices
            p.Tooltip = Tooltip : p.SettingsProperty = SettingsProperty
            Return p
        End Function
    End Class

#End Region

#Region "CircuitNode — IC 칩 (연산 블록)"

    ''' <summary>회로의 개별 노드 (지표, 조건, 필터, 게이트)</summary>
    Public Class CircuitNode
        ' ── 식별 ──
        Public Property Id As String = ""
        Public Property Name As String = ""
        Public Property NodeType As NodeType = NodeType.Condition
        Public Property Category As String = ""       ' "지표", "매수조건", "매도조건", "필터"

        ' ── 스위치 (ON/OFF) ──
        Public Property Enabled As Boolean = True
        Public Property Locked As Boolean = False     ' True면 UI에서 OFF 불가

        ' ── 파라미터 (부품 값) ──
        Public Property Params As New List(Of CircuitParam)

        ' ── 시각적 위치 ──
        Public Property X As Integer = 0
        Public Property Y As Integer = 0
        Public Property Width As Integer = 160
        Public Property Height As Integer = 60

        ' ── 입출력 포트 ──
        Public Property InputPorts As New List(Of String)    ' 연결된 입력 와이어 ID
        Public Property OutputPorts As New List(Of String)   ' 연결된 출력 와이어 ID

        ' ── 런타임 상태 (실시간 갱신) ──
        Public Property CurrentValue As Object = Nothing     ' 현재 계산 값
        Public Property IsTriggered As Boolean = False       ' 조건 충족 여부
        Public Property LastEvalTime As DateTime = DateTime.MinValue
        Public Property ProbeText As String = ""             ' 테스트 프로브 표시

        ' ── 게이트 전용 ──
        Public Property GateType As GateType = GateType.AND_Gate

        ' ── 매도 전용 ──
        Public Property SellPriority As String = ""          ' "P0"~"P8"

        ' ── 색상 ──
        Public ReadOnly Property DisplayColor As Color
            Get
                If Not Enabled Then Return Color.FromArgb(80, 80, 80)
                If IsTriggered Then Return Color.FromArgb(0, 200, 0)
                Return Color.FromArgb(60, 120, 200)
            End Get
        End Property

        Public ReadOnly Property CenterPoint As Point
            Get
                Return New Point(X + Width \ 2, Y + Height \ 2)
            End Get
        End Property

        Public Function GetParam(key As String) As CircuitParam
            Return Params.FirstOrDefault(Function(p) p.Key = key)
        End Function
    End Class

#End Region

#Region "CircuitWire — 도선 (연결)"

    ''' <summary>노드 간 데이터 연결</summary>
    Public Class CircuitWire
        Public Property Id As String = ""
        Public Property FromNodeId As String = ""
        Public Property ToNodeId As String = ""
        Public Property State As WireState = WireState.Inactive
        Public Property SignalValue As Object = Nothing
    End Class

#End Region

#Region "CircuitDefinition — 전체 회로도"

    ''' <summary>하나의 전략 회로도 전체 정의</summary>
    Public Class CircuitDefinition
        Public Property Name As String = "Default Strategy"
        Public Property Version As String = "1.0"
        Public Property Description As String = ""
        Public Property CreatedAt As DateTime = DateTime.Now

        Public Property Nodes As New List(Of CircuitNode)
        Public Property Wires As New List(Of CircuitWire)

        Public Function GetNode(id As String) As CircuitNode
            Return Nodes.FirstOrDefault(Function(n) n.Id = id)
        End Function

        Public Function GetInputWires(nodeId As String) As List(Of CircuitWire)
            Return Wires.Where(Function(w) w.ToNodeId = nodeId).ToList()
        End Function

        Public Function GetOutputWires(nodeId As String) As List(Of CircuitWire)
            Return Wires.Where(Function(w) w.FromNodeId = nodeId).ToList()
        End Function
    End Class

#End Region

#Region "CircuitEvalResult — 회로 실행 결과"

    ''' <summary>회로 전체 실행 결과</summary>
    Public Class CircuitEvalResult
        Public Property BuySignal As Boolean = False
        Public Property SellSignal As Boolean = False
        Public Property SellPriority As String = ""
        Public Property BuyConditionsMet As Integer = 0
        Public Property BuyConditionsTotal As Integer = 7
        Public Property ActiveFilterBlocks As New List(Of String)
        Public Property NodeResults As New Dictionary(Of String, Boolean)
        Public Property EvalTime As DateTime = DateTime.Now
    End Class

#End Region

End Namespace
