' MA_Indicator.vb — 이동평균 (SMA/EMA/WMA)

Public Class MA_Indicator
    Implements IIndicator

    Private _period As Integer = 20
    Private _maType As String = "SMA"
    Private _params As New Dictionary(Of String, Object) From {{"Period", 20}, {"Type", "SMA"}}

    Public Sub New(Optional period As Integer = 20, Optional maType As String = "SMA")
        _period = period
        _maType = maType.ToUpper()
        _params("Period") = _period
        _params("Type") = _maType
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"{_maType}_{_period}"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"{_maType}({_period})"
        End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 0
        End Get
    End Property
    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params
        End Get
        Set(value As Dictionary(Of String, Object))
            _params = value
            If _params.ContainsKey("Period") Then _period = CInt(_params("Period"))
            If _params.ContainsKey("Type") Then _maType = _params("Type").ToString().ToUpper()
        End Set
    End Property

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        Dim vals(count - 1) As Single
        Select Case _maType
            Case "EMA"
                Dim k As Single = 2.0F / (_period + 1)
                For i = 0 To count - 1
                    If i < _period - 1 Then
                        vals(i) = Single.NaN
                    ElseIf i = _period - 1 Then
                        Dim s As Single = 0
                        For j = 0 To _period - 1
                            s += candles(j).Close
                        Next
                        vals(i) = s / _period
                    Else
                        vals(i) = candles(i).Close * k + vals(i - 1) * (1 - k)
                    End If
                Next
            Case "WMA"
                Dim denom = _period * (_period + 1) / 2.0F
                For i = 0 To count - 1
                    If i < _period - 1 Then
                        vals(i) = Single.NaN
                    Else
                        Dim s As Single = 0
                        For j = 0 To _period - 1
                            s += candles(i - _period + 1 + j).Close * (j + 1)
                        Next
                        vals(i) = s / denom
                    End If
                Next
            Case Else ' SMA
                Dim runSum As Single = 0
                For i = 0 To count - 1
                    runSum += candles(i).Close
                    If i >= _period Then runSum -= candles(i - _period).Close
                    If i >= _period - 1 Then
                        vals(i) = runSum / _period
                    Else
                        vals(i) = Single.NaN
                    End If
                Next
        End Select
        For i = 0 To count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                .Values = New Dictionary(Of String, Single)}
            r.Values("Value") = vals(i)
            results.Add(r)
        Next
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim full = Calculate(candles)
        If full.Count > 0 Then Return full(full.Count - 1)
        Dim emptyR As New IndicatorResult With {.Name = Name, .Index = candles.Count - 1, .PanelIndex = 0}
        emptyR.Values = New Dictionary(Of String, Single)
        emptyR.Values("Value") = Single.NaN
        Return emptyR
    End Function
End Class
