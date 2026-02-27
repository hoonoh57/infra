' ═══════════════════════════════════════════════════════════════
' StrategyEvaluator.vb — 전략 실행 및 신호 생성기 (Ported from C#)
' ═══════════════════════════════════════════════════════════════

Imports MainApp.Models
Imports MainApp.ChartEngine.Models
Imports System.Collections.Generic
Imports System.Linq

Namespace Services
    Public Class StrategyEvaluator
        ''' <summary>
        ''' 전체 데이터셋에 대해 전략을 평가하고 이력(Results)을 생성함.
        ''' </summary>
        Public Function RunHistorical(stockCode As String,
                                       strategy As StrategyDefinition,
                                       candles As List(Of CandleItem),
                                       indicatorResults As Dictionary(Of String, List(Of IndicatorResult)),
                                       Optional prevClose As Double = 0) As List(Of EvaluationResult)

            If strategy Is Nothing OrElse candles Is Nothing OrElse candles.Count = 0 Then Return New List(Of EvaluationResult)

            Dim snapshots = SnapshotService.CreateSnapshots(stockCode, candles, indicatorResults, prevClose)
            Dim results As New List(Of EvaluationResult)
            LogStrategyInputs(strategy, snapshots)

            ' 가상 포지션 상태 추적 (수익률 기반 매도를 위해)
            Dim entryPrice As Double = 0
            Dim hasPosition As Boolean = False

            For i As Integer = 0 To snapshots.Count - 1
                Dim snap = snapshots(i)

                ' [상태 반영] 현재 포지션이 있다면 수익률(PROFIT_PCT) 계산하여 주입
                If hasPosition AndAlso entryPrice > 0 Then
                    snap.SetIndicator("PROFIT_PCT", (snap.Close - entryPrice) / entryPrice * 100.0)
                Else
                    snap.SetIndicator("PROFIT_PCT", 0.0)
                End If

                ' [지능형 에이전트 분석 및 점수 주입]
                ' (AgentManager 구현 여부에 따라 조건부 실행)
                Dim agentScore As Double = 50.0
                ' ... (생략 또는 추후 구현)
                snap.SetIndicator("AGENT_SCORE", agentScore)

                Dim res = StrategyEngine.EvaluateInternal(strategy, snapshots, i)
                res.AgentScore = agentScore ' 결과에 점수 보강
                
                ' [상태 업데이트 및 중복 신호 필터링]
                If res.IsBuySignal AndAlso Not hasPosition Then
                    hasPosition = True
                    entryPrice = snap.Close
                Else If res.IsBuySignal AndAlso hasPosition Then
                    ' 이미 포지션 보유 중이면 매수 신호 무시
                    res.IsBuySignal = False 
                End If

                If res.IsSellSignal AndAlso hasPosition Then
                    hasPosition = False
                    entryPrice = 0
                Else If res.IsSellSignal AndAlso Not hasPosition Then
                    ' 포지션 없는데 매도 신호 무시
                    res.IsSellSignal = False
                End If

                results.Add(res)
            Next

            Return results
        End Function

        Private Shared Sub LogStrategyInputs(strategy As StrategyDefinition, snapshots As List(Of MarketSnapshot))
            If strategy Is Nothing OrElse snapshots Is Nothing OrElse snapshots.Count = 0 Then Return
            Dim keys As New List(Of String) From {"Price", "SuperTrend"}
            Dim allConds = strategy.BuyRules.SelectMany(Function(g) g.Conditions).
                Concat(strategy.SellRules.SelectMany(Function(g) g.Conditions))
            For Each c In allConds
                If c Is Nothing Then Continue For
                If Not String.IsNullOrWhiteSpace(c.IndicatorA) AndAlso Not keys.Contains(c.IndicatorA) Then keys.Add(c.IndicatorA)
                If Not String.IsNullOrWhiteSpace(c.IndicatorB) AndAlso Not keys.Contains(c.IndicatorB) Then keys.Add(c.IndicatorB)
            Next

            For Each k In keys
                Dim valid As Integer = 0
                For i = 0 To snapshots.Count - 1
                    Dim v = snapshots(i).GetValue(k)
                    If Not Double.IsNaN(v) Then valid += 1
                Next
                AppLogger.I.Info($"[StrategyDiag] {strategy.Name} key={k} valid={valid}/{snapshots.Count}")
            Next
        End Sub

        ''' <summary>
        ''' 평가 결과를 바탕으로 차트에 표시할 마커 리스트 생성
        ''' </summary>
        Public Function GenerateMarkers(results As List(Of EvaluationResult), candles As List(Of CandleItem)) As List(Of StrategySignal)
            Dim markers As New List(Of StrategySignal)
            If results Is Nothing OrElse candles Is Nothing Then Return markers

            For i As Integer = 0 To Math.Min(results.Count, candles.Count) - 1
                Dim res = results(i)
                Dim candle = candles(i)

                If res.IsBuySignal Then
                    markers.Add(New StrategySignal With {
                        .SignalType = SignalType.Buy,
                        .Timestamp = res.Timestamp,
                        .Price = CSng(candle.Low),
                        .Reason = "AI Buy Signal"
                    })
                ElseIf res.IsSellSignal Then
                    markers.Add(New StrategySignal With {
                        .SignalType = SignalType.Sell,
                        .Timestamp = res.Timestamp,
                        .Price = CSng(candle.High),
                        .Reason = "AI Sell Signal"
                    })
                End If
            Next
            Return markers
        End Function
    End Class
End Namespace
