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
        BuildDecisionLogTable(result, candles, indicatorResults, signals)
        BuildSummary(result)

        If String.IsNullOrWhiteSpace(result.Message) Then
            result.Message = String.Format("분석 완료: 신호 {0}건, 거래 {1}건, 진단 {2}건", result.SignalCount, result.TradeCount, result.DecisionLogCount)
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

    Private Shared Sub BuildDecisionLogTable(result As ChartStrategyAnalysisResult,
                                             candles As List(Of CandleItem),
                                             indicatorResults As Dictionary(Of String, List(Of IndicatorResult)),
                                             signals As List(Of StrategySignal))
        Dim table As DataTable = StrategyDecisionLogTableBuilder.CreateTable()
        If candles Is Nothing OrElse candles.Count < 3 Then
            result.DecisionLogTable = table
            Return
        End If

        Dim signalByTime As New Dictionary(Of DateTime, StrategySignal)()
        If signals IsNot Nothing Then
            For Each signal As StrategySignal In signals
                If signal Is Nothing Then Continue For
                If Not signalByTime.ContainsKey(signal.Timestamp) Then signalByTime.Add(signal.Timestamp, signal)
            Next
        End If

        Dim i As Integer
        For i = 2 To candles.Count - 1
            Dim log As StrategyDecisionLog = BuildDecisionLogForBar(candles, indicatorResults, i)
            If log Is Nothing Then Continue For

            Dim matchedSignal As StrategySignal = Nothing
            If signalByTime.TryGetValue(log.TimeStamp, matchedSignal) AndAlso matchedSignal IsNot Nothing Then
                log.State = matchedSignal.SignalType.ToString()
                log.Reason = matchedSignal.Reason
            End If

            table.Rows.Add(log.ToDataRow(table).ItemArray)
        Next

        result.DecisionLogTable = table
    End Sub

    Private Shared Function BuildDecisionLogForBar(candles As List(Of CandleItem),
                                                    indicatorResults As Dictionary(Of String, List(Of IndicatorResult)),
                                                    index As Integer) As StrategyDecisionLog
        If candles Is Nothing OrElse index < 2 OrElse index >= candles.Count Then Return Nothing

        Dim indicatorIndex As Integer = index - 1
        Dim prevIndicatorIndex As Integer = index - 2
        Dim candle As CandleItem = candles(index)

        Dim log As New StrategyDecisionLog()
        log.TimeStamp = candle.Dt
        log.ClosePrice = CDbl(candle.Close)
        Dim dayOpen As Double = FindDayOpen(candles, index)
        log.OpenRiseRate = CalcRate(log.ClosePrice, dayOpen)

        Dim stUp As Double = GetIndicatorValue(indicatorResults, indicatorIndex, "ST", "SuperTrend", "Up")
        Dim stDown As Double = GetIndicatorValue(indicatorResults, indicatorIndex, "ST", "SuperTrend", "Down")
        Dim stValue As Double = GetIndicatorValue(indicatorResults, indicatorIndex, "ST", "SuperTrend", "Value")
        Dim stDirection As Double = GetIndicatorValue(indicatorResults, indicatorIndex, "ST", "SuperTrend", "Direction")
        log.SuperTrendBullish = IsValidLine(stUp)
        If Not log.SuperTrendBullish AndAlso Not IsValidLine(stDown) Then
            log.SuperTrendBullish = stDirection > 0.0R OrElse (IsValidLine(stValue) AndAlso log.ClosePrice >= stValue)
        End If

        Dim jmaUp As Double = GetIndicatorValue(indicatorResults, indicatorIndex, "JMA", "Up")
        Dim jmaDown As Double = GetIndicatorValue(indicatorResults, indicatorIndex, "JMA", "Down")
        Dim prevJmaDown As Double = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "JMA", "Down")
        Dim jmaValue As Double = GetIndicatorValue(indicatorResults, indicatorIndex, "JMA", "Value")
        Dim jmaSlope As Double = GetIndicatorValue(indicatorResults, indicatorIndex, "JMA", "Slope")
        Dim prevJmaSlope As Double = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "JMA", "Slope")
        log.JmaBullish = IsValidLine(jmaUp)
        log.JmaTurnUp = IsValidLine(prevJmaDown) AndAlso IsValidLine(jmaUp)
        If Not log.JmaBullish AndAlso Not IsValidLine(jmaDown) Then
            log.JmaBullish = jmaSlope > 0.0R OrElse (IsValidLine(jmaValue) AndAlso log.ClosePrice >= jmaValue)
            log.JmaTurnUp = prevJmaSlope <= 0.0R AndAlso jmaSlope > 0.0R
        End If

        Dim obv As Double = GetIndicatorValue(indicatorResults, indicatorIndex, "OBV", "OBV")
        Dim obvSignal As Double = GetIndicatorValue(indicatorResults, indicatorIndex, "OBV", "Signal")
        log.ObvBullish = Not Double.IsNaN(obv) AndAlso Not Double.IsNaN(obvSignal) AndAlso obv > obvSignal

        log.TickValue = Math.Abs(GetIndicatorValue(indicatorResults, indicatorIndex, "TICKINT", "TickSum"))
        log.TickMa5 = Math.Abs(GetIndicatorValue(indicatorResults, indicatorIndex, "TICKINT", "MA5"))
        log.TickMa20 = Math.Abs(GetIndicatorValue(indicatorResults, indicatorIndex, "TICKINT", "MA20"))
        log.RecentTickCrossBarsAgo = FindRecentTickCrossBarsAgo(indicatorResults, indicatorIndex, 6, 10.0R)
        log.TickNotCollapsed = log.TickValue >= log.TickMa5 * 0.55R

        log.IsSecondViLeaderEntry = (log.OpenRiseRate >= 11.0R AndAlso
                                     log.OpenRiseRate <= 16.0R AndAlso
                                     log.TickMa5 >= 10.0R AndAlso
                                     log.RecentTickCrossBarsAgo >= 0 AndAlso
                                     log.TickNotCollapsed)

        log.LeaderScore = 0.0R
        If log.TickMa20 > 0.0R Then log.LeaderScore += Clamp((log.TickValue / log.TickMa20) / 3.0R * 30.0R, 0.0R, 30.0R)
        If log.RecentTickCrossBarsAgo >= 0 Then log.LeaderScore += Math.Max(0.0R, 20.0R - CDbl(log.RecentTickCrossBarsAgo) * 3.0R)
        If log.ObvBullish Then log.LeaderScore += 15.0R
        If log.JmaBullish Then log.LeaderScore += 10.0R
        If log.SuperTrendBullish Then log.LeaderScore += 10.0R
        If log.IsSecondViLeaderEntry Then log.LeaderScore += 10.0R
        log.LeaderScore = Clamp(log.LeaderScore, 0.0R, 100.0R)

        log.TrendStartScore = 0.0R
        If log.JmaTurnUp Then
            log.TrendStartScore += 35.0R
        ElseIf log.JmaBullish Then
            log.TrendStartScore += 22.0R
        End If
        If log.SuperTrendBullish Then log.TrendStartScore += 25.0R
        If log.ObvBullish Then log.TrendStartScore += 20.0R
        If log.RecentTickCrossBarsAgo >= 0 Then log.TrendStartScore += 15.0R
        If log.IsSecondViLeaderEntry Then log.TrendStartScore += 10.0R
        log.TrendStartScore = Clamp(log.TrendStartScore, 0.0R, 100.0R)

        If log.IsSecondViLeaderEntry Then
            log.EntrySafetyScore = 88.0R
        ElseIf log.OpenRiseRate > 4.5R Then
            log.EntrySafetyScore = 0.0R
        Else
            log.EntrySafetyScore = 100.0R
        End If
        log.TradePriorityScore = log.LeaderScore * log.TrendStartScore * log.EntrySafetyScore / 10000.0R

        log.State = "Blocked"
        log.Reason = BuildBlockReason(log)
        If log.EntrySafetyScore > 0.0R AndAlso log.LeaderScore >= 60.0R AndAlso log.TrendStartScore >= 60.0R AndAlso log.SuperTrendBullish AndAlso log.JmaBullish AndAlso log.ObvBullish Then
            log.State = "BuyCandidate"
            log.Reason = "전략 후보 조건 근접/충족"
        End If

        Return log
    End Function

    Private Shared Function BuildBlockReason(log As StrategyDecisionLog) As String
        If log Is Nothing Then Return ""
        Dim reasons As New List(Of String)()
        If log.OpenRiseRate < 11.0R OrElse log.OpenRiseRate > 16.0R Then reasons.Add(String.Format("2차VI 구간 아님({0:0.00}%)", log.OpenRiseRate))
        If log.TickMa5 < 10.0R Then reasons.Add(String.Format("TickMA5<10({0:0.00})", log.TickMa5))
        If log.RecentTickCrossBarsAgo < 0 Then reasons.Add("최근6봉 TickCross 없음")
        If Not log.TickNotCollapsed Then reasons.Add(String.Format("Tick 붕괴(Tick={0:0.00}, MA5={1:0.00})", log.TickValue, log.TickMa5))
        If Not log.SuperTrendBullish Then reasons.Add("ST 상승 아님")
        If Not log.JmaBullish Then reasons.Add("JMA 상승 아님")
        If Not log.ObvBullish Then reasons.Add("OBV>Signal 아님")
        If reasons.Count = 0 Then Return "차단 사유 없음: 점수/세부조건 확인"
        Return String.Join(" / ", reasons)
    End Function

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
        result.DecisionLogCount = If(result.DecisionLogTable IsNot Nothing, result.DecisionLogTable.Rows.Count, 0)

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

    Private Shared Function FindDayOpen(candles As List(Of CandleItem), index As Integer) As Double
        If candles Is Nothing OrElse index < 0 OrElse index >= candles.Count Then Return 0.0R
        Dim targetDate As Date = candles(index).Dt.Date
        Dim i As Integer
        For i = index To 0 Step -1
            If candles(i).Dt.Date <> targetDate Then Exit For
            If i = 0 OrElse candles(i - 1).Dt.Date <> targetDate Then Return CDbl(candles(i).Open)
        Next
        Return CDbl(candles(index).Open)
    End Function

    Private Shared Function FindRecentTickCrossBarsAgo(indicatorResults As Dictionary(Of String, List(Of IndicatorResult)), indicatorIndex As Integer, lookback As Integer, minTickMa5 As Double) As Integer
        Dim startIndex As Integer = Math.Max(1, indicatorIndex - lookback + 1)
        Dim i As Integer
        For i = indicatorIndex To startIndex Step -1
            Dim tickValue As Double = Math.Abs(GetIndicatorValue(indicatorResults, i, "TICKINT", "TickSum"))
            Dim tickMa5Value As Double = Math.Abs(GetIndicatorValue(indicatorResults, i, "TICKINT", "MA5"))
            Dim prevTickValue As Double = Math.Abs(GetIndicatorValue(indicatorResults, i - 1, "TICKINT", "TickSum"))
            Dim prevTickMa5Value As Double = Math.Abs(GetIndicatorValue(indicatorResults, i - 1, "TICKINT", "MA5"))
            If tickMa5Value >= minTickMa5 AndAlso prevTickValue <= prevTickMa5Value AndAlso tickValue > tickMa5Value Then
                Return indicatorIndex - i
            End If
        Next
        Return -1
    End Function

    Private Shared Function GetIndicatorValue(indicatorResults As Dictionary(Of String, List(Of IndicatorResult)), index As Integer, indicatorNamePart As String, valueKey As String) As Double
        Return GetIndicatorValue(indicatorResults, index, indicatorNamePart, "", valueKey)
    End Function

    Private Shared Function GetIndicatorValue(indicatorResults As Dictionary(Of String, List(Of IndicatorResult)), index As Integer, indicatorNamePart1 As String, indicatorNamePart2 As String, valueKey As String) As Double
        If indicatorResults Is Nothing OrElse index < 0 Then Return Double.NaN
        Dim kvp As KeyValuePair(Of String, List(Of IndicatorResult))
        For Each kvp In indicatorResults
            If Not KeyMatches(kvp.Key, indicatorNamePart1, indicatorNamePart2) Then Continue For
            Dim list As List(Of IndicatorResult) = kvp.Value
            If list Is Nothing OrElse index >= list.Count Then Continue For
            Dim result As IndicatorResult = list(index)
            Dim value As Double = ReadValue(result, valueKey)
            If Not Double.IsNaN(value) Then Return value
        Next
        For Each kvp In indicatorResults
            Dim list As List(Of IndicatorResult) = kvp.Value
            If list Is Nothing OrElse index >= list.Count Then Continue For
            Dim result As IndicatorResult = list(index)
            If result Is Nothing Then Continue For
            If Not KeyMatches(result.Name, indicatorNamePart1, indicatorNamePart2) Then Continue For
            Dim value As Double = ReadValue(result, valueKey)
            If Not Double.IsNaN(value) Then Return value
        Next
        Return Double.NaN
    End Function

    Private Shared Function KeyMatches(source As String, part1 As String, part2 As String) As Boolean
        If String.IsNullOrWhiteSpace(source) Then Return False
        Dim upper As String = source.ToUpperInvariant()
        If Not String.IsNullOrWhiteSpace(part1) AndAlso upper.IndexOf(part1.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) < 0 Then Return False
        If Not String.IsNullOrWhiteSpace(part2) AndAlso upper.IndexOf(part2.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) < 0 Then Return False
        Return True
    End Function

    Private Shared Function ReadValue(result As IndicatorResult, valueKey As String) As Double
        If result Is Nothing OrElse result.Values Is Nothing Then Return Double.NaN
        If result.Values.ContainsKey(valueKey) Then
            Dim v As Single = result.Values(valueKey)
            If Single.IsNaN(v) Then Return Double.NaN
            Return CDbl(v)
        End If
        Dim kvp As KeyValuePair(Of String, Single)
        For Each kvp In result.Values
            If String.Equals(kvp.Key, valueKey, StringComparison.OrdinalIgnoreCase) Then
                If Single.IsNaN(kvp.Value) Then Return Double.NaN
                Return CDbl(kvp.Value)
            End If
        Next
        Return Double.NaN
    End Function

    Private Shared Function CalcRate(value As Double, baseValue As Double) As Double
        If value <= 0.0R OrElse baseValue <= 0.0R Then Return 0.0R
        Return ((value / baseValue) - 1.0R) * 100.0R
    End Function

    Private Shared Function IsValidLine(value As Double) As Boolean
        Return Not Double.IsNaN(value) AndAlso Not Double.IsInfinity(value)
    End Function

    Private Shared Function Clamp(value As Double, minValue As Double, maxValue As Double) As Double
        If value < minValue Then Return minValue
        If value > maxValue Then Return maxValue
        Return value
    End Function

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
    Public Property DecisionLogCount As Integer = 0
    Public Property WinRate As Double = 0.0R
    Public Property AvgReturnPct As Double = 0.0R
    Public Property MaxReturnPct As Double = 0.0R
    Public Property MinReturnPct As Double = 0.0R
    Public Property ProfitFactor As Double = 0.0R
    Public Property Message As String = ""
    Public Property Signals As List(Of StrategySignal) = New List(Of StrategySignal)()
    Public Property SignalTable As DataTable = New DataTable("Signals")
    Public Property TradeTable As DataTable = New DataTable("Trades")
    Public Property DecisionLogTable As DataTable = StrategyDecisionLogTableBuilder.CreateTable()
End Class
