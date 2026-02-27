' ═══════════════════════════════════════════════════════════════
' StrategyEngine.vb — 통합 전략 관리 및 평가 엔진
' ═══════════════════════════════════════════════════════════════

Imports MainApp.Models
'Imports MainApp.ChartEngine.Models
Imports System.Collections.Generic
Imports System.Linq

Namespace Services
    Public Class StrategyEngine
        Private ReadOnly _aiStrategies As New List(Of StrategyDefinition)
        Private ReadOnly _hardcodedStrategies As New List(Of IStrategy)
        Private ReadOnly _evaluator As New StrategyEvaluator()

        ''' <summary>AI 기반 전략(JSON 정의) 등록</summary>
        Public Sub [Register](strat As StrategyDefinition)
            If strat Is Nothing Then Return
            _aiStrategies.RemoveAll(Function(s) s.Name = strat.Name)
            _aiStrategies.Add(strat)
        End Sub

        ''' <summary>하드코딩된 전략(IStrategy 인터페이스 구현체) 등록</summary>
        Public Sub [Register](strat As IStrategy)
            If strat Is Nothing Then Return
            _hardcodedStrategies.RemoveAll(Function(s) s.Name = strat.Name)
            _hardcodedStrategies.Add(strat)
        End Sub

        Public Sub Remove(name As String)
            _aiStrategies.RemoveAll(Function(s) s.Name = name)
            _hardcodedStrategies.RemoveAll(Function(s) s.Name = name)
        End Sub

        Public Sub Clear()
            _aiStrategies.Clear()
            _hardcodedStrategies.Clear()
        End Sub

        ''' <summary>
        ''' 등록된 모든 전략(AI + Hardcoded)을 평가하여 신호 목록을 반환함
        ''' </summary>
        Public Function EvaluateAll(stockCode As String,
                                    candles As List(Of CandleItem),
                                    indicatorResults As Dictionary(Of String, List(Of IndicatorResult)),
                                    Optional prevClose As Double = 0) As List(Of StrategySignal)

            Dim allSignals As New List(Of StrategySignal)

            ' 1. AI 기반 전략 평가 (StrategyDefinition)
            For Each strat In _aiStrategies
                ' Historical Evaluation 실행
                Dim results = _evaluator.RunHistorical(stockCode, strat, candles, indicatorResults, prevClose)
                ' 결과를 Marker(신호)로 변환
                Dim buyCnt As Integer = results.Where(Function(r) r IsNot Nothing AndAlso r.IsBuySignal).Count()
                Dim sellCnt As Integer = results.Where(Function(r) r IsNot Nothing AndAlso r.IsSellSignal).Count()
                Dim markers = _evaluator.GenerateMarkers(results, candles)
                AppLogger.I.Info($"[Strategy] {strat.Name}: eval={results.Count}, buy={buyCnt}, sell={sellCnt}, markers={markers.Count}")

                ' 각 신호에 전략 정보 보강
                For Each m In markers
                    m.StockCode = stockCode
                    m.StrategyName = strat.Name
                Next

                allSignals.AddRange(markers)
            Next

            ' 2. 하드코딩된 전략 평가 (IStrategy)
            For Each strat In _hardcodedStrategies
                Try
                    ' IStrategy.Evaluate는 보통 마지막 시점의 신호나 전체를 반환할 수 있음 (구현에 따름)
                    Dim signals = strat.Evaluate(stockCode, candles, indicatorResults)
                    If signals IsNot Nothing Then allSignals.AddRange(signals)
                Catch ex As Exception
                    AppLogger.I.Error($"[StrategyEngine] 하드코딩 전략({strat.Name}) 평가 오류: {ex.Message}")
                End Try
            Next

            Return allSignals
        End Function

        ''' <summary>단일 시점 평가 (공유 로직)</summary>
        Public Shared Function EvaluateInternal(strategy As StrategyDefinition, snapshots As List(Of MarketSnapshot), currentIndex As Integer) As EvaluationResult
            If strategy Is Nothing OrElse snapshots Is Nothing OrElse currentIndex < 0 OrElse currentIndex >= snapshots.Count Then Return Nothing

            Dim current = snapshots(currentIndex)
            Dim states As New Dictionary(Of String, Boolean)

            ' 1. 모든 개별 조건(ConditionCell) 선행 평가
            Dim allBuyConds = strategy.BuyRules.SelectMany(Function(g) g.Conditions)
            Dim allSellConds = strategy.SellRules.SelectMany(Function(g) g.Conditions)

            Dim allConditions = allBuyConds.Concat(allSellConds).Where(Function(c) c.IsActive).GroupBy(Function(c) c.Id).Select(Function(g) g.First())

            For Each cell In allConditions
                states(cell.Id) = EvaluateCell(cell, snapshots, currentIndex)
            Next

            ' 2. Buy 논리 게이트 평가 (OR)
            Dim isBuy = strategy.BuyRules.Where(Function(g) g.IsActive).Any(Function(gate) EvaluateGate(gate, states))

            ' 3. Sell 논리 게이트 평가 (OR)
            Dim isSell = strategy.SellRules.Where(Function(g) g.IsActive).Any(Function(gate) EvaluateGate(gate, states))

            Return New EvaluationResult(current.Time, strategy.Name, isBuy, isSell, states)
        End Function

        ''' <summary>인스턴스 래퍼 (호환성 유지)</summary>
        Public Function Evaluate(strategy As StrategyDefinition, snapshots As List(Of MarketSnapshot), currentIndex As Integer) As EvaluationResult
            Return EvaluateInternal(strategy, snapshots, currentIndex)
        End Function

        Private Shared Function EvaluateCell(cell As ConditionCell, snapshots As List(Of MarketSnapshot), index As Integer) As Boolean
            Dim targetIdx = index - cell.Offset
            If targetIdx < 0 OrElse targetIdx >= snapshots.Count Then Return False

            Dim valA = GetTargetValue(cell.IndicatorA, snapshots, targetIdx, cell.Lookback)
            Dim valB As Double

            If Not String.IsNullOrEmpty(cell.IndicatorB) Then
                valB = GetTargetValue(cell.IndicatorB, snapshots, targetIdx, cell.Lookback)
            Else
                valB = If(cell.ConstantValue.HasValue, cell.ConstantValue.Value, Double.NaN)
            End If

            If Double.IsNaN(valA) OrElse Double.IsNaN(valB) Then Return False

            Dim result As Boolean = False
            Select Case cell.Operator
                Case ComparisonOperator.Greater : result = valA > valB
                Case ComparisonOperator.Less : result = valA < valB
                Case ComparisonOperator.GreaterEqual : result = valA >= valB
                Case ComparisonOperator.LessEqual : result = valA <= valB
                Case ComparisonOperator.Equal : result = Math.Abs(valA - valB) < 0.000001
                Case ComparisonOperator.NotEqual : result = Math.Abs(valA - valB) >= 0.000001
                Case ComparisonOperator.CrossUp
                    If targetIdx <= 0 Then Return False
                    Dim prevA = snapshots(targetIdx - 1).GetValue(cell.IndicatorA)
                    Dim prevB = If(Not String.IsNullOrEmpty(cell.IndicatorB), snapshots(targetIdx - 1).GetValue(cell.IndicatorB), If(cell.ConstantValue.HasValue, cell.ConstantValue.Value, Double.NaN))
                    result = (prevA <= prevB) AndAlso (valA > valB)
                Case ComparisonOperator.CrossDown
                    If targetIdx <= 0 Then Return False
                    Dim pA = snapshots(targetIdx - 1).GetValue(cell.IndicatorA)
                    Dim pB = If(Not String.IsNullOrEmpty(cell.IndicatorB), snapshots(targetIdx - 1).GetValue(cell.IndicatorB), If(cell.ConstantValue.HasValue, cell.ConstantValue.Value, Double.NaN))
                    result = (pA >= pB) AndAlso (valA < valB)
            End Select

            Return If(cell.IsInverted, Not result, result)
        End Function

        Private Shared Function GetTargetValue(key As String, snaps As List(Of MarketSnapshot), index As Integer, lookback As Integer) As Double
            If lookback <= 1 Then Return snaps(index).GetValue(key)

            Dim maxVal As Double = Double.MinValue
            Dim startIdx = Math.Max(0, index - lookback + 1)
            For i = startIdx To index
                Dim v = snaps(i).GetValue(key)
                If Not Double.IsNaN(v) AndAlso v > maxVal Then maxVal = v
            Next
            Return If(maxVal = Double.MinValue, Double.NaN, maxVal)
        End Function

        Private Shared Function EvaluateGate(gate As LogicGate, states As Dictionary(Of String, Boolean)) As Boolean
            Dim activeConditions = gate.Conditions.Where(Function(c) c.IsActive).ToList()
            If activeConditions.Count = 0 Then Return False

            Select Case gate.Operator
                Case LogicalOperator.AND
                    Return activeConditions.All(Function(c)
                                                    Dim res As Boolean
                                                    Return states.TryGetValue(c.Id, res) AndAlso res
                                                End Function)
                Case LogicalOperator.OR
                    Return activeConditions.Any(Function(c)
                                                    Dim res As Boolean
                                                    Return states.TryGetValue(c.Id, res) AndAlso res
                                                End Function)
                Case LogicalOperator.XOR
                    Dim trueCount As Integer = 0
                    For Each c In activeConditions
                        Dim res As Boolean
                        If states.TryGetValue(c.Id, res) AndAlso res Then trueCount += 1
                    Next
                    Return trueCount = 1
                Case Else
                    Return False
            End Select
        End Function
    End Class
End Namespace
