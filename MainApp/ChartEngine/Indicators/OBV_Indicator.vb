' OBV_Indicator.vb

Public Class OBV_Indicator
    Implements IIndicator

    Private _maPeriod As Integer = 20
    Private _params As New Dictionary(Of String, Object) From {{"MAPeriod", 20}}

    Public Sub New(Optional maPeriod As Integer = 20)
        _maPeriod = maPeriod
        _params("MAPeriod") = _maPeriod
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"OBV_{_maPeriod}"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"OBV(MA{_maPeriod})"
        End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 5
        End Get
    End Property
    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params
        End Get
        Set(value As Dictionary(Of String, Object))
            _params = value
            If _params.ContainsKey("MAPeriod") Then _maPeriod = CInt(_params("MAPeriod"))
        End Set
    End Property

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        If count = 0 Then Return results
        Dim obv(count - 1) As Single
        obv(0) = CSng(candles(0).Volume)
        For i = 1 To count - 1
            If candles(i).Close > candles(i - 1).Close Then
                obv(i) = obv(i - 1) + CSng(candles(i).Volume)
            ElseIf candles(i).Close < candles(i - 1).Close Then
                obv(i) = obv(i - 1) - CSng(candles(i).Volume)
            Else
                obv(i) = obv(i - 1)
            End If
        Next
        Dim obvMA = CalcSMA(obv, _maPeriod)
        For i = 0 To count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            r.Values("OBV") = obv(i)
            r.Values("Signal") = obvMA(i)
            If Not Single.IsNaN(obvMA(i)) Then
                If obv(i) > obvMA(i) Then r.Values("Direction") = 1.0F Else r.Values("Direction") = -1.0F
            Else
                r.Values("Direction") = Single.NaN
            End If
            results.Add(r)
        Next
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim full = Calculate(candles)
        If full.Count > 0 Then Return full(full.Count - 1)
        Dim emptyR As New IndicatorResult With {.Name = Name, .Index = candles.Count - 1, .PanelIndex = PanelIndex}
        emptyR.Values = New Dictionary(Of String, Single)
        emptyR.Values("OBV") = Single.NaN
        Return emptyR
    End Function

    Private Shared Function CalcSMA(data As Single(), period As Integer) As Single()
        Dim count = data.Length
        Dim result(count - 1) As Single
        Dim sum As Single = 0
        For i = 0 To count - 1
            sum += data(i)
            If i >= period Then sum -= data(i - period)
            If i >= period - 1 Then result(i) = sum / period Else result(i) = Single.NaN
        Next
        Return result
    End Function
End Class
