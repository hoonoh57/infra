' ═══════════════════════════════════════════════════════════════
' Msg.vb — 프로세스 간 교환되는 유일한 메시지 객체
' ═══════════════════════════════════════════════════════════════
' 수정 금지. 모든 통신은 이 객체 하나로 이루어짐.
' ═══════════════════════════════════════════════════════════════

Public Class Msg
    Inherits Dictionary(Of String, Object)

    Public Property Topic As String = ""

    Public Sub New()
        MyBase.New(StringComparer.OrdinalIgnoreCase)
    End Sub

    Public Sub New(topic As String)
        MyBase.New(StringComparer.OrdinalIgnoreCase)
        Me.Topic = topic
    End Sub

    Public Sub New(topic As String, ParamArray pairs() As Object)
        MyBase.New(StringComparer.OrdinalIgnoreCase)
        Me.Topic = topic
        If pairs IsNot Nothing Then
            Dim i As Integer = 0
            While i < pairs.Length - 1
                Me(CStr(pairs(i))) = pairs(i + 1)
                i += 2
            End While
        End If
    End Sub

    ' ─── 타입 안전 Getter ───

    Public Function Str(key As String, Optional def As String = "") As String
        If ContainsKey(key) AndAlso Me(key) IsNot Nothing Then Return CStr(Me(key))
        Return def
    End Function

    Public Function Int(key As String, Optional def As Integer = 0) As Integer
        If Not ContainsKey(key) OrElse Me(key) Is Nothing Then Return def
        Dim v = Me(key)
        If TypeOf v Is Integer Then Return CInt(v)
        Dim r As Integer = def
        Integer.TryParse(CStr(v), r)
        Return r
    End Function

    Public Function Lng(key As String, Optional def As Long = 0) As Long
        If Not ContainsKey(key) OrElse Me(key) Is Nothing Then Return def
        Dim v = Me(key)
        If TypeOf v Is Long Then Return CLng(v)
        If TypeOf v Is Integer Then Return CLng(CInt(v))
        Dim r As Long = def
        Long.TryParse(CStr(v), r)
        Return r
    End Function

    Public Function Sng(key As String, Optional def As Single = 0) As Single
        If Not ContainsKey(key) OrElse Me(key) Is Nothing Then Return def
        Dim v = Me(key)
        If TypeOf v Is Single Then Return CSng(v)
        If TypeOf v Is Double Then Return CSng(CDbl(v))
        If TypeOf v Is Integer Then Return CSng(CInt(v))
        Dim r As Single = def
        Single.TryParse(CStr(v), r)
        Return r
    End Function

    Public Function Dbl(key As String, Optional def As Double = 0) As Double
        If Not ContainsKey(key) OrElse Me(key) Is Nothing Then Return def
        Dim v = Me(key)
        If TypeOf v Is Double Then Return CDbl(v)
        If TypeOf v Is Single Then Return CDbl(CSng(v))
        If TypeOf v Is Integer Then Return CDbl(CInt(v))
        If TypeOf v Is Long Then Return CDbl(CLng(v))
        Dim r As Double = def
        Double.TryParse(CStr(v), r)
        Return r
    End Function

    Public Function Bool(key As String, Optional def As Boolean = False) As Boolean
        If Not ContainsKey(key) OrElse Me(key) Is Nothing Then Return def
        Dim v = Me(key)
        If TypeOf v Is Boolean Then Return CBool(v)
        Dim s = CStr(v).ToLower()
        Return s = "true" OrElse s = "1" OrElse s = "yes"
    End Function

    Public Function Dt(key As String) As DateTime
        If Not ContainsKey(key) OrElse Me(key) Is Nothing Then Return DateTime.MinValue
        Return SharedUtil.ToDateTime(Me(key))
    End Function

    Public Function Arr(Of T)(key As String) As T()
        If Not ContainsKey(key) OrElse Me(key) Is Nothing Then Return Array.Empty(Of T)()
        Dim v = Me(key)
        If TypeOf v Is T() Then Return DirectCast(v, T())
        Return Array.Empty(Of T)()
    End Function

    Public Function Has(key As String) As Boolean
        Return ContainsKey(key) AndAlso Me(key) IsNot Nothing
    End Function

    Public Function Obj(Of T As Class)(key As String) As T
        If Not ContainsKey(key) OrElse Me(key) Is Nothing Then Return Nothing
        Return TryCast(Me(key), T)
    End Function

    Public Function DictList(key As String) As List(Of Dictionary(Of String, String))
        If Not ContainsKey(key) OrElse Me(key) Is Nothing Then Return New List(Of Dictionary(Of String, String))()
        Return TryCast(Me(key), List(Of Dictionary(Of String, String)))
    End Function

    ' ─── 복제 ───

    Public Function Clone() As Msg
        Dim m As New Msg(Me.Topic)
        For Each kv In Me
            m(kv.Key) = kv.Value
        Next
        Return m
    End Function
End Class
