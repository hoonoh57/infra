Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports StrategyCore.Models

Namespace StrategyCore.Services
    Public Class StrategyPromptValidationService
        Public Function Validate(prompt As String, mode As TradeMode, targetProfitRate As Double) As PromptValidationReport
            Dim normalized = If(prompt, "").Trim()
            Dim report As New PromptValidationReport()
            PopulateVisualTradeGuide(report, targetProfitRate)

            If normalized.Length = 0 Then
                report.Summary = "프롬프트가 비어 있습니다."
                report.IsFullySupported = False
                Return report
            End If

            For Each clause In SplitClauses(normalized)
                report.Clauses.Add(ValidateClause(clause, targetProfitRate))
            Next

            Dim supportedCount = report.Clauses.Where(Function(c) c.Status = "Supported").Count()
            Dim partialCount = report.Clauses.Where(Function(c) c.Status = "Partial").Count()
            Dim unsupportedCount = report.Clauses.Where(Function(c) c.Status = "Unsupported").Count()
            report.IsFullySupported = partialCount = 0 AndAlso unsupportedCount = 0
            report.Summary = $"지원 {supportedCount}개, 부분지원 {partialCount}개, 재작성 필요 {unsupportedCount}개"
            Return report
        End Function

        Private Shared Sub PopulateVisualTradeGuide(report As PromptValidationReport, targetProfitRate As Double)
            If report Is Nothing Then Return

            report.VisualTradePrinciples.Clear()
            report.VisualTradePrinciples.Add("Cross: 가격이 JMA 또는 SuperTrend 위로 실제로 올라타는 순간을 본다")
            report.VisualTradePrinciples.Add("Separation: 교차 직후 가격과 기준선 이격이 일정 기준 이상 벌어지는지 본다")
            report.VisualTradePrinciples.Add("FollowThrough: 교차 후 2~3봉 동안 이격이 유지되거나 확대되는지 본다")
            report.VisualTradePrinciples.Add("BoxFilter: 횡보/박스권에서 잦은 교차는 제외하고 확장 구간만 남긴다")

            report.RecommendedPrompts.Clear()
            report.RecommendedPrompts.Add("m3 jma 상승전환이고 supertrend 상승중이며 교차 직후 jma 이격률이 0.8% 이상이고 3봉 유지되면 매수")
            report.RecommendedPrompts.Add("m3 supertrend 상승복귀 후 2봉 이내에 거래량20 기울기 양수, obv > obvsignal, tickintensity > tickintensityavg5 이면 매수")
            report.RecommendedPrompts.Add($"m3 박스권이 아닌 구간에서 가격이 jma를 상승돌파하고 이격률이 1.0% 이상 유지되면 매수, 목표 {targetProfitRate:P0} 이상 후 jma 하락전환시 매도")
            report.RecommendedPrompts.Add("m5 횡보구간 제외, 가격이 supertrend와 jma를 동시에 상향돌파하고 초기 이격이 확대되면 매수")
        End Sub

        Private Shared Function SplitClauses(prompt As String) As List(Of String)
            Dim normalized = prompt.Replace(vbCr, " ").Replace(vbLf, " ")
            Dim parts = normalized.Split({","c}, StringSplitOptions.RemoveEmptyEntries).
                Select(Function(part) part.Trim()).
                Where(Function(part) part.Length > 0).
                ToList()
            If parts.Count = 0 Then parts.Add(normalized.Trim())
            Return parts
        End Function

        Private Shared Function ValidateClause(clause As String, targetProfitRate As Double) As PromptClauseValidation
            Dim lowered = clause.ToLowerInvariant()
            Dim result As New PromptClauseValidation With {
                .SourceText = clause
            }

            If lowered.Contains("매수") Then
                result.Category = "Entry"

                If clause.Contains("지수대비") OrElse clause.Contains("코스피 대비") OrElse clause.Contains("상대강도") Then
                    result.Status = "Supported"
                    result.Interpretation = "포착 후 지수 대비 초과상승률 필터로 해석합니다."
                    result.SuggestedRewrite = "포착 후 코스피 대비 3% 이상 강하고 5일내 매물대가 얕으면 매수"
                    Return result
                End If

                If clause.Contains("매물대") OrElse clause.Contains("상단 매물") Then
                    result.Status = "Supported"
                    result.Interpretation = "최근 5일 상단 매물 부담 필터로 해석합니다."
                    result.SuggestedRewrite = "5일내 매물대가 얕고 상단 매물 부담이 작으면 매수"
                    Return result
                End If

                If clause.Contains("틱강도") Then
                    result.Status = "Supported"
                    result.Interpretation = "틱강도와 5이평 비교 조건으로 해석합니다."
                    result.SuggestedRewrite = "틱강도 5이상 그리고 tickintensity > tickintensityavg5 이면 매수"
                    Return result
                End If

                If lowered.Contains("jma 상승전환") Then
                    result.Status = "Supported"
                    result.Interpretation = "JMA 상승전환 진입 규칙으로 해석합니다."
                    result.SuggestedRewrite = "jma 상승전환시 매수"
                ElseIf lowered.Contains("obv") AndAlso (clause.Contains("상승추세") OrElse lowered.Contains("obv > obvsignal")) Then
                    result.Status = "Supported"
                    result.Interpretation = "OBV > Signal 및 상승추세 필터로 해석합니다."
                    result.SuggestedRewrite = "obv 상승추세고 obv > obvsignal 이면 매수"
                ElseIf clause.Contains("기울기 상승중") AndAlso lowered.Contains("jma") Then
                    result.Status = "Partial"
                    result.Interpretation = "JMA 상승 방향 조건으로 근사 해석합니다."
                    result.SuggestedRewrite = "jma 상승전환이고 supertrend 상승중이면 매수"
                ElseIf clause.Contains("상승교차") OrElse clause.Contains("상향돌파") OrElse clause.Contains("돌파") Then
                    result.Status = "Partial"
                    result.Interpretation = "교차 또는 돌파 시작 구간 조건으로 해석합니다."
                    result.SuggestedRewrite = "가격이 jma를 상승돌파하고 초기 이격률이 1.0% 이상이면 매수"
                Else
                    result.Status = "Partial"
                    result.Interpretation = "지표 기반 진입 조건으로 해석합니다."
                    result.SuggestedRewrite = "macd 상승전환시 매수"
                End If

                Return result
            End If

            If clause.Contains("매도자제") OrElse clause.Contains("매도 자제") Then
                result.Category = "Hold"
                If lowered.Contains("supertrend") AndAlso clause.Contains("미만") Then
                    result.Status = "Supported"
                    result.Interpretation = $"목표수익 {targetProfitRate:P0} 미달 구간에서 SuperTrend 상승 유지 시 보유로 해석합니다."
                    result.SuggestedRewrite = $"목표 {targetProfitRate:P0} 미만이고 supertrend 상승중이면 매도자제"
                Else
                    result.Status = "Partial"
                    result.Interpretation = "보유 예외 규칙으로 부분 해석합니다."
                    result.SuggestedRewrite = $"목표 {targetProfitRate:P0} 미만이고 supertrend 상승중이면 매도자제"
                End If
                Return result
            End If

            If lowered.Contains("매도") Then
                result.Category = "Exit"
                If lowered.Contains("supertrend 하락전환") Then
                    result.Status = "Supported"
                    result.Interpretation = "SuperTrend 하락전환 보호청산으로 해석합니다."
                    result.SuggestedRewrite = "supertrend 하락전환시 매도"
                ElseIf lowered.Contains("jma 하락전환") Then
                    result.Status = "Supported"
                    result.Interpretation = $"목표수익 {targetProfitRate:P0} 이상 구간에서 JMA 하락전환 청산으로 해석합니다."
                    result.SuggestedRewrite = $"목표 {targetProfitRate:P0} 이상 상승 후 jma 하락전환시 매도"
                ElseIf clause.Contains("하락전환") Then
                    result.Status = "Partial"
                    result.Interpretation = "하락전환 청산 규칙으로 부분 해석합니다."
                    result.SuggestedRewrite = "jma 하락전환시 매도"
                Else
                    result.Status = "Unsupported"
                    result.Interpretation = "청산 문장이 충분히 명확하지 않습니다."
                    result.SuggestedRewrite = $"목표 {targetProfitRate:P0} 이상 상승 후 jma 하락전환시 매도"
                End If
                Return result
            End If

            result.Category = "Context"
            If lowered.Contains("m1") OrElse lowered.Contains("m3") OrElse lowered.Contains("m5") OrElse lowered.Contains("t30") OrElse lowered.Contains("t60") OrElse lowered.Contains("t120") Then
                result.Status = "Supported"
                result.Interpretation = "타임프레임 지정으로 해석합니다."
                result.SuggestedRewrite = clause
            Else
                result.Status = "Partial"
                result.Interpretation = "보조 문맥으로 보이지만 실행 규칙으로 직접 반영되지는 않습니다."
                result.SuggestedRewrite = clause
            End If

            Return result
        End Function
    End Class
End Namespace
