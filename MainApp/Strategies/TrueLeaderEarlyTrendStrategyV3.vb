Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic

''' <summary>
''' 진성대장주 초입 Top3 전략 V3.
''' V2의 1봉 TickCrossUp 조건이 너무 좁아 실제 2차 VI 진입구간에서 신호가 0건이 되는 문제를 보완한다.
'''
''' 핵심:
''' - 현재봉 미확정 지표값 사용 금지
''' - 지표 판단은 i-1, 전환 판단은 i-2 이하 확정봉 기준
''' - 2차 VI 대장주: 시가대비 11~16%, TickMA5 >= 10, 최근 N봉 내 TickIntensity가 TickMA5를 상승돌파,
'''   현재 TickIntensity가 TickMA5 대비 완전 붕괴하지 않은 경우만 허용
''' </summary>
Public Class TrueLeaderEarlyTrendStrategyV3
    Implements IStrategy

#Region "Parameters"
    Public Property TargetProfitPct As Double = 5.0R
    Public Property MaxBuyCount As Integer = 3
    Public Property MinLeaderScore As Double = 60.0R
    Public Property MinTrendStartScore As Double = 60.0R
    Public Property MinEntrySafetyScore As Double = 65.0R
    Public Property MaxOpenRiseForNewBuy As Double = 5.0R
    Public Property GapCooldownThreshold As Double = 3.0R
    Public Property PullbackConfirmPct As Double = 1.5R
    Public Property ViTriggerPct As Double = 10.0R
    Public Property ViSafetyMarginPct As Double = 0.5R
    Public Property SecondViLeaderMinOpenRise As Double = 11.0R
    Public Property SecondViLeaderMaxOpenRise As Double = 16.0R
    Public Property SecondViMinTickMa5 As Double = 10.0R
    Public Property RecentTickCrossLookback As Integer = 6
    Public Property TickCollapseRatio As Double = 0.55R
    Public Property SecondViEntrySafetyScore As Double = 88.0R
#End Region

