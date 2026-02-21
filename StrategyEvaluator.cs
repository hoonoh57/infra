using System;
using System.Collections.Generic;
using System.Linq;
using App64.Controls;
using Common.Models;

namespace App64.Services
{
    /// <summary>
    /// 차트 데이터와 전략 정의를 결합하여 최종 신호를 생성하고, 
    /// 각 시점의 평가 결과를 관리하는 상위 조정 서비스.
    /// </summary>
    public class StrategyEvaluator
    {
        private readonly StrategyEngine _engine = new StrategyEngine();

        /// <summary>
        /// 전체 데이터셋에 대해 전략을 평가하고 이력(Results)을 생성함.
        /// </summary>
        public List<EvaluationResult> RunHistorical(string stockCode, StrategyDefinition strategy, List<FastChart.OHLCV> data, List<FastChart.CustomSeries> indicators, double todayOpen = 0, List<BarData> externalDaily = null)
        {
            if (strategy == null || data == null || data.Count == 0) return new List<EvaluationResult>();

            var snapshots = SnapshotService.CreateSnapshots(stockCode, data, indicators, todayOpen, strategy, externalDaily);
            var results = new List<EvaluationResult>();

            // 가상 포지션 상태 추적 (수익률 기반 매도를 위해)
            double entryPrice = 0;
            bool hasPosition = false;

            for (int i = 0; i < snapshots.Count; i++)
            {
                var snap = snapshots[i];

                // [상태 반영] 현재 포지션이 있다면 수익률(PROFIT_PCT) 계산하여 주입
                if (hasPosition && entryPrice > 0)
                {
                    snap.SetIndicator("PROFIT_PCT", (snap.Close - entryPrice) / entryPrice * 100.0);
                }
                else
                {
                    snap.SetIndicator("PROFIT_PCT", 0.0);
                }

                // [지능형 에이전트 분석 및 점수 주입]
                double agentScore = 50.0;
                if (i >= 20)
                {
                    // AgentManager를 통해 통합 분석 (Coordinator의 가중치 적용됨)
                    var analysis = App64.Services.AgentManager.Instance.Analyze(snap.Code, data, i, indicators);
                    agentScore = analysis.FinalScore;

                    // 개별 에이전트 점수도 지표로 주입 (차트 시각화 및 디버깅용)
                    foreach (var detail in analysis.Details)
                    {
                        snap.SetIndicator($"AGENT_{detail.Agent}", detail.Score);
                    }
                }
                
                snap.SetIndicator("AGENT_SCORE", agentScore);

                var res = _engine.Evaluate(strategy, snapshots, i);
                res.AgentScore = agentScore; // 결과에 점수 보강
                
                // [상태 업데이트 및 중복 신호 필터링]
                // 1. 매수 신호: 포지션이 없을 때만 유효 -> 진입
                if (res.IsBuySignal && !hasPosition)
                {
                    hasPosition = true;
                    entryPrice = snap.Close;
                    // res.IsBuySignal = true; // 유지
                }
                else if (res.IsBuySignal && hasPosition)
                {
                    // 이미 포지션 보유 중이면 매수 신호 무시 (중복 진입 방지)
                    res.IsBuySignal = false; 
                }

                // 2. 매도 신호: 포지션이 있을 때만 유효 -> 청산
                if (res.IsSellSignal && hasPosition)
                {
                    // 매수 신호와 동시 발생 시, 당일 청산은 보수적으로 다음 봉으로 미루거나, 
                    // 혹은 전략 엔진에서 우선순위를 정했어야 함.
                    // 여기서는 매수 진입 직후 바로 매도 조건이 뜨면 당일 스캘핑으로 인정.
                    // 하지만 위 조건문 흐름상, 만약 이번 봉에서 Buy해서 hasPosition=true가 되었다면,
                    // 바로 아래 로직을 타게 됨. 즉 같은 봉에서 진입/청산 동시 발생 가능.
                    // 보통은 진입봉에서는 청산 안 함 -> i > entryIndex 체크 필요하나,
                    // 급변동 시 '진입 후 즉시 손절'도 가능해야 하므로 일단 허용.
                    // 단, 위에서 Buy로 인해 hasPosition이 true가 된 경우, 이번 턴의 Sell은 무시하는 게 일반적 (다음 봉부터 청산 감시).
                    // -> entryPrice가 방금 설정되었다면(즉 진입봉이라면) 청산 스킵?
                    // -> 일단 단순하게 감.
                    
                    hasPosition = false;
                    entryPrice = 0;
                    // res.IsSellSignal = true; // 유지
                }
                else if (res.IsSellSignal && !hasPosition)
                {
                    // 포지션 없는데 매도 신호 -> 무시 (공매도 로직이 아니면)
                    res.IsSellSignal = false;
                }
                
                // [중요] 같은 봉에서 Buy/Sell 동시 발생 시 처리
                // 위 로직대로면:
                // Case A: NoPos -> Buy -> HasPos -> Sell -> NoPos (하루에 사고 팔고)
                // 현재 구조상 if ... else if가 아니므로 순차 실행됨.
                // 1. res.Buy && !Pos -> Pos=true.
                // 2. res.Sell && Pos(방금 됨) -> Pos=false. 
                // 결과: IsBuy=True, IsSell=True. 마커 둘 다 찍힘. (스캘핑 승인)
                // 만약 이걸 원치 않으면(진입봉 청산 금지), 별도 flag 필요.
                // 다만 사용자의 "즉시 손절" 요구사항 등을 고려하면 허용하는 게 맞음.

                results.Add(res);
            }

            return results;
        }

        /// <summary>
        /// 생성된 평가 결과를 기반으로 FastChart에 표시할 신호(SignalMarker) 리스트를 생성함.
        /// </summary>
        public List<FastChart.SignalMarker> GenerateMarkers(List<EvaluationResult> results, List<FastChart.OHLCV> data)
        {
            var markers = new List<FastChart.SignalMarker>();
            if (results == null || data == null) return markers;

            for (int i = 0; i < results.Count; i++)
            {
                var res = results[i];
                if (i >= data.Count) break;

                if (res.IsBuySignal)
                {
                    markers.Add(new FastChart.SignalMarker
                    {
                        Time = res.Time,
                        Type = FastChart.SignalType.Buy,
                        Price = data[i].Low, // 매수 신호는 캔들 아래쪽에 표시하기 위해 저가 기준
                        Note = "Buy Signal (Pass all gates)"
                    });
                }
                else if (res.IsSellSignal)
                {
                    markers.Add(new FastChart.SignalMarker
                    {
                        Time = res.Time,
                        Type = FastChart.SignalType.Sell,
                        Price = data[i].High, // 매도 신호는 캔들 위쪽에 표시하기 위해 고가 기준
                        Note = "Sell Signal (Pass all gates)"
                    });
                }
            }
            return markers;
        }
    }
}
