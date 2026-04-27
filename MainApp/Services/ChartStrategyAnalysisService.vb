Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Linq
Imports System.Reflection

Public NotInheritable Class ChartStrategyAnalysisService

    Private Sub New()
    End Sub

    Public Shared Function Run(chart As FastChartControl, strategy As IStrategy) As ChartStrategyAnalysisResult
        Dim result As New ChartStrategyAnalysisResult()
        result.RunTime = DateTime.Now

        If chart Is Nothing Then
            result.Message = "차트가 없습니다."
            Return result
        End If

        If strategy Is Nothing Then
            result.Message = "전략이 없습니다."
            Return result
        End If

        result.StrategyName = strategy.Name
        result.StrategyDisplayName = strategy.DisplayName
        result.StockCode = chart.CurrentStockCode

        Dim candles As List(Of CandleItem) = GetPrivateField(Of List(Of CandleItem))(chart, "_candles")
        If candles Is Nothing OrElse candles.Count = 0 Then
            result.Message = "캔들 데이터가 없습니다."
            Return result
        End If

        result.CandleCount = candles.Count
        result.StartTimeStamp = candles(0).Dt
        result.EndTimeStamp = candles(candles.Count - 1).Dt

        Try
            chart.ReCalculate()
        Catch
        End Try

        Dim indicatorEngine As IndicatorEngine = GetPrivateField(Of IndicatorEngine)(chart, "_indicatorEngine")
        Dim indicatorResults As Dictionary(Of String, List(Of IndicatorResult)) = Nothing
        If indicatorEngine IsNot Nothing Then
            indicatorResults = indicatorEngine.Results
        End If
        If indicatorResults Is Nothing Then
            indicatorResults = New Dictionary(Of String, List(Of IndicatorResult))()
        End If

        Dim signals As List(Of StrategySignal) = Nothing
        Try
            signals = strategy.Evaluate(result.StockCode, candles, indicatorResults)
        Catch ex As Exception
            result.Message = "전략 평가 오류: " & ex.Message
            signals = New List(Of StrategySignal)()
        End Try

        If signals Is Nothing Then signals = New List(Of StrategySignal)()
        result.Signals = signals

        Try
            chart.SetStrategySignals(signals)
        Catch
        End Try

        BuildSignalTable(result, signals)
        BuildTradeTable(result, candles, signals)
        BuildSummary(result)

        If String.IsNullOrWhiteSpace(result.Message) Then
            result.Message = String.Format("분석 완료: 신호 {0}건, 거래 {1}건", result.SignalCount, result.TradeCount)
        End If

        Return result
    End Function

    Private Shared Function GetPrivateField(Of T)(target As Object, fieldName As String) As T
        If target Is Nothing Then Return Nothing
        Dim flags As BindingFlags = BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.Public
        Dim fi As FieldInfo = target.GetType().GetField(fieldName, flags)
        If fi Is Nothing Then Return Nothing
        Dim value As Object = fi.GetValue(target)
        If value Is Nothing Then Return Nothing
        If TypeOf value Is T Then Return DirectCast(value, T)
        Return Nothing
    End Function

    Private Shared Sub BuildSignalTable(result As ChartStrategyAnalysisResult, signals As List(Of StrategySignal))
        Dim table As New DataTable("Signals")
        table.Columns.Add("시간", GetType(String))
        table.Columns.Add("신호", GetType(String))
        table.Columns.Add("가격", GetType(Double))
        table.Columns.Add("신뢰도", GetType(Double))
        table.Columns.Add("전략", GetType(String))
        table.Columns.Add("사유", GetType(String))

        If signals IsNot Nothing Then
            For Each signal As StrategySignal In signals
                If signal Is Nothing Then Continue For
                table.Rows.Add(signal.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                               signal.SignalType.ToString(),
                               CDbl(signal.Price),
                               CDbl(signal.Confidence),
                               signal.StrategyName,
                               signal.Reason)
            Next
        End If

        result.SignalTable = table
    End Sub

    Private Shared Sub BuildTradeTable(result As ChartStrategyAnalysisResult,
                                       candles As List(Of CandleItem),
                                       signals As List(Of StrategySignal))
        Dim table As New DataTable("Trades")
        table.Columns.Add("매수시간", GetType(String))
        table.Columns.Add("매수가", GetType(Double))
        table.Columns.Add("매도시간", GetType(String))
        table.Columns.Add("매도가", GetType(Double))
        table.Columns.Add("수익률", GetType(Double))
        table.Columns.Add("매수후최고", GetType(Double))
        table.Columns.Add("최고수익률", GetType(Double))
        table.Columns.Add("보유분", GetType(Integer))
        table.Columns.Add("매수사유", GetType(String))
        table.Columns.Add("매도사유", GetType(String))

        If candles Is Nothing OrElse candles.Count = 0 OrElse signals Is Nothing Then
            result.TradeTable = table
            Return
        End If

        Dim orderedSignals As List(Of StrategySignal) = signals.
            Where(Function(x) x IsNot Nothing AndAlso x.SignalType <> SignalType.None).
            OrderBy(Function(x) x.Timestamp).
            ToList()

        Dim inPosition As Boolean = False
        Dim buySignal As StrategySignal = Nothing
        Dim buyPrice As Double = 0.0R

        For Each signal As StrategySignal In orderedSignals
            If signal.SignalType = SignalType.Buy OrElse signal.SignalType = SignalType.StrongBuy Then
                If Not inPosition Then
                    inPosition = True
                    buySignal = signal
                    buyPrice = CDbl(signal.Price)
                End If
            ElseIf signal.SignalType = SignalType.Sell OrElse signal.SignalType = SignalType.StrongSell Then
                If inPosition AndAlso buySignal IsNot Nothing AndAlso buyPrice > 0.0R Then
                    Dim sellPrice As Double = CDbl(signal.Price)
                    Dim retPct As Double = ((sellPrice / buyPrice) - 1.0R) * 100.0R
                    Dim maxHigh As Double = GetMaxHighAfter(candles, buySignal.Timestamp, signal.Timestamp)
                    Dim maxRetPct As Double = 0.0R
                    If maxHigh > 0.0R Then maxRetPct = ((maxHigh / buyPrice) - 1.0R) * 100.0R
                    Dim holdingMinutes As Integer = CInt(Math.Max(0.0R, (signal.Timestamp - buySignal.Timestamp).TotalMinutes))

                    table.Rows.Add(buySignal.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                                   buyPrice,
                                   signal.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                                   sellPrice,
                                   retPct,
                                   maxHigh,
                                   maxRetPct,
                                   holdingMinutes,
                                   buySignal.Reason,
                                   signal.Reason)

                    inPosition = False
                    buySignal = Nothing
                    buyPrice = 0.0R
                End If
            End If
        Next

        If inPosition AndAlso buySignal IsNot Nothing AndAlso buyPrice > 0.0R Then
            Dim lastCandle As CandleItem = candles(candles.Count - 1)
            Dim lastPrice As Double = CDbl(lastCandle.Close)
            Dim retPct As Double = ((lastPrice / buyPrice) - 1.0R) * 100.0R
            Dim maxHigh As Double = GetMaxHighAfter(candles, buySignal.Timestamp, lastCandle.Dt)
            Dim maxRetPct As Double = 0.0R
            If maxHigh > 0.0R Then maxRetPct = ((maxHigh / buyPrice) - 1.0R) * 100.0R
            Dim holdingMinutes As Integer = CInt(Math.Max(0.0R, (lastCandle.Dt - buySignal.Timestamp).TotalMinutes))

            table.Rows.Add(buySignal.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                           buyPrice,
                           "미청산",
                           lastPrice,
                           retPct,
                           maxHigh,
                           maxRetPct,
                           holdingMinutes,
                           buySignal.Reason,
                           "보유중")
        End If

        result.TradeTable = table
    End Sub

    Private Shared Function GetMaxHighAfter(candles As List(Of CandleItem), startTimeStamp As DateTime, endTimeStamp As DateTime) As Double
        Dim maxHigh As Double = 0.0R
        For Each candle As CandleItem In candles
            If candle Is Nothing Then Continue For
            If candle.Dt < startTimeStamp Then Continue For
            If candle.Dt > endTimeStamp Then Continue For
            If CDbl(candle.High) > maxHigh Then maxHigh = CDbl(candle.High)
        Next
        Return maxHigh
    End Function

    Private Shared Sub BuildSummary(result As ChartStrategyAnalysisResult)
        result.SignalCount = If(result.SignalTable IsNot Nothing, result.SignalTable.Rows.Count, 0)
        result.TradeCount = If(result.TradeTable IsNot Nothing, result.TradeTable.Rows.Count, 0)

        Dim wins As Integer = 0
        Dim sumRet As Double = 0.0R
        Dim maxRet As Double = Double.MinValue
        Dim minRet As Double = Double.MaxValue
        Dim sumWin As Double = 0.0R
        Dim sumLoss As Double = 0.0R

        If result.TradeTable IsNot Nothing Then
            For Each row As DataRow In result.TradeTable.Rows
                Dim retPct As Double = 0.0R
                If row("수익률") IsNot DBNull.Value Then retPct = CDbl(row("수익률"))
                sumRet += retPct
                If retPct > 0.0R Then
                    wins += 1
                    sumWin += retPct
                ElseIf retPct < 0.0R Then
                    sumLoss += Math.Abs(retPct)
                End If
                If retPct > maxRet Then maxRet = retPct
                If retPct < minRet Then minRet = retPct
            Next
        End If

        If result.TradeCount > 0 Then
            result.WinRate = CDbl(wins) / CDbl(result.TradeCount) * 100.0R
            result.AvgReturnPct = sumRet / CDbl(result.TradeCount)
            result.MaxReturnPct = maxRet
            result.MinReturnPct = minRet
        Else
            result.WinRate = 0.0R
            result.AvgReturnPct = 0.0R
            result.MaxReturnPct = 0.0R
            result.MinReturnPct = 0.0R
        End If

        If sumLoss > 0.0R Then
            result.ProfitFactor = sumWin / sumLoss
        ElseIf sumWin > 0.0R Then
            result.ProfitFactor = 999.0R
        Else
            result.ProfitFactor = 0.0R
        End If
    End Sub

End Class

Public Class ChartStrategyAnalysisResult
    Public Property RunTime As DateTime = DateTime.Now
    Public Property StockCode As String = ""
    Public Property StrategyName As String = ""
    Public Property StrategyDisplayName As String = ""
    Public Property CandleCount As Integer = 0
    Public Property StartTimeStamp As DateTime = DateTime.MinValue
    Public Property EndTimeStamp As DateTime = DateTime.MinValue
    Public Property SignalCount As Integer = 0
    Public Property TradeCount As Integer = 0
    Public Property WinRate As Double = 0.0R
    Public Property AvgReturnPct As Double = 0.0R
    Public Property MaxReturnPct As Double = 0.0R
    Public Property MinReturnPct As Double = 0.0R
    Public Property ProfitFactor As Double = 0.0R
    Public Property Message As String = ""
    Public Property Signals As List(Of StrategySignal) = New List(Of StrategySignal)()
    Public Property SignalTable As DataTable = New DataTable("Signals")
    Public Property TradeTable As DataTable = New DataTable("Trades")
End Class
