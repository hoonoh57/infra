Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic

''' <summary>
''' 진성대장주 초입 Top3 전략.
''' 조건검색으로 압축된 종목 중에서 대장성, 대세상승 초입, 진입 안전성을 동시에 만족할 때만 매수 신호를 낸다.
''' TickIntensity는 단독 매수 조건이 아니라 LeaderScore와 TradePriorityScore를 구성하는 핵심 증거로 사용한다.
'''
''' 핵심 원칙:
''' - 현재 진행 중인 봉의 미확정 지표값을 매매 조건으로 사용하지 않는다.
''' - 평가봉 i의 가격은 사용할 수 있지만, 지표 상태/전환은 i-1, i-2의 확정 지표로 판단한다.
''' - 즉, 전봉까지 확정된 지표 + 현재봉 가격 돌파/이탈 확인 구조를 사용한다.
''' </summary>
Public Class TrueLeaderEarlyTrendStrategy
    Implements IStrategy

#Region "Parameters"
    Public Property TargetProfitPct As Double = 5.0R
    Public Property MaxBuyCount As Integer = 3
    Public Property WatchCount As Integer = 10
    Public Property MinLeaderScore As Double = 70.0R
    Public Property MinTrendStartScore As Double = 70.0R
    Public Property MinEntrySafetyScore As Double = 70.0R
    Public Property MaxOpenRiseForNewBuy As Double = 5.0R
    Public Property GapCooldownThreshold As Double = 3.0R
    Public Property PullbackConfirmPct As Double = 1.5R
    Public Property ReboundConfirmPct As Double = 0.5R
    Public Property ViTriggerPct As Double = 10.0R
    Public Property ViSafetyMarginPct As Double = 0.5R
#End Region

#Region "IStrategy"
    Public ReadOnly Property Name As String Implements IStrategy.Name
        Get
            Return "TrueLeaderEarlyTrendTop3"
        End Get
    End Property

    Public ReadOnly Property DisplayName As String Implements IStrategy.DisplayName
        Get
            Return "진성대장주 초입 Top3 전략"
        End Get
    End Property

    Public Function RequiredIndicators() As List(Of String) Implements IStrategy.RequiredIndicators
        Dim items As New List(Of String)()
        items.Add("ST_10_3.0")
        items.Add("JMA_7")
        items.Add("JMA_14")
        items.Add("OBV_20")
        items.Add("TICKINT_1")
        items.Add("TradeStrength")
        items.Add("Volume")
        Return items
    End Function

    Public Function Evaluate(stockCode As String,
                             candles As List(Of CandleItem),
                             indicatorResults As Dictionary(Of String, List(Of IndicatorResult))) As List(Of StrategySignal) Implements IStrategy.Evaluate
        Dim signals As New List(Of StrategySignal)()
        If candles Is Nothing OrElse candles.Count < 35 Then Return signals

        Dim inPosition As Boolean = False
        Dim entryPrice As Double = 0.0R
        Dim highestSinceEntry As Double = 0.0R
        Dim buyCount As Integer = 0

        Dim i As Integer
        For i = 2 To candles.Count - 1
            Dim ctx As LeaderContext = BuildContext(candles, indicatorResults, i)
            If ctx Is Nothing Then Continue For

            If inPosition Then
                If ctx.ClosePrice > highestSinceEntry Then highestSinceEntry = ctx.ClosePrice

                Dim sellReason As String = ""
                Dim sellType As SignalType = SignalType.None
                If ShouldSell(ctx, entryPrice, highestSinceEntry, sellReason, sellType) Then
                    signals.Add(CreateSignal(stockCode, candles(i), sellType, sellReason, ctx.TradePriorityScore))
                    inPosition = False
                    entryPrice = 0.0R
                    highestSinceEntry = 0.0R
                End If
            Else
                If buyCount < MaxBuyCount Then
                    Dim buyReason As String = ""
                    Dim buyType As SignalType = SignalType.None
                    If ShouldBuy(ctx, buyReason, buyType) Then
                        signals.Add(CreateSignal(stockCode, candles(i), buyType, buyReason, ctx.TradePriorityScore))
                        inPosition = True
                        entryPrice = ctx.ClosePrice
                        highestSinceEntry = ctx.ClosePrice
                        buyCount += 1
                    End If
                End If
            End If
        Next

        Return signals
    End Function
