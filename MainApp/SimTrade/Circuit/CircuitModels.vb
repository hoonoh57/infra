' ═══════════════════════════════════════════════════════════════
' CircuitModels.vb — 전략 로직 회로 모델 정의
' ═══════════════════════════════════════════════════════════════

Imports System.Drawing

Namespace SimTrade.Circuit

#Region "열거형"

    Public Enum NodeType
        Indicator
        Condition
        Filter
        Gate
        SellPriority
        Output
        Input
    End Enum

    Public Enum GateType
        AND_Gate
        OR_Gate
        NOT_Gate
        PRIORITY
    End Enum

    Public Enum ParamDataType
        IntNumber
        DecNumber
        Bool
        TimeSpan
        Choice
    End Enum

    Public Enum WireState
        Inactive
        Active
        Blocked
        Warning
    End Enum

    ''' <summary>노드 평가 상태 (LED 색상 결정)</summary>
    Public Enum NodeStatus
        Off
        Pass
        Fail
        Warn
    End Enum

#End Region

#Region "CircuitParam"

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
        Public Property SettingsProperty As String = ""

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

#Region "CircuitNode"

    Public Class CircuitNode
        Public Property Id As String = ""
        Public Property Name As String = ""
        Public Property NodeType As NodeType = NodeType.Condition
        Public Property Category As String = ""
        Public Property Zone As String = ""

        Public Property Enabled As Boolean = True
        Public Property Locked As Boolean = False

        Public Property Params As New List(Of CircuitParam)

        Public Property X As Integer = 0
        Public Property Y As Integer = 0
        Public Property Width As Integer = 150
        Public Property Height As Integer = 45


        Public Property InputPorts As New List(Of String)
        Public Property OutputPorts As New List(Of String)

        Public Property CurrentValue As Object = Nothing
        Public Property IsTriggered As Boolean = False
        Public Property LastEvalTime As DateTime = DateTime.MinValue
        Public Property ProbeText As String = ""

        Public Property GateType As GateType = GateType.AND_Gate
        Public Property SellPriority As String = ""

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

#Region "CircuitWire"

    Public Class CircuitWire
        Public Property Id As String = ""
        Public Property FromNodeId As String = ""
        Public Property ToNodeId As String = ""
        Public Property State As WireState = WireState.Inactive
        Public Property SignalValue As Object = Nothing
        Public Property Label As String = ""
    End Class

#End Region

#Region "CircuitGate"

    ''' <summary>독립 게이트 객체 (렌더러가 별도 컬렉션으로 접근)</summary>
    Public Class CircuitGate
        Public Property Id As String = ""
        Public Property GateType As GateType = GateType.AND_Gate
        Public Property X As Integer = 0
        Public Property Y As Integer = 0
        Public Property Label As String = ""
    End Class

#End Region

#Region "NodeEvalResult"

    ''' <summary>개별 노드 평가 결과 (렌더러 LED/텍스트 표시용)</summary>
    Public Class NodeEvalResult
        Public Property Status As NodeStatus = NodeStatus.Off
        Public Property ValueText As String = ""
    End Class

#End Region

#Region "CircuitDefinition"

    Public Class CircuitDefinition
        Public Property Name As String = "Default Strategy"
        Public Property Version As String = "1.0"
        Public Property Description As String = ""
        Public Property CreatedAt As DateTime = DateTime.Now

        Public Property Nodes As New List(Of CircuitNode)
        Public Property Wires As New List(Of CircuitWire)
        Public Property Gates As New List(Of CircuitGate)

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

#Region "CircuitEvalResult"

    Public Class CircuitEvalResult
        Public Property BuySignal As Boolean = False
        Public Property SellSignal As Boolean = False
        Public Property SellPriority As String = ""
        Public Property BuyConditionsMet As Integer = 0
        Public Property BuyConditionsTotal As Integer = 7
        Public Property ActiveFilterBlocks As New List(Of String)
        Public Property NodeResults As New Dictionary(Of String, NodeEvalResult)
        Public Property GateResults As New Dictionary(Of String, Boolean)
        Public Property EvalTime As DateTime = DateTime.Now
    End Class

#End Region

End Namespace
