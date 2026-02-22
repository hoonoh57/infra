' Bollinger_Indicator.vb — 볼린저 밴드 (20, 2.0) + %B, BandWidth

Public Class Bollinger_Indicator
    Implements IIndicator

    Private _period As Integer = 20
    Private _stdDev As Single = 2.0F
    Private _params As New Dictionary(Of String, Object) From {{"Period", 20}, {"StdDev", 2.0F}}

    Public Sub New(Optional period As Integer = 20, Optional stdDev As Single = 2.0F)
        _period = period
        _stdDev = stdDev
        _params("Period") = _period
        _params("StdDev") = _stdDev
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"BB_{_period}"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"BB({_period},{_stdDev:F1})"
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
            If _params.ContainsKey("StdDev") Then _stdDev = CSng(_params("StdDev"))
        End Set
    End Property

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        For i = 0 To count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                .Values = New Dictionary(Of String, Single)}
            If i < _period - 1 Then
                r.Values("Middle") = Single.NaN
                r.Values("Upper") = Single.NaN
                r.Values("Lower") = Single.NaN
            Else
                Dim sum As Single = 0
                For j = i - _period + 1 To i
                    sum += candles(j).Close
                Next
                Dim sma = sum / _period
                Dim sqSum As Single = 0
                For j = i - _period + 1 To i
                    Dim diff = candles(j).Close - sma
                    sqSum += diff * diff
                Next
                Dim sd = CSng(Math.Sqrt(sqSum / _period))
                Dim upper = sma + _stdDev * sd
                Dim lower = sma - _stdDev * sd
                r.Values("Middle") = sma
                r.Values("Upper") = upper
                r.Values("Lower") = lower
            End If
            results.Add(r)
        Next
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim i = candles.Count - 1
        Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
            .Values = New Dictionary(Of String, Single)}
        If i < _period - 1 Then
            r.Values("Middle") = Single.NaN
            r.Values("Upper") = Single.NaN
            r.Values("Lower") = Single.NaN
            r.Values("PctB") = Single.NaN
            r.Values("BandWidth") = Single.NaN
            Return r
        End If
        Dim sum As Single = 0
        For j = i - _period + 1 To i
            sum += candles(j).Close
        Next
        Dim sma = sum / _period
        Dim sqSum As Single = 0
        For j = i - _period + 1 To i
            Dim diff = candles(j).Close - sma
            sqSum += diff * diff
        Next
        Dim sd = CSng(Math.Sqrt(sqSum / _period))
        Dim upper = sma + _stdDev * sd
        Dim lower = sma - _stdDev * sd
        r.Values("Middle") = sma
        r.Values("Upper") = upper
        r.Values("Lower") = lower
        Return r
    End Function
End Class