#End Region

#Region "Decision"
    Private Function ShouldBuy(ctx As LeaderContext, ByRef reason As String, ByRef signalType As SignalType) As Boolean
        reason = ""
        signalType = SignalType.None

        If ctx.ClosePrice <= 0.0R Then
            reason = "현재가 없음"
            Return False
        End If

        If ctx.EntrySafetyScore <= 0.0R Then
            reason = ctx.BlockReason
            Return False
        End If

        If ctx.LeaderScore < MinLeaderScore Then
            reason = "LeaderScore 부족"
            Return False
        End If

        If ctx.TrendStartScore < MinTrendStartScore Then
            reason = "TrendStartScore 부족"
            Return False
        End If

        If ctx.EntrySafetyScore < MinEntrySafetyScore Then
            reason = ctx.BlockReason
            If reason = "" Then reason = "EntrySafetyScore 부족"
            Return False
        End If

        If Not ctx.SuperTrendBullish Then
            reason = "전봉 기준 SuperTrend 상승 아님"
            Return False
        End If

        If Not ctx.ObvBullish Then
            reason = "전봉 기준 OBV가 Signal 아래"
            Return False
        End If

        If Not ctx.JmaTurnUp AndAlso Not ctx.JmaBullish Then
            reason = "전봉 기준 JMA 상승/재상승 확인 부족"
            Return False
        End If

        If ctx.RawTickPowerScore < 45.0R Then
            reason = "전봉 기준 TickIntensity 파생강도 부족"
            Return False
        End If

        If ctx.JmaValue > 0.0R AndAlso ctx.ClosePrice < ctx.JmaValue Then
            reason = "현재 가격이 전봉 JMA 기준선 아래"
            Return False
        End If

        reason = String.Format("BuyReady(전봉지표+현재가격): Leader={0:0.0}, Trend={1:0.0}, Safety={2:0.0}, Tick={3:0.0}",
                               ctx.LeaderScore,
                               ctx.TrendStartScore,
                               ctx.EntrySafetyScore,
                               ctx.RawTickPowerScore)
        If ctx.TradePriorityScore >= 70.0R AndAlso ctx.JmaTurnUp Then
            signalType = SignalType.StrongBuy
        Else
            signalType = SignalType.Buy
        End If
        Return True
    End Function

    Private Function ShouldSell(ctx As LeaderContext,
                                entryPrice As Double,
                                highestSinceEntry As Double,
                                ByRef reason As String,
                                ByRef signalType As SignalType) As Boolean
        reason = ""
        signalType = SignalType.None
        If entryPrice <= 0.0R Then Return False

        Dim profitPct As Double = ((ctx.ClosePrice / entryPrice) - 1.0R) * 100.0R

        If ctx.SuperTrendTurnDown Then
            reason = String.Format("전봉 기준 SuperTrend 하락전환 방어매도, 수익률={0:0.00}%", profitPct)
            signalType = SignalType.StrongSell
            Return True
        End If

        If ctx.OpenRiseRate >= (ViTriggerPct - ViSafetyMarginPct) AndAlso profitPct > 0.0R Then
            reason = String.Format("VI 접근 위험회피 매도, 시가대비={0:0.00}%, 수익률={1:0.00}%", ctx.OpenRiseRate, profitPct)
            signalType = SignalType.Sell
            Return True
        End If

        If profitPct >= TargetProfitPct AndAlso ctx.JmaTurnDown Then
            reason = String.Format("목표수익 달성 후 전봉 기준 JMA 하락전환, 수익률={0:0.00}%", profitPct)
            signalType = SignalType.Sell
            Return True
        End If

        If profitPct < TargetProfitPct AndAlso ctx.SuperTrendBullish Then
            reason = String.Format("목표 전 ST 상승 유지로 보유, 수익률={0:0.00}%", profitPct)
            Return False
        End If

        Return False
    End Function
#End Region

