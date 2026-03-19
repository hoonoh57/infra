' ═══════════════════════════════════════════════════════════════
' IndicatorIncrementalTest.vb — 증분 계산 정합성 검증 (2-4 + 3-1~3-3)
' ═══════════════════════════════════════════════════════════════
' ★ 테스트 A: OBV  Calculate vs UpdateLast 결과 비교
' ★ 테스트 B: RSI  Calculate vs UpdateLast 결과 비교
' ★ 테스트 C: Volume Calculate vs UpdateLast 결과 비교
' ★ 테스트 D: MACD  Calculate vs UpdateLast 결과 비교
' ★ 테스트 E: JMA   Calculate vs UpdateLast 결과 비교
' ★ 테스트 F: SuperTrend Calculate vs UpdateLast 결과 비교
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

    Public Class IndicatorIncrementalTest

        ' ───────────── 전체 실행 ─────────────
        Public Shared Function RunAll() As String
            Dim sb As New System.Text.StringBuilder()
            sb.AppendLine("═══ 증분 계산 검증 테스트 시작 ═══")

            Dim candles = GenerateTestCandles(100)

            Dim passA = TestOBV(candles, sb)
            Dim passB = TestRSI(candles, sb)
            Dim passC = TestVolume(candles, sb)
            Dim passD = TestMACD(candles, sb)
            Dim passE = TestJMA(candles, sb)
            Dim passF = TestSuperTrend(candles, sb)

            sb.AppendLine()
            Dim allPass = passA AndAlso passB AndAlso passC AndAlso passD AndAlso passE AndAlso passF
            If allPass Then
                sb.AppendLine("═══ 결과: ALL PASS (6/6) ═══")
            Else
                sb.AppendLine($"═══ 결과: FAIL (OBV={TF(passA)}, RSI={TF(passB)}, VOL={TF(passC)}, MACD={TF(passD)}, JMA={TF(passE)}, ST={TF(passF)}) ═══")
            End If
            Return sb.ToString()
        End Function

        Private Shared Function TF(v As Boolean) As String
            Return If(v, "OK", "FAIL")
        End Function

        ' ───────────── 테스트 A: OBV ─────────────
        Private Shared Function TestOBV(candles As List(Of CandleItem), sb As System.Text.StringBuilder) As Boolean
            sb.AppendLine()
            sb.AppendLine("── 테스트 A: OBV ──")
            Dim indicator As New OBV_Indicator(20)
            Dim allPass = True
            Dim tolerance = 0.01F
            Dim fullResults = indicator.Calculate(candles)
            Dim indicator2 As New OBV_Indicator(20)
            Dim inc As New List(Of CandleItem)
            Dim prev As List(Of IndicatorResult) = Nothing
            For i = 0 To candles.Count - 1
                inc.Add(candles(i))
                If i = 0 Then
                    prev = indicator2.Calculate(inc)
                Else
                    Dim lr = indicator2.UpdateLast(inc, prev)
                    If prev.Count > inc.Count - 1 Then
                        prev(prev.Count - 1) = lr
                    Else
                        prev.Add(lr)
                    End If
                End If
                If i > 0 AndAlso i Mod 10 = 0 Then
                    Dim rr = indicator2.UpdateLast(inc, prev)
                    prev(prev.Count - 1) = rr
                End If
            Next
            For i = 0 To candles.Count - 1
                If i >= fullResults.Count OrElse i >= prev.Count Then
                    allPass = False : Continue For
                End If
                Dim fO = fullResults(i).Val("OBV")
                Dim iO = prev(i).Val("OBV")
                If Math.Abs(fO - iO) > tolerance Then
                    sb.AppendLine($"  [FAIL] 봉{i}: OBV Full={fO:F2} vs Inc={iO:F2}")
                    allPass = False
                End If
            Next
            sb.AppendLine($"  OBV: {TF(allPass)}")
            Return allPass
        End Function

        ' ───────────── 테스트 B: RSI ─────────────
        Private Shared Function TestRSI(candles As List(Of CandleItem), sb As System.Text.StringBuilder) As Boolean
            sb.AppendLine()
            sb.AppendLine("── 테스트 B: RSI ──")
            Dim indicator As New RSI_Indicator(14)
            Dim allPass = True
            Dim tolerance = 0.1F
            Dim fullResults = indicator.Calculate(candles)
            Dim indicator2 As New RSI_Indicator(14)
            Dim inc As New List(Of CandleItem)
            Dim prev As List(Of IndicatorResult) = Nothing
            For i = 0 To candles.Count - 1
                inc.Add(candles(i))
                If i <= 14 Then
                    prev = indicator2.Calculate(inc)
                Else
                    Dim lr = indicator2.UpdateLast(inc, prev)
                    If prev.Count > inc.Count - 1 Then
                        prev(prev.Count - 1) = lr
                    Else
                        prev.Add(lr)
                    End If
                End If
                If i > 14 AndAlso i Mod 7 = 0 Then
                    Dim rr = indicator2.UpdateLast(inc, prev)
                    prev(prev.Count - 1) = rr
                End If
            Next
            For i = 0 To candles.Count - 1
                If i >= fullResults.Count OrElse i >= prev.Count Then Continue For
                Dim fV = fullResults(i).Val("Value")
                Dim iV = prev(i).Val("Value")
                If Single.IsNaN(fV) AndAlso Single.IsNaN(iV) Then Continue For
                If Single.IsNaN(fV) <> Single.IsNaN(iV) Then
                    sb.AppendLine($"  [FAIL] 봉{i}: RSI NaN 불일치")
                    allPass = False : Continue For
                End If
                If Math.Abs(fV - iV) > tolerance Then
                    sb.AppendLine($"  [FAIL] 봉{i}: RSI Full={fV:F2} vs Inc={iV:F2}")
                    allPass = False
                End If
            Next
            sb.AppendLine($"  RSI: {TF(allPass)}")
            Return allPass
        End Function

        ' ───────────── 테스트 C: Volume ─────────────
        Private Shared Function TestVolume(candles As List(Of CandleItem), sb As System.Text.StringBuilder) As Boolean
            sb.AppendLine()
            sb.AppendLine("── 테스트 C: Volume ──")
            Dim indicator As New Volume_Indicator(20)
            Dim allPass = True
            Dim tolerance = 0.01F
            Dim fullResults = indicator.Calculate(candles)
            Dim indicator2 As New Volume_Indicator(20)
            Dim inc As New List(Of CandleItem)
            Dim prev As List(Of IndicatorResult) = Nothing
            For i = 0 To candles.Count - 1
                inc.Add(candles(i))
                If i = 0 Then
                    prev = indicator2.Calculate(inc)
                Else
                    Dim lr = indicator2.UpdateLast(inc, prev)
                    If prev.Count > inc.Count - 1 Then
                        prev(prev.Count - 1) = lr
                    Else
                        prev.Add(lr)
                    End If
                End If
                If i > 0 AndAlso i Mod 5 = 0 Then
                    Dim rr = indicator2.UpdateLast(inc, prev)
                    prev(prev.Count - 1) = rr
                End If
            Next
            For i = 0 To candles.Count - 1
                If i >= fullResults.Count OrElse i >= prev.Count Then Continue For
                Dim fMA = fullResults(i).Val("MA")
                Dim iMA = prev(i).Val("MA")
                If Single.IsNaN(fMA) AndAlso Single.IsNaN(iMA) Then Continue For
                If Single.IsNaN(fMA) <> Single.IsNaN(iMA) Then
                    sb.AppendLine($"  [FAIL] 봉{i}: MA NaN 불일치")
                    allPass = False : Continue For
                End If
                If Not Single.IsNaN(fMA) AndAlso Math.Abs(fMA - iMA) > tolerance Then
                    sb.AppendLine($"  [FAIL] 봉{i}: MA Full={fMA:F2} vs Inc={iMA:F2}")
                    allPass = False
                End If
            Next
            sb.AppendLine($"  Volume: {TF(allPass)}")
            Return allPass
        End Function

        ' ───────────── 테스트 D: MACD ─────────────
        Private Shared Function TestMACD(candles As List(Of CandleItem), sb As System.Text.StringBuilder) As Boolean
            sb.AppendLine()
            sb.AppendLine("── 테스트 D: MACD ──")
            Dim indicator As New MACD_Indicator(7, 14, 9)
            Dim allPass = True
            Dim tolerance = 0.5F   ' EMA 누적 → 허용폭 넓게
            Dim fullResults = indicator.Calculate(candles)
            Dim indicator2 As New MACD_Indicator(7, 14, 9)
            Dim inc As New List(Of CandleItem)
            Dim prev As List(Of IndicatorResult) = Nothing
            Dim initBars = 14  ' slow period

            For i = 0 To candles.Count - 1
                inc.Add(candles(i))
                If i <= initBars Then
                    prev = indicator2.Calculate(inc)
                Else
                    Dim lr = indicator2.UpdateLast(inc, prev)
                    If prev.Count > inc.Count - 1 Then
                        prev(prev.Count - 1) = lr
                    Else
                        prev.Add(lr)
                    End If
                End If
                ' 재호출 테스트 (같은 봉 틱 업데이트)
                If i > initBars AndAlso i Mod 8 = 0 Then
                    Dim rr = indicator2.UpdateLast(inc, prev)
                    prev(prev.Count - 1) = rr
                End If
            Next

            Dim mismatchCount = 0
            For i = 0 To candles.Count - 1
                If i >= fullResults.Count OrElse i >= prev.Count Then Continue For
                Dim fM = fullResults(i).Val("MACD")
                Dim iM = prev(i).Val("MACD")
                Dim fS = fullResults(i).Val("Signal")
                Dim iSs = prev(i).Val("Signal")
                Dim fH = fullResults(i).Val("Histogram")
                Dim iH = prev(i).Val("Histogram")

                If Single.IsNaN(fM) AndAlso Single.IsNaN(iM) Then Continue For
                If Single.IsNaN(fM) <> Single.IsNaN(iM) Then
                    If mismatchCount < 5 Then sb.AppendLine($"  [FAIL] 봉{i}: MACD NaN 불일치")
                    mismatchCount += 1
                    allPass = False : Continue For
                End If
                If Not Single.IsNaN(fM) AndAlso Math.Abs(fM - iM) > tolerance Then
                    If mismatchCount < 5 Then sb.AppendLine($"  [FAIL] 봉{i}: MACD Full={fM:F2} vs Inc={iM:F2} (차이={Math.Abs(fM - iM):F4})")
                    mismatchCount += 1
                    allPass = False
                End If
                If Not Single.IsNaN(fS) AndAlso Not Single.IsNaN(iSs) AndAlso Math.Abs(fS - iSs) > tolerance Then
                    If mismatchCount < 5 Then sb.AppendLine($"  [FAIL] 봉{i}: Signal Full={fS:F2} vs Inc={iSs:F2}")
                    mismatchCount += 1
                    allPass = False
                End If
            Next
            If mismatchCount > 5 Then sb.AppendLine($"  ... 외 {mismatchCount - 5}건 추가 불일치")
            sb.AppendLine($"  MACD: {TF(allPass)} (불일치 {mismatchCount}건)")
            Return allPass
        End Function

        ' ───────────── 테스트 E: JMA ─────────────
        Private Shared Function TestJMA(candles As List(Of CandleItem), sb As System.Text.StringBuilder) As Boolean
            sb.AppendLine()
            sb.AppendLine("── 테스트 E: JMA ──")
            Dim indicator As New JMA_Indicator(14, 50, 2)
            Dim allPass = True
            Dim tolerance = 0.5F   ' JMA 내부 재귀 → 허용폭 넓게
            Dim fullResults = indicator.Calculate(candles)
            Dim indicator2 As New JMA_Indicator(14, 50, 2)
            Dim inc As New List(Of CandleItem)
            Dim prev As List(Of IndicatorResult) = Nothing
            Dim initBars = 14

            For i = 0 To candles.Count - 1
                inc.Add(candles(i))
                If i <= initBars Then
                    prev = indicator2.Calculate(inc)
                Else
                    Dim lr = indicator2.UpdateLast(inc, prev)
                    If prev.Count > inc.Count - 1 Then
                        prev(prev.Count - 1) = lr
                    Else
                        prev.Add(lr)
                    End If
                End If
                ' 재호출 테스트
                If i > initBars AndAlso i Mod 6 = 0 Then
                    Dim rr = indicator2.UpdateLast(inc, prev)
                    prev(prev.Count - 1) = rr
                End If
            Next

            Dim mismatchCount = 0
            For i = 0 To candles.Count - 1
                If i >= fullResults.Count OrElse i >= prev.Count Then Continue For
                Dim fV = fullResults(i).Val("Value")
                Dim iV = prev(i).Val("Value")

                If Single.IsNaN(fV) AndAlso Single.IsNaN(iV) Then Continue For
                If Single.IsNaN(fV) <> Single.IsNaN(iV) Then
                    If mismatchCount < 5 Then sb.AppendLine($"  [FAIL] 봉{i}: JMA NaN 불일치")
                    mismatchCount += 1
                    allPass = False : Continue For
                End If
                If Not Single.IsNaN(fV) AndAlso Math.Abs(fV - iV) > tolerance Then
                    If mismatchCount < 5 Then sb.AppendLine($"  [FAIL] 봉{i}: JMA Full={fV:F1} vs Inc={iV:F1} (차이={Math.Abs(fV - iV):F2})")
                    mismatchCount += 1
                    allPass = False
                End If
            Next
            If mismatchCount > 5 Then sb.AppendLine($"  ... 외 {mismatchCount - 5}건 추가 불일치")
            sb.AppendLine($"  JMA: {TF(allPass)} (불일치 {mismatchCount}건)")
            Return allPass
        End Function

        ' ───────────── 테스트 F: SuperTrend ─────────────
        Private Shared Function TestSuperTrend(candles As List(Of CandleItem), sb As System.Text.StringBuilder) As Boolean
            sb.AppendLine()
            sb.AppendLine("── 테스트 F: SuperTrend ──")
            Dim indicator As New SuperTrend_Indicator(10, 3.0F)
            Dim allPass = True
            Dim tolerance = 0.5F
            Dim fullResults = indicator.Calculate(candles)
            Dim indicator2 As New SuperTrend_Indicator(10, 3.0F)
            Dim inc As New List(Of CandleItem)
            Dim prev As List(Of IndicatorResult) = Nothing
            Dim initBars = 10

            For i = 0 To candles.Count - 1
                inc.Add(candles(i))
                If i <= initBars Then
                    prev = indicator2.Calculate(inc)
                Else
                    Dim lr = indicator2.UpdateLast(inc, prev)
                    If prev.Count > inc.Count - 1 Then
                        prev(prev.Count - 1) = lr
                    Else
                        prev.Add(lr)
                    End If
                End If
                ' 재호출 테스트
                If i > initBars AndAlso i Mod 7 = 0 Then
                    Dim rr = indicator2.UpdateLast(inc, prev)
                    prev(prev.Count - 1) = rr
                End If
            Next

            Dim mismatchCount = 0
            For i = 0 To candles.Count - 1
                If i >= fullResults.Count OrElse i >= prev.Count Then Continue For
                Dim fV = fullResults(i).Val("Value")
                Dim iV = prev(i).Val("Value")
                Dim fD = fullResults(i).Val("Direction")
                Dim iD = prev(i).Val("Direction")

                If Single.IsNaN(fV) AndAlso Single.IsNaN(iV) Then Continue For
                If Single.IsNaN(fV) <> Single.IsNaN(iV) Then
                    If mismatchCount < 5 Then sb.AppendLine($"  [FAIL] 봉{i}: ST NaN 불일치")
                    mismatchCount += 1
                    allPass = False : Continue For
                End If
                If Not Single.IsNaN(fV) AndAlso Math.Abs(fV - iV) > tolerance Then
                    If mismatchCount < 5 Then sb.AppendLine($"  [FAIL] 봉{i}: ST Full={fV:F1} vs Inc={iV:F1} (차이={Math.Abs(fV - iV):F2})")
                    mismatchCount += 1
                    allPass = False
                End If
                If Not Single.IsNaN(fD) AndAlso Not Single.IsNaN(iD) AndAlso fD <> iD Then
                    If mismatchCount < 5 Then sb.AppendLine($"  [FAIL] 봉{i}: Direction Full={fD} vs Inc={iD}")
                    mismatchCount += 1
                    allPass = False
                End If
            Next
            If mismatchCount > 5 Then sb.AppendLine($"  ... 외 {mismatchCount - 5}건 추가 불일치")
            sb.AppendLine($"  SuperTrend: {TF(allPass)} (불일치 {mismatchCount}건)")
            Return allPass
        End Function

        ' ───────────── 테스트 캔들 생성 ─────────────
        Private Shared Function GenerateTestCandles(count As Integer) As List(Of CandleItem)
            Dim candles As New List(Of CandleItem)
            Dim rng As New Random(42)
            Dim basePrice As Single = 10000

            For i = 0 To count - 1
                Dim change = CSng((rng.NextDouble() - 0.48) * 200)
                basePrice += change
                If basePrice < 1000 Then basePrice = 1000

                Dim high = basePrice + CSng(rng.NextDouble() * 100)
                Dim low = basePrice - CSng(rng.NextDouble() * 100)
                Dim open1 = low + CSng(rng.NextDouble() * (high - low))
                Dim close1 = low + CSng(rng.NextDouble() * (high - low))
                Dim vol = CLng(rng.Next(10000, 500000))

                Dim c As New CandleItem With {
                    .Dt = DateTime.Today.AddSeconds(i * 30),
                    .Open = open1, .High = high, .Low = low, .Close = close1,
                    .Volume = vol, .TickCount = rng.Next(5, 50)}
                candles.Add(c)
            Next
            Return candles
        End Function

    End Class

End Namespace
