using System;
using System.Collections.Generic;
using System.Linq;
using Common.Models;

namespace App64.Services
{
    /// <summary>
    /// 투명하고 단순한 논리를 지향하는 전략 평가 엔진.
    /// 모든 조건은 3개 계층 이하의 트리 구조로 평가되며, 결과는 불변 객체로 반환됨.
    /// </summary>
    public class StrategyEngine
    {
        public EvaluationResult Evaluate(StrategyDefinition strategy, List<MarketSnapshot> snapshots, int currentIndex)
        {
            if (strategy == null || snapshots == null || currentIndex < 0 || currentIndex >= snapshots.Count) return null;

            var current = snapshots[currentIndex];
            var states = new Dictionary<string, bool>();

            // 1. 모든 개별 조건(ConditionCell) 선행 평가
            var allConditions = strategy.BuyRules
                .Concat(strategy.SellRules)
                .SelectMany(g => g.Conditions)
                .Where(c => c.IsActive)
                .GroupBy(c => c.Id)
                .Select(g => g.First());

            foreach (var cell in allConditions)
            {
                states[cell.Id] = EvaluateCell(cell, snapshots, currentIndex);
            }

            // 2. Buy 논리 게이트 평가 (OR)
            bool isBuy = strategy.BuyRules
                .Where(g => g.IsActive)
                .Any(gate => EvaluateGate(gate, states));

            // 3. Sell 논리 게이트 평가 (OR)
            bool isSell = strategy.SellRules
                .Where(g => g.IsActive)
                .Any(gate => EvaluateGate(gate, states));

            return new EvaluationResult(current.Time, strategy.Name, isBuy, isSell, states);
        }

        private bool EvaluateCell(ConditionCell cell, List<MarketSnapshot> snapshots, int index)
        {
            int targetIdx = index - cell.Offset;
            if (targetIdx < 0 || targetIdx >= snapshots.Count) return false;

            double valA = GetTargetValue(cell.IndicatorA, snapshots, targetIdx, cell.Lookback);
            double valB = cell.IndicatorB != null 
                ? GetTargetValue(cell.IndicatorB, snapshots, targetIdx, cell.Lookback) 
                : (cell.ConstantValue ?? double.NaN);

            if (double.IsNaN(valA) || double.IsNaN(valB)) return false;

            bool result = false;
            switch (cell.Operator)
            {
                case ComparisonOperator.GreaterThan: result = valA > valB; break;
                case ComparisonOperator.LessThan: result = valA < valB; break;
                case ComparisonOperator.GreaterThanOrEqual: result = valA >= valB; break;
                case ComparisonOperator.LessThanOrEqual: result = valA <= valB; break;
                case ComparisonOperator.Equal: result = Math.Abs(valA - valB) < 0.000001; break;
                case ComparisonOperator.NotEqual: result = Math.Abs(valA - valB) >= 0.000001; break;
                case ComparisonOperator.CrossUp:
                    if (targetIdx <= 0) return false;
                    double prevA = snapshots[targetIdx - 1].GetValue(cell.IndicatorA);
                    double prevB = cell.IndicatorB != null ? snapshots[targetIdx - 1].GetValue(cell.IndicatorB) : (cell.ConstantValue ?? double.NaN);
                    result = (prevA <= prevB) && (valA > valB);
                    break;
                case ComparisonOperator.CrossDown:
                    if (targetIdx <= 0) return false;
                    double pA = snapshots[targetIdx - 1].GetValue(cell.IndicatorA);
                    double pB = cell.IndicatorB != null ? snapshots[targetIdx - 1].GetValue(cell.IndicatorB) : (cell.ConstantValue ?? double.NaN);
                    result = (pA >= pB) && (valA < valB);
                    break;
            }

            return cell.IsInverted ? !result : result;
        }

        private double GetTargetValue(string key, List<MarketSnapshot> snaps, int index, int lookback)
        {
            if (lookback <= 1) return snaps[index].GetValue(key);

            // Lookback > 1인 경우 범위 내 최대값 반환 (기본 전략적 직관: 돌파 등에서 High 참조 시 유용)
            // 지표 성격에 따라 다를 수 있으나, 일단 Max(High) 식의 접근
            double max = double.MinValue;
            int start = Math.Max(0, index - lookback + 1);
            for (int i = start; i <= index; i++)
            {
                double v = snaps[i].GetValue(key);
                if (!double.IsNaN(v) && v > max) max = v;
            }
            return max == double.MinValue ? double.NaN : max;
        }

        private bool EvaluateGate(LogicGate gate, Dictionary<string, bool> states)
        {
            var activeConditions = gate.Conditions.Where(c => c.IsActive).ToList();
            if (activeConditions.Count == 0) return false;

            if (gate.Operator == LogicalOperator.AND)
            {
                // AND 게이트: 모든 조건이 참이어야 함
                return activeConditions.All(c => states.TryGetValue(c.Id, out bool res) && res);
            }
            else
            {
                // OR 게이트: 하나라도 참이면 참
                return activeConditions.Any(c => states.TryGetValue(c.Id, out bool res) && res);
            }
        }
    }
}