#Region "Context"
    Private Function BuildContext(candles As List(Of CandleItem),
                                  indicatorResults As Dictionary(Of String, List(Of IndicatorResult)),
                                  index As Integer) As LeaderContext
        If index < 2 OrElse index >= candles.Count Then Return Nothing

        Dim indicatorIndex As Integer = index - 1
        Dim prevIndicatorIndex As Integer = index - 2
        Dim c As CandleItem = candles(index)
        Dim ctx As New LeaderContext()
        ctx.Index = index
        ctx.IndicatorIndex = indicatorIndex
        ctx.PrevIndicatorIndex = prevIndicatorIndex
        ctx.TimeStamp = c.Dt
        ctx.ClosePrice = CDbl(c.Close)
        ctx.DayOpen = FindDayOpen(candles, index)
        ctx.DayHigh = FindDayHigh(candles, index)
        ctx.OpenRiseRate = CalcRate(ctx.ClosePrice, ctx.DayOpen)
        ctx.GapRate = CalcGapRate(candles, index, ctx.DayOpen)
        ctx.PullbackRate = CalcPullback(ctx.ClosePrice, ctx.DayHigh)

        ctx.SuperTrendDirection = GetIndicatorValue(indicatorResults, indicatorIndex, "ST", "SuperTrend", "Direction")
        ctx.SuperTrendValue = GetIndicatorValue(indicatorResults, indicatorIndex, "ST", "SuperTrend", "Value")
        ctx.PrevSuperTrendDirection = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "ST", "SuperTrend", "Direction")
        ctx.SuperTrendBullish = (ctx.SuperTrendDirection > 0.0R OrElse (ctx.SuperTrendValue > 0.0R AndAlso ctx.ClosePrice >= ctx.SuperTrendValue))
        ctx.SuperTrendTurnDown = (ctx.PrevSuperTrendDirection > 0.0R AndAlso ctx.SuperTrendDirection < 0.0R)

        ctx.JmaValue = GetIndicatorValue(indicatorResults, indicatorIndex, "JMA", "Value")
        ctx.JmaSlope = GetIndicatorValue(indicatorResults, indicatorIndex, "JMA", "Slope")
        ctx.PrevJmaSlope = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "JMA", "Slope")
        ctx.JmaBullish = (ctx.JmaSlope > 0.0R OrElse (ctx.JmaValue > 0.0R AndAlso ctx.ClosePrice >= ctx.JmaValue))
        ctx.JmaTurnUp = (ctx.PrevJmaSlope <= 0.0R AndAlso ctx.JmaSlope > 0.0R)
        ctx.JmaTurnDown = (ctx.PrevJmaSlope >= 0.0R AndAlso ctx.JmaSlope < 0.0R)

        ctx.Obv = GetIndicatorValue(indicatorResults, indicatorIndex, "OBV", "OBV")
        ctx.ObvSignal = GetIndicatorValue(indicatorResults, indicatorIndex, "OBV", "Signal")
        ctx.PrevObv = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "OBV", "OBV")
        ctx.ObvBullish = (Not Double.IsNaN(ctx.Obv) AndAlso Not Double.IsNaN(ctx.ObvSignal) AndAlso ctx.Obv > ctx.ObvSignal)
        ctx.ObvImproving = (Not Double.IsNaN(ctx.PrevObv) AndAlso ctx.Obv > ctx.PrevObv)

        ctx.TickSum = Math.Abs(GetIndicatorValue(indicatorResults, indicatorIndex, "TICKINT", "TickSum"))
        ctx.TickMa5 = Math.Abs(GetIndicatorValue(indicatorResults, indicatorIndex, "TICKINT", "MA5"))
        ctx.TickMa20 = Math.Abs(GetIndicatorValue(indicatorResults, indicatorIndex, "TICKINT", "MA20"))
        ctx.PrevTickSum = Math.Abs(GetIndicatorValue(indicatorResults, prevIndicatorIndex, "TICKINT", "TickSum"))
        ctx.TickRatio = SafeDiv(ctx.TickSum, ctx.TickMa20)
        ctx.TickSlope = ctx.TickSum - ctx.TickMa5
        ctx.TickAccel = (ctx.TickSum - ctx.PrevTickSum)
        ctx.TickPersist = CountTickPersist(indicatorResults, indicatorIndex, 5)

        ctx.TurnoverAccel = CalcTurnoverAccel(candles, indicatorIndex)
        ctx.PriceAccel = CalcPriceAccel(candles, index)

        ctx.RawTickPowerScore = CalcRawTickPower(ctx)
        ctx.LeaderScore = CalcLeaderScore(ctx)
        ctx.TrendStartScore = CalcTrendStartScore(ctx)
        ctx.EntrySafetyScore = CalcEntrySafetyScore(ctx)
        ctx.TradePriorityScore = ctx.LeaderScore * ctx.TrendStartScore * ctx.EntrySafetyScore / 10000.0R
        Return ctx
    End Function

    Private Function CalcRawTickPower(ctx As LeaderContext) As Double
        Dim ratioScore As Double = Clamp(ctx.TickRatio / 3.0R * 40.0R, 0.0R, 40.0R)
        Dim slopeScore As Double = If(ctx.TickSlope > 0.0R, 20.0R, 0.0R)
        Dim accelScore As Double = If(ctx.TickAccel > 0.0R, 15.0R, 0.0R)
        Dim persistScore As Double = Clamp(CDbl(ctx.TickPersist) / 5.0R * 15.0R, 0.0R, 15.0R)
        Dim turnoverScore As Double = Clamp(ctx.TurnoverAccel / 3.0R * 10.0R, 0.0R, 10.0R)
        Return ratioScore + slopeScore + accelScore + persistScore + turnoverScore
    End Function

    Private Function CalcLeaderScore(ctx As LeaderContext) As Double
        Dim score As Double = 0.0R
        score += Clamp(ctx.RawTickPowerScore, 0.0R, 55.0R)
        If ctx.ObvBullish Then score += 15.0R
        If ctx.ObvImproving Then score += 5.0R
        If ctx.JmaBullish Then score += 10.0R
        If ctx.SuperTrendBullish Then score += 10.0R
        If ctx.TurnoverAccel >= 1.5R Then score += 5.0R
        Return Clamp(score, 0.0R, 100.0R)
    End Function

    Private Function CalcTrendStartScore(ctx As LeaderContext) As Double
        Dim score As Double = 0.0R
        If ctx.JmaTurnUp Then
            score += 35.0R
        ElseIf ctx.JmaBullish Then
            score += 22.0R
        End If
        If ctx.SuperTrendBullish Then score += 25.0R
        If ctx.ObvBullish Then score += 20.0R
        If ctx.TickRatio >= 1.3R AndAlso ctx.TickSlope > 0.0R Then score += 15.0R
        If ctx.PriceAccel > 0.0R Then score += 5.0R
        Return Clamp(score, 0.0R, 100.0R)
    End Function

    Private Function CalcEntrySafetyScore(ctx As LeaderContext) As Double
        Dim score As Double = 100.0R
        Dim dynamicMaxOpenRise As Double = Math.Min(MaxOpenRiseForNewBuy, ViTriggerPct - TargetProfitPct - ViSafetyMarginPct)
        If dynamicMaxOpenRise < 0.0R Then dynamicMaxOpenRise = 0.0R

        If ctx.OpenRiseRate > dynamicMaxOpenRise Then
            ctx.BlockReason = String.Format("시가대비 {0:0.00}%: 목표수익 공간/VI 위험", ctx.OpenRiseRate)
            Return 0.0R
        End If

        If ctx.GapRate >= GapCooldownThreshold AndAlso ctx.PullbackRate < PullbackConfirmPct Then
            ctx.BlockReason = String.Format("갭상승 {0:0.00}% 후 조정 미확인", ctx.GapRate)
            Return 20.0R
        End If

        If ctx.OpenRiseRate >= 3.5R AndAlso ctx.PullbackRate < 0.5R Then
            ctx.BlockReason = "당일고점 근접 고점추격 위험"
            Return 30.0R
        End If

        If ctx.PullbackRate >= PullbackConfirmPct AndAlso ctx.TickRatio >= 1.3R AndAlso ctx.JmaBullish Then
            score += 10.0R
        End If

        ctx.BlockReason = ""
        Return Clamp(score, 0.0R, 100.0R)
    End Function
