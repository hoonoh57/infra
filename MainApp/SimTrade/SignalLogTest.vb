' ═══════════════════════════════════════════════════════════════
' SignalLogTest.vb — 7조건 상세 로그 검증 (장외 테스트용)
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

    Public Class SignalLogTest

        Public Shared Function RunAll(settings As SimTradeSettings) As String
            Dim sb As New System.Text.StringBuilder()
            sb.AppendLine("═══ 7조건 상세 로그 테스트 시작 ═══")

            ' ★ 테스트용 시간 설정 — 장중으로 오버라이드
            Dim savedStart = settings.TradingStartTime
            Dim savedNoNew = settings.NoNewBuyAfter
            Dim savedForce = settings.ForceCloseTime
            settings.TradingStartTime = TimeSpan.FromHours(0)
            settings.NoNewBuyAfter = TimeSpan.FromHours(23)
            settings.ForceCloseTime = TimeSpan.FromHours(23.5)

            Dim evaluator As New SignalEvaluator(settings)

            ' ── 시나리오 A: 전체 미충족 (0/7) ──
            sb.AppendLine()
            sb.AppendLine("── 시나리오 A: 전체 미충족 ──")
            Dim stateA = CreateTestState("TEST01", "테스트종목A")
            stateA.ST_Direction = -1
            stateA.JMA_Direction = -1
            stateA.JMA_TurnBar = -1
            stateA.TickSum_Normalized = 1.0
            stateA.TickMA5_Normalized = 3.0
            stateA.OBV_Direction = -1
            stateA.RSI_Value = 45
            stateA.MACD_Histogram = -0.5
            stateA.Volume_Ratio = 60
            FillTestCandles(stateA, settings)

            Dim resultA = evaluator.EvaluateBuy(stateA, 0, 50000000, 100000000)
            FormatResult(sb, stateA, resultA)

            ' ── 시나리오 B: 부분 충족 (4/7) ──
            sb.AppendLine()
            sb.AppendLine("── 시나리오 B: 부분 충족 ──")
            Dim stateB = CreateTestState("TEST02", "테스트종목B")
            stateB.ST_Direction = 1
            stateB.JMA_Direction = 1
            stateB.JMA_TurnBar = 1
            stateB.TickSum_Normalized = 2.0
            stateB.TickMA5_Normalized = 3.0
            stateB.OBV_Direction = 1
            stateB.RSI_Value = 62
            stateB.MACD_Histogram = -0.3
            stateB.Volume_Ratio = 80
            FillTestCandles(stateB, settings)

            Dim resultB = evaluator.EvaluateBuy(stateB, 0, 50000000, 100000000)
            FormatResult(sb, stateB, resultB)

            ' ── 시나리오 C: 전체 충족 (7/7) ──
            sb.AppendLine()
            sb.AppendLine("── 시나리오 C: 전체 충족 (7/7) ──")
            Dim stateC = CreateTestState("TEST03", "테스트종목C")
            stateC.ST_Direction = 1
            stateC.JMA_Direction = 1
            stateC.JMA_TurnBar = 0
            stateC.TickSum_Normalized = 8.5
            stateC.TickMA5_Normalized = 5.0
            stateC.TickMA20_Normalized = 4.0
            stateC.OBV_Direction = 1
            stateC.RSI_Value = 63
            stateC.MACD_Histogram = 1.2
            stateC.Volume_Ratio = 180
            stateC.CurrentPrice = 25000
            stateC.Ask1 = 25100
            stateC.Bid1 = 25000
            FillTestCandles(stateC, settings)
            InjectMACDGoldenCross(stateC, settings)

            Dim resultC = evaluator.EvaluateBuy(stateC, 0, 50000000, 100000000)
            FormatResult(sb, stateC, resultC)

            ' ── 시나리오 D: 매도 P0 Grace Period ──
            sb.AppendLine()
            sb.AppendLine("── 시나리오 D: 매도 P0 Grace Period ──")
            Dim stateD = CreateTestState("TEST04", "테스트종목D")
            stateD.HasPosition = True
            stateD.BuyPrice = 20000
            stateD.BuyQty = 10
            stateD.CurrentPrice = 19900
            stateD.CurrentPnLRate = -0.5
            stateD.HighSinceBuy = 20100
            stateD.ST_Direction = 1
            stateD.JMA_Direction = -1
            stateD.TickSum_Normalized = 2.0
            stateD.TickMA5_Normalized = 5.0
            stateD.OBV_Direction = -1
            FillTestCandles(stateD, settings)
            ' BuyTime을 캔들 마지막에서 2봉 전으로 설정 → GracePeriod 안
            stateD.BuyTime = stateD.Candles(stateD.Candles.Count - 3).Dt

            Dim resultD = evaluator.EvaluateSell(stateD)
            sb.AppendLine($"  매도판단: ShouldSell={resultD.ShouldSell}, Priority={resultD.Priority}")
            sb.AppendLine($"  사유: {resultD.Reason}")

            ' ── 시나리오 E: 매도 P1 ST 전환 ──
            sb.AppendLine()
            sb.AppendLine("── 시나리오 E: 매도 P1 ST 전환 ──")
            Dim stateE = CreateTestState("TEST05", "테스트종목E")
            stateE.HasPosition = True
            stateE.BuyPrice = 30000
            stateE.BuyQty = 5
            stateE.CurrentPrice = 30500
            stateE.CurrentPnLRate = 1.67
            stateE.HighSinceBuy = 31000
            stateE.ST_Direction = -1
            stateE.JMA_Direction = 1
            stateE.OBV_Direction = 1
            stateE.TickSum_Normalized = 6.0
            stateE.TickMA5_Normalized = 5.0
            FillTestCandles(stateE, settings)
            ' BuyTime을 캔들 시작 근처 → GracePeriod 밖
            stateE.BuyTime = stateE.Candles(0).Dt

            Dim resultE = evaluator.EvaluateSell(stateE)
            sb.AppendLine($"  매도판단: ShouldSell={resultE.ShouldSell}, Priority={resultE.Priority}")
            sb.AppendLine($"  사유: {resultE.Reason}")

            ' ── 시나리오 F: 매도 P6 손절 ──
            sb.AppendLine()
            sb.AppendLine("── 시나리오 F: 매도 P6 손절 ──")
            Dim stateF = CreateTestState("TEST06", "테스트종목F")
            stateF.HasPosition = True
            stateF.BuyPrice = 10000
            stateF.BuyQty = 20
            stateF.CurrentPrice = 9650
            stateF.CurrentPnLRate = -3.5
            stateF.HighSinceBuy = 10200
            stateF.ST_Direction = 1    ' ST는 상승 유지 → P1 안걸림
            stateF.JMA_Direction = 1   ' JMA도 상승 → P0 악화 안걸림
            stateF.OBV_Direction = 1   ' OBV도 상승 → P0/P5 안걸림
            stateF.TickSum_Normalized = 6.0
            stateF.TickMA5_Normalized = 5.0
            FillTestCandles(stateF, settings)
            stateF.BuyTime = stateF.Candles(0).Dt   ' GracePeriod 밖

            Dim resultF = evaluator.EvaluateSell(stateF)
            sb.AppendLine($"  매도판단: ShouldSell={resultF.ShouldSell}, Priority={resultF.Priority}")
            sb.AppendLine($"  사유: {resultF.Reason}")

            ' ── 시나리오 G: 매도 P7 트레일링 ──
            sb.AppendLine()
            sb.AppendLine("── 시나리오 G: 매도 P7 트레일링 ──")
            Dim stateG = CreateTestState("TEST07", "테스트종목G")
            stateG.HasPosition = True
            stateG.BuyPrice = 10000
            stateG.BuyQty = 15
            stateG.CurrentPrice = 10300
            stateG.CurrentPnLRate = 3.0
            stateG.HighSinceBuy = 10500    ' 고점 10500 → 현재 10300 = -1.9% 하락
            stateG.ST_Direction = 1
            stateG.JMA_Direction = 1
            stateG.OBV_Direction = 1
            stateG.TickSum_Normalized = 6.0
            stateG.TickMA5_Normalized = 5.0
            FillTestCandles(stateG, settings)
            stateG.BuyTime = stateG.Candles(0).Dt

            Dim resultG = evaluator.EvaluateSell(stateG)
            sb.AppendLine($"  매도판단: ShouldSell={resultG.ShouldSell}, Priority={resultG.Priority}")
            sb.AppendLine($"  사유: {resultG.Reason}")

            ' ── 설정 복원 ──
            settings.TradingStartTime = savedStart
            settings.NoNewBuyAfter = savedNoNew
            settings.ForceCloseTime = savedForce

            ' ── 결과 요약 ──
            sb.AppendLine()
            Dim passA = (resultA.ConditionsMet = 0 AndAlso Not resultA.ShouldBuy)
            Dim passB = (resultB.ConditionsMet >= 2 AndAlso resultB.ConditionsMet <= 5 AndAlso Not resultB.ShouldBuy)
            Dim passC = (resultC.ConditionsMet = 7 AndAlso resultC.ShouldBuy)
            Dim passD = (resultD.ShouldSell AndAlso resultD.Priority = "P0")
            Dim passE = (resultE.ShouldSell AndAlso resultE.Priority = "P1")
            Dim passF = (resultF.ShouldSell AndAlso resultF.Priority = "P6")
            Dim passG = (resultG.ShouldSell AndAlso resultG.Priority = "P7")

            sb.AppendLine("═══ 결과 요약 ═══")
            sb.AppendLine($"  A(0/7 미충족): {TF(passA)} — met={resultA.ConditionsMet}")
            sb.AppendLine($"  B(부분충족):   {TF(passB)} — met={resultB.ConditionsMet}, buy={resultB.ShouldBuy}")
            sb.AppendLine($"  C(7/7 매수):   {TF(passC)} — met={resultC.ConditionsMet}, buy={resultC.ShouldBuy}")
            sb.AppendLine($"  D(P0 Grace):   {TF(passD)} — {resultD.Priority}")
            sb.AppendLine($"  E(P1 ST전환):  {TF(passE)} — {resultE.Priority}")
            sb.AppendLine($"  F(P6 손절):    {TF(passF)} — {resultF.Priority}")
            sb.AppendLine($"  G(P7 트레일):  {TF(passG)} — {resultG.Priority}")

            Dim allPass = passA AndAlso passB AndAlso passC AndAlso passD AndAlso passE AndAlso passF AndAlso passG
            sb.AppendLine()
            sb.AppendLine($"═══ 최종: {If(allPass, "ALL PASS (7/7)", "FAIL")} ═══")

            Return sb.ToString()
        End Function

        Private Shared Function TF(v As Boolean) As String
            Return If(v, "PASS", "FAIL")
        End Function

        Private Shared Function CreateTestState(code As String, name As String) As StockState
            Dim s As New StockState()
            s.Code = code
            s.Name = name
            s.State = DataState.Ready
            s.CurrentPrice = 20000
            s.Ask1 = 20100
            s.Bid1 = 20000
            s.PrevClose = 19500
            s.ChangeRate = 2.5
            Return s
        End Function

        Private Shared Sub FillTestCandles(state As StockState, settings As SimTradeSettings)
            Dim rng As New Random(42)
            Dim basePrice As Single = state.CurrentPrice
            Dim totalCandles = settings.MinCandlesForSignal + 10
            For i = 0 To totalCandles
                Dim c As New CandleItem()
                c.Dt = DateTime.Today.AddSeconds(i * 30 + 9 * 3600)
                Dim change = CSng((rng.NextDouble() - 0.48) * 100)
                basePrice += change
                If basePrice < 1000 Then basePrice = 1000
                c.Open = basePrice - CSng(rng.NextDouble() * 50)
                c.High = basePrice + CSng(rng.NextDouble() * 80)
                c.Low = basePrice - CSng(rng.NextDouble() * 80)
                c.Close = basePrice
                c.Volume = CLng(rng.Next(10000, 300000))
                c.TickCount = rng.Next(5, 50)
                c.NormalizedTickSum = c.TickCount * 2.0
                state.Candles.Add(c)
            Next
        End Sub

        Private Shared Sub InjectMACDGoldenCross(state As StockState, settings As SimTradeSettings)
            Try
                state.Engine.Register(New SuperTrend_Indicator(settings.ST_Period, settings.ST_Multiplier))
                state.Engine.Register(New RSI_Indicator(settings.RSI_Period))
                state.Engine.Register(New Volume_Indicator())
                state.Engine.Register(New OBV_Indicator())
                state.Engine.Register(New MACD_Indicator(settings.MACD_Fast, settings.MACD_Slow, settings.MACD_Signal))
                state.Engine.Register(New JMA_Indicator(settings.JMA_Period, settings.JMA_Phase, settings.JMA_Power))
            Catch
            End Try

            Dim last = state.Candles(state.Candles.Count - 1)
            For i = 1 To 10
                Dim c As New CandleItem()
                c.Dt = last.Dt.AddSeconds(i * 30)
                c.Open = last.Close + i * 30
                c.High = last.Close + i * 50
                c.Low = last.Close + i * 10
                c.Close = last.Close + i * 40
                c.Volume = CLng(300000 + i * 50000)
                c.TickCount = 30 + i * 5
                c.NormalizedTickSum = c.TickCount * 2.0
                state.Candles.Add(c)
            Next

            state.Engine.CalculateAll(state.Candles)
        End Sub

        Private Shared Sub FormatResult(sb As System.Text.StringBuilder, state As StockState, result As BuySignalResult)
            Dim c1 = If(result.C1_ST, "●", "○")
            Dim c2 = If(result.C2_JMA, "●", "○")
            Dim c3 = If(result.C3_TickSum, "●", "○")
            Dim c4 = If(result.C4_OBV, "●", "○")
            Dim c5 = If(result.C5_Confirm, "●", "○")
            Dim c6 = If(result.C6_MACD, "●", "○")
            Dim c7 = If(result.C7_Volume, "●", "○")
            Dim met = result.ConditionsMet

            sb.AppendLine($"  {state.Code} {state.Name} [{met}/7]")
            sb.AppendLine($"  C1:ST{c1} C2:JMA{c2} C3:Tick{c3} C4:OBV{c4} C5:동시{c5} C6:MACD{c6} C7:Vol{c7}")
            sb.AppendLine($"  ST={state.ST_Direction:F0} JMA={state.JMA_Direction:F0}(턴{state.JMA_TurnBar}) " &
                          $"Tick={state.TickSum_Normalized:F1} OBV={state.OBV_Direction:F0} " &
                          $"RSI={state.RSI_Value:F0} MACD_H={state.MACD_Histogram:F2} VolR={state.Volume_Ratio:F0}%")
            sb.AppendLine($"  ShouldBuy={result.ShouldBuy}, 사유: {result.Reason}")
            If result.RejectReasons.Count > 0 Then
                sb.AppendLine($"  미충족: {String.Join(", ", result.RejectReasons)}")
            End If
        End Sub

    End Class

End Namespace