#Region "IStrategy"
    Public ReadOnly Property Name As String Implements IStrategy.Name
        Get
            Return "TrueLeaderEarlyTrendTop3V3"
        End Get
    End Property

    Public ReadOnly Property DisplayName As String Implements IStrategy.DisplayName
        Get
            Return "진성대장주 초입 Top3 전략 V3 - RecentTickCross"
        End Get
    End Property

    Public Function RequiredIndicators() As List(Of String) Implements IStrategy.RequiredIndicators
        Dim items As New List(Of String)()
        items.Add("ST_10_3.0")
        items.Add("JMA_7")
        items.Add("JMA_14")
        items.Add("OBV_20")
        items.Add("TICKINT_1")
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
        Dim buyCount As Integer = 0

        Dim i As Integer
        For i = 2 To candles.Count - 1
            Dim ctx As LeaderContext = BuildContext(candles, indicatorResults, i)
            If ctx Is Nothing Then Continue For

            If inPosition Then
                Dim sellReason As String = ""
                Dim sellType As SignalType = SignalType.None
                If ShouldSell(ctx, entryPrice, sellReason, sellType) Then
                    signals.Add(CreateSignal(stockCode, candles(i), sellType, sellReason, ctx.TradePriorityScore))
                    inPosition = False
                    entryPrice = 0.0R
                End If
            Else
                If buyCount < MaxBuyCount Then
                    Dim buyReason As String = ""
                    Dim buyType As SignalType = SignalType.None
                    If ShouldBuy(ctx, buyReason, buyType) Then
                        signals.Add(CreateSignal(stockCode, candles(i), buyType, buyReason, ctx.TradePriorityScore))
                        inPosition = True
                        entryPrice = ctx.ClosePrice
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

        If ctx.EntrySafetyScore <= 0.0R Then
            reason = ctx.BlockReason
            Return False
        End If

        If ctx.LeaderScore < MinLeaderScore Then
            reason = String.Format("LeaderScore 부족: {0:0.0}", ctx.LeaderScore)
            Return False
        End If

        If ctx.TrendStartScore < MinTrendStartScore Then
            reason = String.Format("TrendStartScore 부족: {0:0.0}", ctx.TrendStartScore)
            Return False
        End If

        If ctx.EntrySafetyScore < MinEntrySafetyScore Then
            reason = If(ctx.BlockReason, "EntrySafetyScore 부족")
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

        If IsValidLine(ctx.JmaActiveValue) AndAlso ctx.ClosePrice < ctx.JmaActiveValue Then
            reason = "현재 가격이 전봉 JMA 상승 기준선 아래"
            Return False
        End If

        If ctx.IsSecondViLeaderEntry Then
            reason = String.Format("SecondVI RecentTickCross BuyReady: OpenRise={0:0.00}%, Tick={1:0.00}, TickMA5={2:0.00}, RecentCross={3}, Leader={4:0.0}, Trend={5:0.0}, Safety={6:0.0}",
                                   ctx.OpenRiseRate,
                                   ctx.TickSum,
                                   ctx.TickMa5,
                                   ctx.RecentTickCrossBarsAgo,
                                   ctx.LeaderScore,
                                   ctx.TrendStartScore,
                                   ctx.EntrySafetyScore)
            signalType = SignalType.StrongBuy
            Return True
        End If

        If ctx.RawTickPowerScore < 40.0R Then
            reason = String.Format("전봉 기준 TickIntensity 파생강도 부족: {0:0.0}", ctx.RawTickPowerScore)
            Return False
        End If

        reason = String.Format("BuyReady: Leader={0:0.0}, Trend={1:0.0}, Safety={2:0.0}, TickPower={3:0.0}",
                               ctx.LeaderScore,
                               ctx.TrendStartScore,
                               ctx.EntrySafetyScore,
                               ctx.RawTickPowerScore)
        If ctx.TradePriorityScore >= 70.0R Then
            signalType = SignalType.StrongBuy
        Else
            signalType = SignalType.Buy
        End If
        Return True
    End Function

    Private Function ShouldSell(ctx As LeaderContext,
                                entryPrice As Double,
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

        If ctx.OpenRiseRate > SecondViLeaderMaxOpenRise AndAlso profitPct > 0.0R Then
            reason = String.Format("2차 VI 허용상한 초과 위험회피 매도, 시가대비={0:0.00}%, 수익률={1:0.00}%", ctx.OpenRiseRate, profitPct)
            signalType = SignalType.Sell
            Return True
        End If

        If profitPct >= TargetProfitPct AndAlso ctx.JmaTurnDown Then
            reason = String.Format("목표수익 달성 후 JMA 하락전환, 수익률={0:0.00}%", profitPct)
            signalType = SignalType.Sell
            Return True
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

        ctx.SuperTrendUp = GetIndicatorValue(indicatorResults, indicatorIndex, "ST", "SuperTrend", "Up")
        ctx.SuperTrendDown = GetIndicatorValue(indicatorResults, indicatorIndex, "ST", "SuperTrend", "Down")
        ctx.PrevSuperTrendUp = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "ST", "SuperTrend", "Up")
        ctx.PrevSuperTrendDown = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "ST", "SuperTrend", "Down")
        ctx.SuperTrendValue = GetIndicatorValue(indicatorResults, indicatorIndex, "ST", "SuperTrend", "Value")
        ctx.SuperTrendDirection = GetIndicatorValue(indicatorResults, indicatorIndex, "ST", "SuperTrend", "Direction")
        ctx.PrevSuperTrendDirection = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "ST", "SuperTrend", "Direction")
        ctx.SuperTrendBullish = IsValidLine(ctx.SuperTrendUp)
        ctx.SuperTrendBearish = IsValidLine(ctx.SuperTrendDown)
        ctx.SuperTrendTurnDown = IsValidLine(ctx.PrevSuperTrendUp) AndAlso IsValidLine(ctx.SuperTrendDown)
        If Not ctx.SuperTrendBullish AndAlso Not ctx.SuperTrendBearish Then
            ctx.SuperTrendBullish = ctx.SuperTrendDirection > 0.0R OrElse (IsValidLine(ctx.SuperTrendValue) AndAlso ctx.ClosePrice >= ctx.SuperTrendValue)
            ctx.SuperTrendTurnDown = ctx.PrevSuperTrendDirection > 0.0R AndAlso ctx.SuperTrendDirection < 0.0R
        End If

        ctx.JmaUp = GetIndicatorValue(indicatorResults, indicatorIndex, "JMA", "Up")
        ctx.JmaDown = GetIndicatorValue(indicatorResults, indicatorIndex, "JMA", "Down")
        ctx.PrevJmaUp = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "JMA", "Up")
        ctx.PrevJmaDown = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "JMA", "Down")
        ctx.JmaValue = GetIndicatorValue(indicatorResults, indicatorIndex, "JMA", "Value")
        ctx.JmaSlope = GetIndicatorValue(indicatorResults, indicatorIndex, "JMA", "Slope")
        ctx.PrevJmaSlope = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "JMA", "Slope")
        ctx.JmaBullish = IsValidLine(ctx.JmaUp)
        ctx.JmaBearish = IsValidLine(ctx.JmaDown)
        ctx.JmaTurnUp = IsValidLine(ctx.PrevJmaDown) AndAlso IsValidLine(ctx.JmaUp)
        ctx.JmaTurnDown = IsValidLine(ctx.PrevJmaUp) AndAlso IsValidLine(ctx.JmaDown)
        If ctx.JmaBullish Then
            ctx.JmaActiveValue = ctx.JmaUp
        ElseIf IsValidLine(ctx.JmaValue) Then
            ctx.JmaActiveValue = ctx.JmaValue
        End If
        If Not ctx.JmaBullish AndAlso Not ctx.JmaBearish Then
            ctx.JmaBullish = ctx.JmaSlope > 0.0R OrElse (IsValidLine(ctx.JmaValue) AndAlso ctx.ClosePrice >= ctx.JmaValue)
            ctx.JmaTurnUp = ctx.PrevJmaSlope <= 0.0R AndAlso ctx.JmaSlope > 0.0R
            ctx.JmaTurnDown = ctx.PrevJmaSlope >= 0.0R AndAlso ctx.JmaSlope < 0.0R
            ctx.JmaActiveValue = ctx.JmaValue
        End If

        ctx.Obv = GetIndicatorValue(indicatorResults, indicatorIndex, "OBV", "OBV")
        ctx.ObvSignal = GetIndicatorValue(indicatorResults, indicatorIndex, "OBV", "Signal")
        ctx.PrevObv = GetIndicatorValue(indicatorResults, prevIndicatorIndex, "OBV", "OBV")
        ctx.ObvBullish = Not Double.IsNaN(ctx.Obv) AndAlso Not Double.IsNaN(ctx.ObvSignal) AndAlso ctx.Obv > ctx.ObvSignal
        ctx.ObvImproving = Not Double.IsNaN(ctx.PrevObv) AndAlso ctx.Obv > ctx.PrevObv

        ctx.TickSum = Math.Abs(GetIndicatorValue(indicatorResults, indicatorIndex, "TICKINT", "TickSum"))
        ctx.TickMa5 = Math.Abs(GetIndicatorValue(indicatorResults, indicatorIndex, "TICKINT", "MA5"))
        ctx.TickMa20 = Math.Abs(GetIndicatorValue(indicatorResults, indicatorIndex, "TICKINT", "MA20"))
        ctx.PrevTickSum = Math.Abs(GetIndicatorValue(indicatorResults, prevIndicatorIndex, "TICKINT", "TickSum"))
        ctx.PrevTickMa5 = Math.Abs(GetIndicatorValue(indicatorResults, prevIndicatorIndex, "TICKINT", "MA5"))
        ctx.TickCrossUpMa5 = ctx.PrevTickSum <= ctx.PrevTickMa5 AndAlso ctx.TickSum > ctx.TickMa5
        ctx.TickRatio = SafeDiv(ctx.TickSum, ctx.TickMa20)
        ctx.TickSlope = ctx.TickSum - ctx.TickMa5
        ctx.TickAccel = ctx.TickSum - ctx.PrevTickSum
        ctx.TickPersist = CountTickPersist(indicatorResults, indicatorIndex, 5)
        ctx.RecentTickCrossBarsAgo = FindRecentTickCrossBarsAgo(indicatorResults, indicatorIndex, RecentTickCrossLookback)
        ctx.HasRecentTickCrossUp = (ctx.RecentTickCrossBarsAgo >= 0)
        ctx.TickNotCollapsed = ctx.TickSum >= ctx.TickMa5 * TickCollapseRatio

        ctx.TurnoverAccel = CalcTurnoverAccel(candles, indicatorIndex)
        ctx.PriceAccel = CalcPriceAccel(candles, index)
        ctx.IsSecondViLeaderEntry = IsSecondViLeaderEntryZone(ctx)

        ctx.RawTickPowerScore = CalcRawTickPower(ctx)
        ctx.LeaderScore = CalcLeaderScore(ctx)
        ctx.TrendStartScore = CalcTrendStartScore(ctx)
        ctx.EntrySafetyScore = CalcEntrySafetyScore(ctx)
        ctx.TradePriorityScore = ctx.LeaderScore * ctx.TrendStartScore * ctx.EntrySafetyScore / 10000.0R
        Return ctx
    End Function

    Private Function IsSecondViLeaderEntryZone(ctx As LeaderContext) As Boolean
        If ctx Is Nothing Then Return False
        If ctx.OpenRiseRate < SecondViLeaderMinOpenRise Then Return False
        If ctx.OpenRiseRate > SecondViLeaderMaxOpenRise Then Return False
        If ctx.TickMa5 < SecondViMinTickMa5 Then Return False
        If Not ctx.HasRecentTickCrossUp Then Return False
        If Not ctx.TickNotCollapsed Then Return False
        Return True
    End Function

    Private Function CalcRawTickPower(ctx As LeaderContext) As Double
        Dim ratioScore As Double = Clamp(ctx.TickRatio / 3.0R * 30.0R, 0.0R, 30.0R)
        Dim crossScore As Double = If(ctx.TickCrossUpMa5, 20.0R, 0.0R)
        Dim recentCrossScore As Double = If(ctx.HasRecentTickCrossUp, Math.Max(0.0R, 20.0R - CDbl(ctx.RecentTickCrossBarsAgo) * 3.0R), 0.0R)
        Dim slopeScore As Double = If(ctx.TickSlope > 0.0R, 10.0R, 0.0R)
        Dim persistScore As Double = Clamp(CDbl(ctx.TickPersist) / 5.0R * 10.0R, 0.0R, 10.0R)
        Dim turnoverScore As Double = Clamp(ctx.TurnoverAccel / 3.0R * 10.0R, 0.0R, 10.0R)
        Return ratioScore + crossScore + recentCrossScore + slopeScore + persistScore + turnoverScore
    End Function

    Private Function CalcLeaderScore(ctx As LeaderContext) As Double
        Dim score As Double = 0.0R
        score += Clamp(ctx.RawTickPowerScore, 0.0R, 55.0R)
        If ctx.ObvBullish Then score += 15.0R
        If ctx.ObvImproving Then score += 5.0R
        If ctx.JmaBullish Then score += 10.0R
        If ctx.SuperTrendBullish Then score += 10.0R
        If ctx.TurnoverAccel >= 1.5R Then score += 5.0R
        If ctx.IsSecondViLeaderEntry Then score += 10.0R
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
        If ctx.HasRecentTickCrossUp Then score += 15.0R
        If ctx.PriceAccel > 0.0R Then score += 5.0R
        If ctx.IsSecondViLeaderEntry Then score += 10.0R
        Return Clamp(score, 0.0R, 100.0R)
    End Function

    Private Function CalcEntrySafetyScore(ctx As LeaderContext) As Double
        Dim dynamicMaxOpenRise As Double = Math.Min(MaxOpenRiseForNewBuy, ViTriggerPct - TargetProfitPct - ViSafetyMarginPct)
        If dynamicMaxOpenRise < 0.0R Then dynamicMaxOpenRise = 0.0R

        If ctx.IsSecondViLeaderEntry Then
            ctx.BlockReason = ""
            Return Clamp(SecondViEntrySafetyScore, 0.0R, 100.0R)
        End If

        If ctx.OpenRiseRate > dynamicMaxOpenRise Then
            ctx.BlockReason = String.Format("시가대비 {0:0.00}%: 초입 매수상한 {1:0.00}% 초과, 2차VI 최근 TickCross 조건 미충족(Tick={2:0.00}, TickMA5={3:0.00}, RecentCross={4}, Collapsed={5})",
                                            ctx.OpenRiseRate,
                                            dynamicMaxOpenRise,
                                            ctx.TickSum,
                                            ctx.TickMa5,
                                            ctx.RecentTickCrossBarsAgo,
                                            Not ctx.TickNotCollapsed)
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

        ctx.BlockReason = ""
        Return 100.0R
    End Function