#End Region

#Region "Indicator Helpers"
    Private Shared Function GetIndicatorValue(indicatorResults As Dictionary(Of String, List(Of IndicatorResult)),
                                               index As Integer,
                                               indicatorNamePart As String,
                                               valueKey As String) As Double
        Return GetIndicatorValue(indicatorResults, index, indicatorNamePart, "", valueKey)
    End Function

    Private Shared Function GetIndicatorValue(indicatorResults As Dictionary(Of String, List(Of IndicatorResult)),
                                               index As Integer,
                                               indicatorNamePart1 As String,
                                               indicatorNamePart2 As String,
                                               valueKey As String) As Double
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
#End Region

#Region "Math Helpers"
    Private Shared Function CreateSignal(stockCode As String,
                                         candle As CandleItem,
                                         signalType As SignalType,
                                         reason As String,
                                         tradePriorityScore As Double) As StrategySignal
        Dim signal As New StrategySignal()
        signal.StockCode = stockCode
        signal.StrategyName = "TrueLeaderEarlyTrendTop3"
        signal.SignalType = signalType
        signal.Price = candle.Close
        signal.Timestamp = candle.Dt
        signal.Reason = reason
        signal.Confidence = CSng(Clamp(tradePriorityScore / 100.0R, 0.0R, 1.0R))
        Return signal
    End Function

    Private Shared Function FindDayOpen(candles As List(Of CandleItem), index As Integer) As Double
        If candles Is Nothing OrElse index < 0 OrElse index >= candles.Count Then Return 0.0R
        Dim targetDate As Date = candles(index).Dt.Date
        Dim i As Integer
        For i = index To 0 Step -1
            If candles(i).Dt.Date <> targetDate Then Exit For
            If i = 0 OrElse candles(i - 1).Dt.Date <> targetDate Then
                Return CDbl(candles(i).Open)
            End If
        Next
        Return CDbl(candles(index).Open)
    End Function

    Private Shared Function FindDayHigh(candles As List(Of CandleItem), index As Integer) As Double
        Dim highValue As Double = 0.0R
        If candles Is Nothing OrElse index < 0 OrElse index >= candles.Count Then Return highValue
        Dim targetDate As Date = candles(index).Dt.Date
        Dim i As Integer
        For i = index To 0 Step -1
            If candles(i).Dt.Date <> targetDate Then Exit For
            If CDbl(candles(i).High) > highValue Then highValue = CDbl(candles(i).High)
        Next
        Return highValue
    End Function

    Private Shared Function CalcGapRate(candles As List(Of CandleItem), index As Integer, dayOpen As Double) As Double
        If candles Is Nothing OrElse index <= 0 OrElse dayOpen <= 0.0R Then Return 0.0R
        Dim targetDate As Date = candles(index).Dt.Date
        Dim i As Integer
        For i = index To 0 Step -1
            If candles(i).Dt.Date <> targetDate Then
                Dim prevClose As Double = CDbl(candles(i).Close)
                Return CalcRate(dayOpen, prevClose)
            End If
        Next
        Return 0.0R
    End Function

    Private Shared Function CalcTurnoverAccel(candles As List(Of CandleItem), index As Integer) As Double
        If candles Is Nothing OrElse index < 5 OrElse index >= candles.Count Then Return 0.0R
        Dim currentTurnover As Double = GetTurnover(candles(index))
        Dim sum As Double = 0.0R
        Dim count As Integer = 0
        Dim startIndex As Integer = Math.Max(0, index - 20)
        Dim i As Integer
        For i = startIndex To index - 1
            sum += GetTurnover(candles(i))
            count += 1
        Next
        If count <= 0 OrElse sum <= 0.0R Then Return 0.0R
        Return currentTurnover / (sum / CDbl(count))
    End Function

    Private Shared Function GetTurnover(candle As CandleItem) As Double
        If candle Is Nothing Then Return 0.0R
        If candle.TradeAmount > 0 Then Return CDbl(candle.TradeAmount)
        Return CDbl(candle.Close) * CDbl(Math.Max(0L, candle.Volume))
    End Function

    Private Shared Function CalcPriceAccel(candles As List(Of CandleItem), index As Integer) As Double
        If candles Is Nothing OrElse index < 3 Then Return 0.0R
        Dim nowSlope As Double = CalcRate(CDbl(candles(index).Close), CDbl(candles(index - 1).Close))
        Dim prevSlope As Double = CalcRate(CDbl(candles(index - 1).Close), CDbl(candles(index - 2).Close))
        Return nowSlope - prevSlope
    End Function

    Private Shared Function CountTickPersist(indicatorResults As Dictionary(Of String, List(Of IndicatorResult)), index As Integer, lookback As Integer) As Integer
        Dim count As Integer = 0
        Dim startIndex As Integer = Math.Max(0, index - lookback + 1)
        Dim i As Integer
        For i = startIndex To index
            Dim tickSum As Double = Math.Abs(GetIndicatorValue(indicatorResults, i, "TICKINT", "TickSum"))
            Dim tickMa20 As Double = Math.Abs(GetIndicatorValue(indicatorResults, i, "TICKINT", "MA20"))
            If tickMa20 > 0.0R AndAlso tickSum / tickMa20 >= 1.2R Then count += 1
        Next
        Return count
    End Function

    Private Shared Function CalcRate(value As Double, baseValue As Double) As Double
        If value <= 0.0R OrElse baseValue <= 0.0R Then Return 0.0R
        Return ((value / baseValue) - 1.0R) * 100.0R
    End Function

    Private Shared Function CalcPullback(price As Double, highValue As Double) As Double
        If price <= 0.0R OrElse highValue <= 0.0R Then Return 0.0R
        Return ((highValue - price) / highValue) * 100.0R
    End Function

    Private Shared Function SafeDiv(value As Double, baseValue As Double) As Double
        If value <= 0.0R OrElse baseValue <= 0.0R Then Return 0.0R
        Return value / baseValue
    End Function

    Private Shared Function Clamp(value As Double, minValue As Double, maxValue As Double) As Double
        If value < minValue Then Return minValue
        If value > maxValue Then Return maxValue
        Return value
    End Function
