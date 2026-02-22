' RSI_Indicator.vb — RSI (Wilder smoothing)

Public Class RSI_Indicator
    Implements IIndicator

    Private _period As Integer = 14
    Private _params As New Dictionary(Of String, Object) From {{"Period", 14}}
    Private _avgGain As Single = Single.NaN
    Private _avgLoss As Single = Single.NaN

    Public Sub New(Optional period As Integer = 14)
        _period = period
        _params("Period") = _period
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"RSI_{_period}"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"RSI({_period})"
        End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 1
        End Get
    End Property
    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params
        End Get
        Set(value As Dictionary(Of String, Object))
            _params = value
            If _params.ContainsKey("Period") Then _period = CInt(_params("Period"))
        End Set
    End Property

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        Dim gains(count - 1) As Single
        Dim losses(count - 1) As Single
        For i = 1 To count - 1
            Dim diff = candles(i).Close - candles(i - 1).Close
            If diff > 0 Then gains(i) = diff Else losses(i) = Math.Abs(diff)
        Next
        Dim ag As Single = 0
        Dim al As Single = 0
        For i = 0 To count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            If i < _period Then
                r.Values("Value") = Single.NaN
            ElseIf i = _period Then
                Dim sumG As Single = 0
                Dim sumL As Single = 0
                For j = 1 To _period
                    sumG += gains(j)
                    sumL += losses(j)
                Next
                ag = sumG / _period
                al = sumL / _period
                If al = 0 Then r.Values("Value") = 100 Else r.Values("Value") = 100 - 100 / (1 + ag / al)
            Else
                ag = (ag * (_period - 1) + gains(i)) / _period
                al = (al * (_period - 1) + losses(i)) / _period
                If al = 0 Then r.Values("Value") = 100 Else r.Values("Value") = 100 - 100 / (1 + ag / al)
            End If
            r.Values("Upper") = 70
            r.Values("Lower") = 30
            results.Add(r)
        Next
        _avgGain = ag
        _avgLoss = al
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim i = candles.Count - 1
        If i < _period OrElse Single.IsNaN(_avgGain) Then
            Dim full = Calculate(candles)
            If full.Count > 0 Then Return full(full.Count - 1)
            Dim emptyR As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex}
            emptyR.Values = New Dictionary(Of String, Single)
            emptyR.Values("Value") = Single.NaN
            Return emptyR
        End If
        Dim diff = candles(i).Close - candles(i - 1).Close
        Dim g As Single = If(diff > 0, diff, 0)
        Dim l As Single = If(diff < 0, Math.Abs(diff), 0)
        _avgGain = (_avgGain * (_period - 1) + g) / _period
        _avgLoss = (_avgLoss * (_period - 1) + l) / _period
        Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
            .Values = New Dictionary(Of String, Single)}
        If _avgLoss = 0 Then r.Values("Value") = 100 Else r.Values("Value") = 100 - 100 / (1 + _avgGain / _avgLoss)
        r.Values("Upper") = 70
        r.Values("Lower") = 30
        Return r
    End Function
End Class