#End Region

#Region "Indicator Helpers"
    Private Function FindRecentTickCrossBarsAgo(indicatorResults As Dictionary(Of String, List(Of IndicatorResult)), indicatorIndex As Integer, lookback As Integer) As Integer
        Dim startIndex As Integer = Math.Max(1, indicatorIndex - lookback + 1)
        Dim i As Integer
        For i = indicatorIndex To startIndex Step -1
            Dim tickValue As Double = Math.Abs(GetIndicatorValue(indicatorResults, i, "TICKINT", "TickSum"))
            Dim tickMa5Value As Double = Math.Abs(GetIndicatorValue(indicatorResults, i, "TICKINT", "MA5"))
            Dim prevTickValue As Double = Math.Abs(GetIndicatorValue(indicatorResults, i - 1, "TICKINT", "TickSum"))
            Dim prevTickMa5Value As Double = Math.Abs(GetIndicatorValue(indicatorResults, i - 1, "TICKINT", "MA5"))

            If tickMa5Value >= SecondViMinTickMa5 AndAlso prevTickValue <= prevTickMa5Value AndAlso tickValue > tickMa5Value Then
                Return indicatorIndex - i
            End If
        Next
        Return -1
    End Function

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
        signal.StrategyName = "TrueLeaderEarlyTrendTop3V3"
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
            If i = 0 OrElse candles(i - 1).Dt.Date <> targetDate Then Return CDbl(candles(i).Open)
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

    Private Shared Function IsValidLine(value As Double) As Boolean
        Return Not Double.IsNaN(value) AndAlso Not Double.IsInfinity(value)
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
        Public Property SuperTrendUp As Double = Double.NaN
        Public Property SuperTrendDown As Double = Double.NaN
        Public Property PrevSuperTrendUp As Double = Double.NaN
        Public Property PrevSuperTrendDown As Double = Double.NaN
        Public Property SuperTrendBullish As Boolean = False
        Public Property SuperTrendBearish As Boolean = False
        Public Property SuperTrendTurnDown As Boolean = False
        Public Property JmaValue As Double = Double.NaN
        Public Property JmaActiveValue As Double = Double.NaN
        Public Property JmaSlope As Double = Double.NaN
        Public Property PrevJmaSlope As Double = Double.NaN
        Public Property JmaUp As Double = Double.NaN
        Public Property JmaDown As Double = Double.NaN
        Public Property PrevJmaUp As Double = Double.NaN
        Public Property PrevJmaDown As Double = Double.NaN
        Public Property JmaBullish As Boolean = False
        Public Property JmaBearish As Boolean = False
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
        Public Property PrevTickMa5 As Double = 0.0R
        Public Property TickCrossUpMa5 As Boolean = False
        Public Property HasRecentTickCrossUp As Boolean = False
        Public Property RecentTickCrossBarsAgo As Integer = -1
        Public Property TickNotCollapsed As Boolean = False
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
        Public Property IsSecondViLeaderEntry As Boolean = False
    End Class
#End Region

End Class
