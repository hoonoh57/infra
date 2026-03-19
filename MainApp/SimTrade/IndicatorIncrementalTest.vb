' ═══════════════════════════════════════════════════════════════
' IndicatorIncrementalTest.vb — 증분 계산 정합성 검증 (2-4)
' ═══════════════════════════════════════════════════════════════
' ★ 테스트 A: OBV Calculate vs UpdateLast 결과 비교
' ★ 테스트 B: RSI Calculate vs UpdateLast 결과 비교
' ★ 테스트 C: Volume Calculate vs UpdateLast 결과 비교
' ★ 모두 통과하면 "PASS", 불일치 시 상세 로그 출력
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

    ''' <summary>
    ''' OBV, RSI, Volume 지표의 증분 계산(UpdateLast) 결과가
    ''' 전체 계산(Calculate) 결과와 동일한지 검증한다.
    ''' SimTradeForm 로그 또는 콘솔에서 호출 가능.
    ''' </summary>
    Public Class IndicatorIncrementalTest

        ''' <summary>전체 테스트 실행. 결과 문자열 반환.</summary>
        Public Shared Function RunAll() As String
            Dim sb As New System.Text.StringBuilder()
            sb.AppendLine("═══ 증분 계산 검증 테스트 시작 ═══")

            ' 테스트용 캔들 생성 (100봉)
            Dim candles = GenerateTestCandles(100)

            Dim passA = TestOBV(candles, sb)
            Dim passB = TestRSI(candles, sb)
            Dim passC = TestVolume(candles, sb)

            sb.AppendLine()
            If passA AndAlso passB AndAlso passC Then
                sb.AppendLine("═══ 결과: ALL PASS ═══")
            Else
                sb.AppendLine($"═══ 결과: FAIL (OBV={If(passA, "OK", "FAIL")}, RSI={If(passB, "OK", "FAIL")}, VOL={If(passC, "OK", "FAIL")}) ═══")
            End If

            Return sb.ToString()
        End Function


        ''' <summary>테스트 A: OBV 증분 검증</summary>
        Private Shared Function TestOBV(candles As List(Of CandleItem), sb As System.Text.StringBuilder) As Boolean
            sb.AppendLine()
            sb.AppendLine("── 테스트 A: OBV ──")

            Dim indicator As New OBV_Indicator(20)
            Dim allPass = True
            Dim tolerance = 0.01F

            ' 방법 1: 전체 Calculate
            Dim fullResults = indicator.Calculate(candles)

            ' 방법 2: 캔들을 하나씩 추가하며 UpdateLast
            Dim indicator2 As New OBV_Indicator(20)
            Dim incrementalCandles As New List(Of CandleItem)
            Dim prevResults As List(Of IndicatorResult) = Nothing

            For i = 0 To candles.Count - 1
                incrementalCandles.Add(candles(i))

                If i = 0 Then
                    ' 첫 봉은 Calculate로 초기화
                    prevResults = indicator2.Calculate(incrementalCandles)
                Else
                    Dim lastResult = indicator2.UpdateLast(incrementalCandles, prevResults)
                    If prevResults.Count > incrementalCandles.Count - 1 Then
                        prevResults(prevResults.Count - 1) = lastResult
                    Else
                        prevResults.Add(lastResult)
                    End If
                End If

                ' 같은 봉 재호출 테스트 (틱 업데이트 시뮬레이션)
                If i > 0 AndAlso i Mod 10 = 0 Then
                    Dim reResult = indicator2.UpdateLast(incrementalCandles, prevResults)
                    prevResults(prevResults.Count - 1) = reResult
                End If
            Next

            ' 비교
            For i = 0 To candles.Count - 1
                If i >= fullResults.Count OrElse i >= prevResults.Count Then
                    sb.AppendLine($"  [FAIL] 인덱스 {i}: 결과 크기 불일치")
                    allPass = False
                    Continue For
                End If

                Dim fObv = fullResults(i).Val("OBV")
                Dim iObv = prevResults(i).Val("OBV")
                Dim fDir = fullResults(i).Val("Direction")
                Dim iDir = prevResults(i).Val("Direction")
                Dim fSig = fullResults(i).Val("Signal")
                Dim iSig = prevResults(i).Val("Signal")

                If Math.Abs(fObv - iObv) > tolerance Then
                    sb.AppendLine($"  [FAIL] 봉{i}: OBV Full={fObv:F2} vs Inc={iObv:F2}")
                    allPass = False
                End If
                If Not Single.IsNaN(fDir) AndAlso Not Single.IsNaN(iDir) AndAlso fDir <> iDir Then
                    sb.AppendLine($"  [FAIL] 봉{i}: Direction Full={fDir} vs Inc={iDir}")
                    allPass = False
                End If
            Next

            sb.AppendLine($"  OBV: {If(allPass, "PASS", "FAIL")}")
            Return allPass
        End Function


        ''' <summary>테스트 B: RSI 증분 검증</summary>
        Private Shared Function TestRSI(candles As List(Of CandleItem), sb As System.Text.StringBuilder) As Boolean
            sb.AppendLine()
            sb.AppendLine("── 테스트 B: RSI ──")

            Dim indicator As New RSI_Indicator(14)
            Dim allPass = True
            Dim tolerance = 0.1F   ' RSI는 누적 오차 허용

            ' 전체 Calculate
            Dim fullResults = indicator.Calculate(candles)

            ' 증분 UpdateLast
            Dim indicator2 As New RSI_Indicator(14)
            Dim incrementalCandles As New List(Of CandleItem)
            Dim prevResults As List(Of IndicatorResult) = Nothing

            For i = 0 To candles.Count - 1
                incrementalCandles.Add(candles(i))

                If i <= 14 Then
                    prevResults = indicator2.Calculate(incrementalCandles)
                Else
                    Dim lastResult = indicator2.UpdateLast(incrementalCandles, prevResults)
                    If prevResults.Count > incrementalCandles.Count - 1 Then
                        prevResults(prevResults.Count - 1) = lastResult
                    Else
                        prevResults.Add(lastResult)
                    End If
                End If

                ' 재호출 테스트
                If i > 14 AndAlso i Mod 7 = 0 Then
                    Dim reResult = indicator2.UpdateLast(incrementalCandles, prevResults)
                    prevResults(prevResults.Count - 1) = reResult
                End If
            Next

            ' 비교
            For i = 0 To candles.Count - 1
                If i >= fullResults.Count OrElse i >= prevResults.Count Then Continue For

                Dim fVal = fullResults(i).Val("Value")
                Dim iVal = prevResults(i).Val("Value")

                If Single.IsNaN(fVal) AndAlso Single.IsNaN(iVal) Then Continue For
                If Single.IsNaN(fVal) <> Single.IsNaN(iVal) Then
                    sb.AppendLine($"  [FAIL] 봉{i}: RSI NaN 불일치 Full={fVal} vs Inc={iVal}")
                    allPass = False
                    Continue For
                End If
                If Math.Abs(fVal - iVal) > tolerance Then
                    sb.AppendLine($"  [FAIL] 봉{i}: RSI Full={fVal:F2} vs Inc={iVal:F2} (차이={Math.Abs(fVal - iVal):F4})")
                    allPass = False
                End If
            Next

            sb.AppendLine($"  RSI: {If(allPass, "PASS", "FAIL")}")
            Return allPass
        End Function


        ''' <summary>테스트 C: Volume 증분 검증</summary>
        Private Shared Function TestVolume(candles As List(Of CandleItem), sb As System.Text.StringBuilder) As Boolean
            sb.AppendLine()
            sb.AppendLine("── 테스트 C: Volume ──")

            Dim indicator As New Volume_Indicator(20)
            Dim allPass = True
            Dim tolerance = 0.01F

            ' 전체 Calculate
            Dim fullResults = indicator.Calculate(candles)

            ' 증분 UpdateLast
            Dim indicator2 As New Volume_Indicator(20)
            Dim incrementalCandles As New List(Of CandleItem)
            Dim prevResults As List(Of IndicatorResult) = Nothing

            For i = 0 To candles.Count - 1
                incrementalCandles.Add(candles(i))

                If i = 0 Then
                    prevResults = indicator2.Calculate(incrementalCandles)
                Else
                    Dim lastResult = indicator2.UpdateLast(incrementalCandles, prevResults)
                    If prevResults.Count > incrementalCandles.Count - 1 Then
                        prevResults(prevResults.Count - 1) = lastResult
                    Else
                        prevResults.Add(lastResult)
                    End If
                End If

                ' 재호출 테스트 (같은 봉에서 볼륨 변경)
                If i > 0 AndAlso i Mod 5 = 0 Then
                    candles(i).Volume += 100  ' 틱 추가 시뮬레이션
                    Dim reResult = indicator2.UpdateLast(incrementalCandles, prevResults)
                    prevResults(prevResults.Count - 1) = reResult
                    candles(i).Volume -= 100  ' 원복
                End If
            Next

            ' 최종 전체 Calculate로 기준값 재생성 (볼륨 원복 후)
            fullResults = indicator.Calculate(candles)

            ' 비교
            For i = 0 To candles.Count - 1
                If i >= fullResults.Count OrElse i >= prevResults.Count Then Continue For

                Dim fRatio = fullResults(i).Val("Ratio")
                Dim iRatio = prevResults(i).Val("Ratio")
                Dim fMA = fullResults(i).Val("MA")
                Dim iMA = prevResults(i).Val("MA")

                If Single.IsNaN(fMA) AndAlso Single.IsNaN(iMA) Then Continue For
                If Single.IsNaN(fMA) <> Single.IsNaN(iMA) Then
                    sb.AppendLine($"  [FAIL] 봉{i}: MA NaN 불일치")
                    allPass = False
                    Continue For
                End If
                If Not Single.IsNaN(fMA) AndAlso Math.Abs(fMA - iMA) > tolerance Then
                    sb.AppendLine($"  [FAIL] 봉{i}: MA Full={fMA:F2} vs Inc={iMA:F2}")
                    allPass = False
                End If
            Next

            sb.AppendLine($"  Volume: {If(allPass, "PASS", "FAIL")}")
            Return allPass
        End Function


        ''' <summary>테스트용 랜덤 캔들 생성</summary>
        Private Shared Function GenerateTestCandles(count As Integer) As List(Of CandleItem)
            Dim candles As New List(Of CandleItem)
            Dim rng As New Random(42)  ' 고정 시드 (재현성)
            Dim basePrice As Single = 10000

            For i = 0 To count - 1
                Dim change = CSng((rng.NextDouble() - 0.48) * 200)  ' 약간 상승 편향
                basePrice += change
                If basePrice < 1000 Then basePrice = 1000

                Dim high = basePrice + CSng(rng.NextDouble() * 100)
                Dim low = basePrice - CSng(rng.NextDouble() * 100)
                Dim open = low + CSng(rng.NextDouble() * (high - low))
                Dim close = low + CSng(rng.NextDouble() * (high - low))
                Dim vol = CLng(rng.Next(10000, 500000))

                Dim c As New CandleItem With {
                    .Dt = DateTime.Today.AddSeconds(i * 30),
                    .Open = open, .High = high, .Low = low, .Close = close,
                    .Volume = vol, .TickCount = rng.Next(5, 50)}
                candles.Add(c)
            Next

            Return candles
        End Function

    End Class

End Namespace
