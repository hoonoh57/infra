' Volume_Indicator.vb — 거래량 + 이동평균

Public Class Volume_Indicator
    Implements IIndicator

    Private _period As Integer = 20
    Private _params As New Dictionary(Of String, Object) From {{"Period", 20}}

    Public Sub New(Optional period As Integer = 20)
        _period = period
        _params("Period") = _period
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"VOL_{_period}"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"Vol MA({_period})"
        End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 3
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
        Dim volSum As Single = 0
        For i = 0 To count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            volSum += CSng(candles(i).Volume)
            If i >= _period Then volSum -= CSng(candles(i - _period).Volume)
            r.Values("Volume") = CSng(candles(i).Volume)
            If i < _period - 1 Then
                r.Values("MA") = Single.NaN
                r.Values("Ratio") = Single.NaN
            Else
                Dim ma = volSum / _period
                r.Values("MA") = ma
                If ma > 0 Then
                    r.Values("Ratio") = CSng(candles(i).Volume) / ma * 100.0F
                Else
                    r.Values("Ratio") = Single.NaN
                End If
            End If
            results.Add(r)
        Next
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim i = candles.Count - 1
        Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
            .Values = New Dictionary(Of String, Single)}
        r.Values("Volume") = CSng(candles(i).Volume)
        If i < _period - 1 Then
            r.Values("MA") = Single.NaN
            r.Values("Ratio") = Single.NaN
        Else
            Dim sum As Single = 0
            For j = i - _period + 1 To i
                sum += CSng(candles(j).Volume)
            Next
            Dim ma = sum / _period
            r.Values("MA") = ma
            If ma > 0 Then
                r.Values("Ratio") = CSng(candles(i).Volume) / ma * 100.0F
            Else
                r.Values("Ratio") = Single.NaN
            End If
        End If
        Return r
    End Function
End Class
