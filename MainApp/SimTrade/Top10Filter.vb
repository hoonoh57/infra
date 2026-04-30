' ═══════════════════════════════════════════════════════════════
' Top10Filter.vb — 상위 10종목 선별 필터 (원칙서 v4.0)
' ═══════════════════════════════════════════════════════════════
' ★ 감지된 종목(최대 50개)에서 매매 가치 상위 10개 선별
' ★ 점수 기준: TickSum + 거래대금 + ST방향 + JMA방향 + RSI
' ★ 1분 주기로 재평가, 순위 변동 시 구독 교체
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

    ''' <summary>종목별 Top10 점수</summary>
    Public Class Top10Score
        Public Property Code As String = ""
        Public Property Name As String = ""
        Public Property TotalScore As Double = 0
        Public Property Rank As Integer = 0

        ' 개별 점수 (디버그/로그용)
        Public Property ScoreTickSum As Double = 0
        Public Property ScoreTradeAmount As Double = 0
        Public Property ScoreST As Double = 0
        Public Property ScoreJMA As Double = 0
        Public Property ScoreRSI As Double = 0
        Public Property ScoreChangeRate As Double = 0
        Public Property ScoreVolume As Double = 0

        Public Function ToSummary() As String
            Return $"#{Rank} {Code} {Name} 점수={TotalScore:F1} (Tick={ScoreTickSum:F1} 대금={ScoreTradeAmount:F1} ST={ScoreST:F0} JMA={ScoreJMA:F0} RSI={ScoreRSI:F1} 등락={ScoreChangeRate:F1} Vol={ScoreVolume:F1})"
        End Function
    End Class

    ''' <summary>Top10 필터 결과</summary>
    Public Class Top10Result
        Public Property TopStocks As New List(Of Top10Score)
        Public Property TotalEvaluated As Integer = 0
        Public Property EvaluatedAt As DateTime = DateTime.Now

        ''' <summary>Top10 코드 목록</summary>
        Public Function GetTopCodes() As List(Of String)
            Return TopStocks.Select(Function(s) s.Code).ToList()
        End Function

        Public Function ToSummary() As String
            If TopStocks.Count = 0 Then Return "Top10: 없음"
            Dim top3 = String.Join(", ", TopStocks.Take(3).Select(Function(s) $"{s.Code}({s.TotalScore:F0})"))
            Return $"Top10: {TopStocks.Count}종목 평가{TotalEvaluated}건 | 상위3: {top3}"
        End Function
    End Class

    ''' <summary>Top10 종목 선별 필터</summary>
    Public Class Top10Filter

        Private ReadOnly _settings As SimTradeSettings
        Private _lastResult As Top10Result
        Private _maxCount As Integer = 10

        Public Sub New(settings As SimTradeSettings, Optional maxCount As Integer = 10)
            _settings = settings
            _maxCount = maxCount
        End Sub

        ''' <summary>현재 감시 종목에서 Top N 선별</summary>
        Public Function Evaluate(states As List(Of StockState)) As Top10Result
            Dim result As New Top10Result()
            If states Is Nothing OrElse states.Count = 0 Then Return result

            ' 제외/미준비 상태 필터링
            Dim candidates = states.Where(
                Function(s) s.State = DataState.Ready OrElse s.State = DataState.Trading).ToList()

            result.TotalEvaluated = candidates.Count
            If candidates.Count = 0 Then Return result

            Dim wTickSum As Double = Math.Max(0.0, _settings.TopTickWeight)
            Dim wTradeAmount As Double = Math.Max(0.0, _settings.TopAmountWeight)
            Dim wTrend As Double = Math.Max(0.0, _settings.TopTrendWeight)
            Dim wMomentum As Double = Math.Max(0.0, _settings.TopMomentumWeight)

            ' 기존 비율 유지: Trend 25 = ST 15 + JMA 10
            Dim wST As Double = wTrend * 0.6
            Dim wJMA As Double = wTrend * 0.4

            ' 기존 비율 유지: Momentum 30 = RSI 10 + 등락률 10 + 거래량 10
            Dim wRSI As Double = wMomentum / 3.0
            Dim wChangeRate As Double = wMomentum / 3.0
            Dim wVolume As Double = wMomentum / 3.0

            ' 정규화를 위한 최대값 계산
            Dim maxTickSum = candidates.Max(Function(s) If(Double.IsNaN(s.TickSum_Normalized), 0, s.TickSum_Normalized))
            Dim maxAmount = candidates.Max(Function(s) CLng(s.CurrentPrice) * s.DayVolume)
            Dim maxVolRatio = candidates.Max(Function(s) If(Double.IsNaN(s.Volume_Ratio), 0, s.Volume_Ratio))

            If maxTickSum <= 0 Then maxTickSum = 1
            If maxAmount <= 0 Then maxAmount = 1
            If maxVolRatio <= 0 Then maxVolRatio = 1

            ' 점수 계산
            Dim scores As New List(Of Top10Score)

            For Each s In candidates
                Dim sc As New Top10Score()
                sc.Code = s.Code
                sc.Name = s.Name

                ' TickSum 정규화 점수 (0~25)
                Dim tick = If(Double.IsNaN(s.TickSum_Normalized), 0, s.TickSum_Normalized)
                sc.ScoreTickSum = (tick / maxTickSum) * wTickSum

                ' 거래대금 정규화 점수 (0~20)
                Dim amt = CLng(s.CurrentPrice) * s.DayVolume
                sc.ScoreTradeAmount = (CDbl(amt) / maxAmount) * wTradeAmount

                ' ST 방향 점수 (+15 또는 0)
                sc.ScoreST = If(s.ST_Direction > 0, wST, 0)

                ' JMA 방향 점수 (+10 또는 0)
                sc.ScoreJMA = If(s.JMA_Direction > 0, wJMA, 0)

                ' RSI 점수 (60~70 구간이 최적, 정규화)
                Dim rsi = If(Double.IsNaN(s.RSI_Value), 50, s.RSI_Value)
                If rsi >= 60 AndAlso rsi <= 70 Then
                    sc.ScoreRSI = wRSI
                ElseIf rsi >= 50 AndAlso rsi < 60 Then
                    sc.ScoreRSI = wRSI * 0.5
                ElseIf rsi > 70 AndAlso rsi <= 80 Then
                    sc.ScoreRSI = wRSI * 0.7
                Else
                    sc.ScoreRSI = 0
                End If

                ' 등락률 점수 (3~10% 최적 구간)
                Dim chg = s.ChangeRate
                If chg >= 3 AndAlso chg <= 10 Then
                    sc.ScoreChangeRate = wChangeRate
                ElseIf chg >= 1 AndAlso chg < 3 Then
                    sc.ScoreChangeRate = wChangeRate * 0.5
                ElseIf chg > 10 AndAlso chg <= 15 Then
                    sc.ScoreChangeRate = wChangeRate * 0.6
                Else
                    sc.ScoreChangeRate = 0
                End If

                ' 거래량 비율 점수 (0~10)
                Dim volR = If(Double.IsNaN(s.Volume_Ratio), 0, s.Volume_Ratio)
                sc.ScoreVolume = Math.Min((volR / maxVolRatio) * wVolume, wVolume)

                sc.TotalScore = sc.ScoreTickSum + sc.ScoreTradeAmount + sc.ScoreST +
                                sc.ScoreJMA + sc.ScoreRSI + sc.ScoreChangeRate + sc.ScoreVolume

                scores.Add(sc)
            Next

            ' 점수 내림차순 정렬 → 상위 N개
            scores = scores.OrderByDescending(Function(s) s.TotalScore).ToList()

            For i = 0 To Math.Min(scores.Count, _maxCount) - 1
                scores(i).Rank = i + 1
                result.TopStocks.Add(scores(i))
            Next

            _lastResult = result
            Return result
        End Function

        ''' <summary>마지막 평가 결과</summary>
        Public Function GetLastResult() As Top10Result
            Return _lastResult
        End Function

        ''' <summary>특정 종목이 Top10에 포함되는지</summary>
        Public Function IsInTop(code As String) As Boolean
            If _lastResult Is Nothing Then Return False
            Return _lastResult.TopStocks.Any(Function(s) s.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>특정 종목의 순위 (미포함 시 -1)</summary>
        Public Function GetRank(code As String) As Integer
            If _lastResult Is Nothing Then Return -1
            Dim found = _lastResult.TopStocks.FirstOrDefault(
                Function(s) s.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
            If found Is Nothing Then Return -1
            Return found.Rank
        End Function

        ''' <summary>Top10 변동 감지 (이전 결과와 비교)</summary>
        Public Function DetectChanges(prevCodes As List(Of String), newCodes As List(Of String)) As String
            If prevCodes Is Nothing OrElse newCodes Is Nothing Then Return ""
            Dim added = newCodes.Except(prevCodes, StringComparer.OrdinalIgnoreCase).ToList()
            Dim removed = prevCodes.Except(newCodes, StringComparer.OrdinalIgnoreCase).ToList()
            Dim parts As New List(Of String)
            If added.Count > 0 Then parts.Add($"편입:{String.Join(",", added)}")
            If removed.Count > 0 Then parts.Add($"이탈:{String.Join(",", removed)}")
            If parts.Count = 0 Then Return "변동없음"
            Return String.Join(" | ", parts)
        End Function

    End Class

End Namespace

