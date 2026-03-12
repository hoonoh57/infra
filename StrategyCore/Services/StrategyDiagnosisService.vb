Imports System
Imports StrategyCore.Models

Namespace StrategyCore.Services
    Public Class StrategyDiagnosisService
        Public Function Diagnose(definition As StrategyDefinition, report As StrategyBaselineReport) As StrategyDiagnosisReport
            Dim diagnosis As New StrategyDiagnosisReport()

            If report Is Nothing Then
                diagnosis.Summary = "평가 결과가 없어 진단을 만들 수 없습니다."
                Return diagnosis
            End If

            If report.TradeCount = 0 Then
                diagnosis.Weaknesses.Add("평가구간에서 진입 신호가 발생하지 않았습니다.")
                diagnosis.Items.Add(New StrategyDiagnosisItem With {
                    .Category = "NoTrade",
                    .Severity = "High",
                    .Observation = "현재 조건 조합이 너무 엄격하거나 현재 구간과 맞지 않습니다.",
                    .Recommendation = "핵심 지표는 유지하고 확인 조건을 1개 줄이거나 타임프레임을 완화해 신호 발생 가능성을 먼저 확보합니다."
                })
            Else
                diagnosis.Strengths.Add($"평가구간에서 총 {report.TradeCount}건의 진입 신호를 검증했습니다.")
            End If

            If report.PrimaryMetric >= 0.5R Then
                diagnosis.Strengths.Add($"목표수익 달성률이 {report.PrimaryMetric:P2}로 절반 이상입니다.")
            Else
                diagnosis.Weaknesses.Add($"목표수익 달성률이 {report.PrimaryMetric:P2}로 낮습니다.")
                diagnosis.Items.Add(New StrategyDiagnosisItem With {
                    .Category = "TargetProfitMiss",
                    .Severity = "High",
                    .Observation = $"목표수익 {definition.TargetProfitRate:P1} 도달 전 이탈하는 신호가 {report.MissedTargetCount}건 있습니다.",
                    .Recommendation = "진입 확인을 한 단계 강화하거나 장초반 흔들림을 피하는 필터를 추가합니다."
                })
            End If

            If report.SecondaryMetric > 0 Then
                diagnosis.Strengths.Add($"평균 순수익이 {report.SecondaryMetric:P2}로 비용 차감 후에도 양수입니다.")
            Else
                diagnosis.Weaknesses.Add($"평균 순수익이 {report.SecondaryMetric:P2}로 비용 차감 후 음수입니다.")
                diagnosis.Items.Add(New StrategyDiagnosisItem With {
                    .Category = "NetReturnNegative",
                    .Severity = "High",
                    .Observation = "승률 또는 목표 달성률이 비용을 상쇄하지 못합니다.",
                    .Recommendation = "약한 초반 신호를 제외하고 추세와 거래량 확인을 동시에 요구합니다."
                })
            End If

            If report.MaxDrawdownRate <= -0.02R Then
                diagnosis.Weaknesses.Add($"평가구간 최대 낙폭이 {report.MaxDrawdownRate:P2}입니다.")
                diagnosis.Items.Add(New StrategyDiagnosisItem With {
                    .Category = "Drawdown",
                    .Severity = "Medium",
                    .Observation = "신호는 발생하지만 불리한 구간을 오래 견딥니다.",
                    .Recommendation = "손상된 추세에서 빨리 나오는 보호 조건을 강화합니다."
                })
            Else
                diagnosis.Strengths.Add($"최대 낙폭이 {report.MaxDrawdownRate:P2}로 제한적입니다.")
            End If

            If Not String.IsNullOrWhiteSpace(report.FailedExampleSummary) Then
                diagnosis.Weaknesses.Add("목표수익 미달 사례 1건을 기준으로 약점 보완 포인트를 추출할 수 있습니다.")
                diagnosis.Items.Add(New StrategyDiagnosisItem With {
                    .Category = "FailedExample",
                    .Severity = "High",
                    .Observation = report.FailedExampleSummary,
                    .Recommendation = BuildExampleRecommendation(definition, report)
                })
            End If

            If definition IsNot Nothing AndAlso definition.Timeframes.Count > 0 Then
                Dim firstFrame = definition.Timeframes(0)
                If String.Equals(firstFrame, "m1", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(firstFrame, "m3", StringComparison.OrdinalIgnoreCase) Then
                    diagnosis.Items.Add(New StrategyDiagnosisItem With {
                        .Category = "EarlySessionSensitivity",
                        .Severity = "Medium",
                        .Observation = $"{firstFrame} 기반 전략은 장초반 흔들림에 민감할 수 있습니다.",
                        .Recommendation = "동일 타임프레임 진입은 유지하되 m5 확인 또는 거래량 기울기 강화로 장초반 허위신호를 줄입니다."
                    })
                End If
            End If

            If definition IsNot Nothing AndAlso definition.Indicators.Count <= 1 Then
                diagnosis.Items.Add(New StrategyDiagnosisItem With {
                    .Category = "ThinIndicatorSet",
                    .Severity = "Low",
                    .Observation = "단일 축 지표에 치우쳐 있어 신호 품질이 흔들릴 수 있습니다.",
                    .Recommendation = "추세, 모멘텀, 거래량 중 빠진 축을 1개만 추가해 확인 구조를 보완합니다."
                })
            End If

            If diagnosis.Strengths.Count = 0 Then
                diagnosis.Strengths.Add("현재 평가만으로 뚜렷한 강점을 분리하기 어렵습니다.")
            End If

            If diagnosis.Weaknesses.Count = 0 Then
                diagnosis.Weaknesses.Add("현재 평가에서는 뚜렷한 약점이 적지만, 다른 종목과 기간으로 확장 검증이 필요합니다.")
            End If

            diagnosis.Summary = $"강점 {diagnosis.Strengths.Count}건, 약점 {diagnosis.Weaknesses.Count}건, 보완 포인트 {diagnosis.Items.Count}건"
            Return diagnosis
        End Function

        Private Shared Function BuildExampleRecommendation(definition As StrategyDefinition, report As StrategyBaselineReport) As String
            Dim timeframe = If(definition?.Timeframes?.Count > 0, definition.Timeframes(0), "m3")
            If String.Equals(timeframe, "m1", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(timeframe, "m3", StringComparison.OrdinalIgnoreCase) Then
                Return $"실패 사례 기준으로 {timeframe} 진입은 유지하되 m5 확인, 거래량20 기울기 양수, supertrend 상방 유지 조건을 함께 요구합니다."
            End If
            Return "실패 사례 기준으로 진입 조건은 유지하고 거래량과 추세 확인을 한 단계 강화합니다."
        End Function
    End Class
End Namespace
