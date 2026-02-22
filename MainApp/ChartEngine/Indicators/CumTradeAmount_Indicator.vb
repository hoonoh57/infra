' CumTradeAmount_Indicator.vb — 당일 거래대금 누적

Public Class CumTradeAmount_Indicator
    Implements IIndicator

    Private _unitBillion As Boolean = True
    Private _params As New Dictionary(Of String, Object) From {{"UnitBillion", True}}

    Public Sub New(Optional unitBillion As Boolean = True)
        _unitBillion = unitBillion
        _params("UnitBillion") = _unitBillion
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return "CUM_TRADE_AMT"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return "당일거래대금누적"
        End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 10
        End Get
    End Property
    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params
        End Get
        Set(value As Dictionary(Of String, Object))
            _params = value
            If _params.ContainsKey("UnitBillion") Then _unitBillion = CBool(_params("UnitBillion"))
        End Set
    End Property

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        Dim cumAmount As Double = 0
        Dim lastDate As DateTime = DateTime.MinValue
        Dim candlesInDay As Integer = 0
        Dim divisor As Double = If(_unitBillion, 100000000.0, 1.0)
        For i = 0 To count - 1
            Dim c = candles(i)
            If c.Dt.Date <> lastDate.Date AndAlso lastDate <> DateTime.MinValue Then
                cumAmount = 0
                candlesInDay = 0
            End If
            lastDate = c.Dt
            candlesInDay += 1
            Dim tradeAmt As Double
            If c.TradeAmount > 0 Then
                tradeAmt = CDbl(c.TradeAmount)
            Else
                tradeAmt = CDbl(c.Close) * c.Volume
            End If
            cumAmount += tradeAmt
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            r.Values("Cumulative") = CSng(cumAmount / divisor)
            r.Values("PerCandle") = CSng(tradeAmt / divisor)
            Dim avgPerCandle As Double = 0
            If candlesInDay > 0 Then avgPerCandle = cumAmount / candlesInDay
            If avgPerCandle > 0 Then
                r.Values("Ratio") = CSng(tradeAmt / avgPerCandle * 100)
            Else
                r.Values("Ratio") = 100.0F
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
        emptyR.Values("Cumulative") = Single.NaN
        Return emptyR
    End Function
End Class
