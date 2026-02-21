' ═══════════════════════════════════════════════════════════════
' StrategyBridge.vb — 자연어 -> 전략 논리 변환 서비스
' ═══════════════════════════════════════════════════════════════

Imports System.Text.RegularExpressions
Imports System.Collections.Generic
Imports System.Linq
Imports MainApp.Models

Namespace Services
    Public Class StrategyBridge
        Public Shared Function CreateFromNaturalLanguage(nlPrompt As String) As StrategyDefinition
            If String.IsNullOrEmpty(nlPrompt) Then Return Nothing

            ' 1. 매수(진입)와 매도(청산) 섹션 분리
            Dim buyPart As String = ""
            Dim sellPart As String = ""

            Dim splitBuy = Regex.Split(nlPrompt, "매수|진행|진입", RegexOptions.IgnoreCase)
            If splitBuy.Length > 1 Then
                buyPart = splitBuy(0)
                Dim nextPart = splitBuy(1)
                Dim splitSell = Regex.Split(nextPart, "매도|청산|탈출", RegexOptions.IgnoreCase)
                If splitSell.Length > 1 Then
                    sellPart = splitSell(0)
                Else
                    sellPart = nextPart
                End If
            Else
                ' 구분자가 명확하지 않으면 쉼표로 분리 시도
                Dim clauses = nlPrompt.Split({","c, "."c})
                buyPart = clauses(0)
                If clauses.Length > 1 Then sellPart = String.Join(",", clauses.Skip(1))
            End If

            ' 2. 조건 추출 및 변환
            Dim buyConditions = ParseConditions(buyPart, True)
            Dim sellConditions = ParseConditions(sellPart, False)

            If buyConditions.Count = 0 AndAlso sellConditions.Count = 0 Then Return Nothing

            ' 3. 전략 조립
            Dim strategyName As String = "AI_Custom_" & DateTime.Now.ToString("HHmmss")
            Dim buyGate As New LogicGate("EntryGate", LogicalOperator.AND, buyConditions)
            Dim sellGate As New LogicGate("ExitGate", LogicalOperator.OR, sellConditions)

            ' 필요 데이터 일수 계산
            Dim maxDays As Integer = 0
            Dim allReqs = buyConditions.Concat(sellConditions).SelectMany(Function(c) {c.IndicatorA, c.IndicatorB}).Where(Function(s) Not String.IsNullOrEmpty(s))
            For Each req In allReqs
                Dim m = Regex.Match(req, "DAILY_HIGH_COND_(\d+)_")
                If m.Success Then
                    Dim d As Integer = 0
                    If Integer.TryParse(m.Groups(1).Value, d) Then
                        If d > maxDays Then maxDays = d
                    End If
                End If
            Next

            Dim strategy As New StrategyDefinition(strategyName, "자연어 해석 전략: " & nlPrompt,
                New List(Of LogicGate) From {buyGate},
                New List(Of LogicGate) From {sellGate}, nlPrompt)
            strategy.RequiredDataDays = maxDays
            Return strategy
        End Function

        Private Shared Function ParseConditions(part As String, isBuy As Boolean) As List(Of ConditionCell)
            Dim results As New List(Of ConditionCell)
            If String.IsNullOrWhiteSpace(part) Then Return results

            Dim condId As Integer = 1
            Dim prefix As String = If(isBuy, "B", "S")

            ' [패턴 0] N일 중 ... 고가 돌파 (복합 로직 - 일봉 기준)
            Dim mComplex = Regex.Match(part, "(\d+)\s*(일|봉)\s*중\s*.*(\d+)\s*%\s*이상\s*.*고가를?\s*(돌파|이상)", RegexOptions.IgnoreCase)
            If mComplex.Success Then
                Dim days = Integer.Parse(mComplex.Groups(1).Value)
                Dim pct = Integer.Parse(mComplex.Groups(3).Value)
                Dim indicatorName = $"DAILY_HIGH_COND_{days}_{pct}"
                results.Add(New ConditionCell($"{prefix}{condId}", $"{days}일중 {pct}%이상 상승일 고가 돌파", "Price", ComparisonOperator.CrossUp, indicatorName))
                condId += 1
            End If

            ' 패턴 1: 시가대비 X% 돌파/이상/하락
            Dim mOpen = Regex.Match(part, "시가대비\s*(\d+(\.\d+)?)\s*%?\s*(상승|하락)?\s*(돌파|이상|이하|초과|미만)", RegexOptions.IgnoreCase)
            If mOpen.Success Then
                Dim val = Double.Parse(mOpen.Groups(1).Value)
                If mOpen.Groups(3).Value = "하락" Then val = -val
                Dim opStr = mOpen.Groups(4).Value
                results.Add(New ConditionCell($"{prefix}{condId}", $"시가대비 {val}% {opStr}", "CHG_OPEN_PCT", MapOperator(opStr), Nothing, val))
                condId += 1
            End If

            ' 패턴 2: 틱강도 X 이상/돌파
            Dim mTick = Regex.Match(part, "(틱강도|체결강도)\s*(\w+)?\s*가?\s*(\d+(\.\d+)?)\s*(이상|돌파|초과)", RegexOptions.IgnoreCase)
            If mTick.Success Then
                Dim val = Double.Parse(mTick.Groups(3).Value)
                results.Add(New ConditionCell($"{prefix}{condId}", $"틱강도 {val} {mTick.Groups(5).Value}", "TICK_RAT", MapOperator(mTick.Groups(5).Value), Nothing, val))
                condId += 1
            End If

            ' 패턴 3: SuperTrend 상승/하락 추세
            Dim lowerPart = part.ToLower()
            If lowerPart.Contains("supertrend") OrElse lowerPart.Contains("슈퍼트렌드") Then
                If lowerPart.Contains("상승추세") OrElse lowerPart.Contains("위") OrElse (isBuy AndAlso lowerPart.Contains("돌파")) Then
                    results.Add(New ConditionCell($"{prefix}{condId}", "SuperTrend 상승 유지", "Price", ComparisonOperator.Greater, "SuperTrend"))
                    condId += 1
                ElseIf lowerPart.Contains("하락추세") OrElse lowerPart.Contains("아래") OrElse (Not isBuy AndAlso lowerPart.Contains("이탈")) Then
                    results.Add(New ConditionCell($"{prefix}{condId}", "SuperTrend 하락 유지", "Price", ComparisonOperator.Less, "SuperTrend"))
                    condId += 1
                End If
            End If

            ' 패턴 4: 매도 특화 (VI 직전, 손절 등)
            If Not isBuy Then
                If lowerPart.Contains("vi") AndAlso (lowerPart.Contains("직전") OrElse lowerPart.Contains("근접")) Then
                    results.Add(New ConditionCell($"{prefix}{condId}", "VI 상한가 근접 (99% 도달)", "Price", ComparisonOperator.GreaterEqual, "VI_UP_99"))
                    condId += 1
                End If

                Dim mStop = Regex.Match(part, "(-?\d+)\s*%\s*(하락|이탈|손절|시)", RegexOptions.IgnoreCase)
                If mStop.Success Then
                    Dim val = Double.Parse(mStop.Groups(1).Value)
                    If val > 0 Then val = -val
                    results.Add(New ConditionCell($"{prefix}{condId}", $"손절매 ({val}%)", "PROFIT_PCT", ComparisonOperator.LessEqual, Nothing, val))
                    condId += 1
                End If

                Dim mPctRange = Regex.Match(part, "(\d+(\.\d+)?)\s*%?\s*(상승|하락)?\s*(하면|시)", RegexOptions.IgnoreCase)
                If mPctRange.Success Then
                    Dim val = Double.Parse(mPctRange.Groups(1).Value)
                    Dim isProfitTarget = part.Contains("추가") OrElse part.Contains("수익") OrElse part.Contains("진입")
                    
                    If isProfitTarget Then
                        If mPctRange.Groups(3).Value <> "하락" Then
                            results.Add(New ConditionCell($"{prefix}{condId}", $"목표 수익률 ({val}%)", "PROFIT_PCT", ComparisonOperator.GreaterEqual, Nothing, val))
                            condId += 1
                        End If
                    Else
                        If mPctRange.Groups(3).Value = "하락" Then
                            results.Add(New ConditionCell($"{prefix}{condId}", $"시가대비 {val}% 하락 매도", "CHG_OPEN_PCT", ComparisonOperator.LessEqual, Nothing, -val))
                        Else
                            results.Add(New ConditionCell($"{prefix}{condId}", $"시가대비 {val}% 상승 매도", "CHG_OPEN_PCT", ComparisonOperator.GreaterEqual, Nothing, val))
                        End If
                        condId += 1
                    End If
                End If
            End If

            ' 패턴 5: 이평선 돌파/이탈
            Dim mMa = Regex.Match(part, "(\d+)\s*(이평|MA|이동평균선)\s*(돌파|이탈|상향)", RegexOptions.IgnoreCase)
            If mMa.Success Then
                Dim period = mMa.Groups(1).Value
                Dim maName = "MA_" & period
                Dim act = mMa.Groups(3).Value
                Dim op = If(act = "이탈", ComparisonOperator.CrossDown, ComparisonOperator.CrossUp)
                results.Add(New ConditionCell($"{prefix}{condId}", $"{period}이평 {act}", "Price", op, maName))
                condId += 1
            End If

            ' 패턴 6: MACD
            Dim mMacd = Regex.Match(part, "(MACD)\s*(가|이)?\s*(시그널|선)?\s*(골든|데드|상향|하향)?크로스", RegexOptions.IgnoreCase)
            If mMacd.Success Then
                Dim isGold = part.Contains("골든") OrElse part.Contains("상향")
                Dim op = If(isGold, ComparisonOperator.CrossUp, ComparisonOperator.CrossDown)
                results.Add(New ConditionCell($"{prefix}{condId}", $"MACD {(If(isGold, "골든", "데드"))}크로스", "MACD_Line", op, "MACD_Signal"))
                condId += 1
            End If

            ' 패턴 7: JMA
            Dim mJma = Regex.Match(part, "(JMA)\s*(\d+)?\s*(상향|하향)?\s*(돌파|이탈|반전)", RegexOptions.IgnoreCase)
            If mJma.Success Then
                Dim period = If(mJma.Groups(2).Success AndAlso Not String.IsNullOrEmpty(mJma.Groups(2).Value), mJma.Groups(2).Value, "14")
                Dim act = mJma.Groups(4).Value
                If act = "반전" Then
                    Dim isUp = part.Contains("상승") OrElse part.Contains("상향")
                    results.Add(New ConditionCell($"{prefix}{condId}", $"JMA({period}) {(If(isUp, "상승", "하락"))}반전", $"JMA_{period}", If(isUp, ComparisonOperator.CrossUp, ComparisonOperator.CrossDown), $"JMA_{period}_Prev"))
                Else
                    Dim op = If(act = "이탈" OrElse mJma.Groups(3).Value = "하향", ComparisonOperator.CrossDown, ComparisonOperator.CrossUp)
                    results.Add(New ConditionCell($"{prefix}{condId}", $"Price JMA({period}) {act}", "Price", op, $"JMA_{period}"))
                End If
                condId += 1
            End If

            ' 패턴 8: RSI
            Dim mRsi = Regex.Match(part, "(RSI)\s*(\d+)?\s*(가|이)?\s*(\d+)\s*(이상|이하|돌파|이탈)", RegexOptions.IgnoreCase)
            If mRsi.Success Then
                Dim period = If(mRsi.Groups(2).Success AndAlso Not String.IsNullOrEmpty(mRsi.Groups(2).Value), mRsi.Groups(2).Value, "14")
                Dim val = Double.Parse(mRsi.Groups(4).Value)
                Dim opStr = mRsi.Groups(5).Value
                Dim op = MapOperator(opStr)
                results.Add(New ConditionCell($"{prefix}{condId}", $"RSI({period}) {val} {opStr}", $"RSI_{period}", op, Nothing, val))
                condId += 1
            End If

            ' 패턴 9: 고래 체결 / 대량 거래 (Whale Flow / THI)
            Dim mWhale = Regex.Match(part, "(고래|대량)\s*(매수|매도|수급)\s*(\d+)?\s*(억|백만)?\s*(이상|유입|포착)", RegexOptions.IgnoreCase)
            If mWhale.Success Then
                Dim isBuyWhale = mWhale.Groups(2).Value <> "매도"
                Dim amount = If(mWhale.Groups(3).Success AndAlso Not String.IsNullOrEmpty(mWhale.Groups(3).Value), Double.Parse(mWhale.Groups(3).Value), 1)
                If mWhale.Groups(4).Value = "억" Then amount *= 100000000
                
                If part.Contains("유입") OrElse part.Contains("포착") Then
                    results.Add(New ConditionCell($"{prefix}{condId}", "고래 매수세 유입 (THI)", "THI_Signal", ComparisonOperator.GreaterEqual, Nothing, 1))
                Else
                    Dim ind = If(isBuyWhale, "WHALE_BUY_VOL", "WHALE_SELL_VOL")
                    results.Add(New ConditionCell($"{prefix}{condId}", $"고래 {(If(isBuyWhale, "매수", "매도"))} {mWhale.Groups(3).Value}{mWhale.Groups(4).Value} 이상", ind, ComparisonOperator.GreaterEqual, Nothing, amount))
                End If
                condId += 1
            End If

            ' 패턴 10: 프로그램 순매수
            Dim mProg = Regex.Match(part, "(프로그램|외인|기관)\s*(순매수|매수)?가?\s*(\d+)\s*(만주|주|억)?\s*(이상|돌파)", RegexOptions.IgnoreCase)
            If mProg.Success Then
                Dim amount = Double.Parse(mProg.Groups(3).Value)
                results.Add(New ConditionCell($"{prefix}{condId}", $"프로그램 순매수 {amount} 이상", "PROGRAM_NET", ComparisonOperator.GreaterEqual, Nothing, amount))
                condId += 1
            End If

            ' 패턴 11: 볼린저밴드 (BB)
            Dim mBb = Regex.Match(part, "(볼린저밴드|볼밴|BB)\s*(상한선|하한선|중심선|상단|하단)\s*(돌파|이탈|터치|근접)", RegexOptions.IgnoreCase)
            If mBb.Success Then
                Dim line = mBb.Groups(2).Value
                Dim act = mBb.Groups(3).Value
                Dim bbVar = If(line = "상한선" OrElse line = "상단", "BB_UPPER", If(line = "하한선" OrElse line = "하단", "BB_LOWER", "BB_MID"))
                Dim op = If(act = "이탈", ComparisonOperator.CrossDown, ComparisonOperator.CrossUp)
                If act = "터치" OrElse act = "근접" Then
                    op = If(line = "하한선" OrElse line = "하단", ComparisonOperator.LessEqual, ComparisonOperator.GreaterEqual)
                End If
                
                results.Add(New ConditionCell($"{prefix}{condId}", $"볼린저밴드 {line} {act}", "Price", op, bbVar))
                condId += 1
            End If

            Return results
        End Function

        Private Shared Function MapOperator(text As String) As ComparisonOperator
            If text.Contains("돌파") OrElse text.Contains("상향") Then Return ComparisonOperator.CrossUp
            If text.Contains("이탈") OrElse text.Contains("하향") Then Return ComparisonOperator.CrossDown
            If text.Contains("이상") OrElse text.Contains("초과") Then Return ComparisonOperator.GreaterEqual
            If text.Contains("이하") OrElse text.Contains("미만") Then Return ComparisonOperator.LessEqual
            Return ComparisonOperator.Greater
        End Function
    End Class
End Namespace