#End Region

#Region "Internal Context"
    Private Class LeaderContext
        Public Property Index As Integer = 0
        Public Property IndicatorIndex As Integer = 0
        Public Property PrevIndicatorIndex As Integer = 0
        Public Property TimeStamp As DateTime = DateTime.MinValue
        Public Property ClosePrice As Double = 0.0R
        Public Property DayOpen As Double = 0.0R
        Public Property DayHigh As Double = 0.0R
        Public Property OpenRiseRate As Double = 0.0R
        Public Property GapRate As Double = 0.0R
        Public Property PullbackRate As Double = 0.0R
        Public Property SuperTrendValue As Double = Double.NaN
        Public Property SuperTrendDirection As Double = Double.NaN
        Public Property PrevSuperTrendDirection As Double = Double.NaN
        Public Property SuperTrendBullish As Boolean = False
        Public Property SuperTrendTurnDown As Boolean = False
        Public Property JmaValue As Double = Double.NaN
        Public Property JmaSlope As Double = Double.NaN
        Public Property PrevJmaSlope As Double = Double.NaN
        Public Property JmaBullish As Boolean = False
        Public Property JmaTurnUp As Boolean = False
        Public Property JmaTurnDown As Boolean = False
        Public Property Obv As Double = Double.NaN
        Public Property ObvSignal As Double = Double.NaN
        Public Property PrevObv As Double = Double.NaN
        Public Property ObvBullish As Boolean = False
        Public Property ObvImproving As Boolean = False
        Public Property TickSum As Double = 0.0R
        Public Property TickMa5 As Double = 0.0R
        Public Property TickMa20 As Double = 0.0R
        Public Property PrevTickSum As Double = 0.0R
        Public Property TickRatio As Double = 0.0R
        Public Property TickSlope As Double = 0.0R
        Public Property TickAccel As Double = 0.0R
        Public Property TickPersist As Integer = 0
        Public Property TurnoverAccel As Double = 0.0R
        Public Property PriceAccel As Double = 0.0R
        Public Property RawTickPowerScore As Double = 0.0R
        Public Property LeaderScore As Double = 0.0R
        Public Property TrendStartScore As Double = 0.0R
        Public Property EntrySafetyScore As Double = 0.0R
        Public Property TradePriorityScore As Double = 0.0R
        Public Property BlockReason As String = ""
    End Class
#End Region

End Class
