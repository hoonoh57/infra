' ═══════════════════════════════════════════════════════════════
' AdaptiveParamCalc.vb — 적응형 파라미터 산출 (원칙서 v4.0 제2조 2-4)
' ═══════════════════════════════════════════════════════════════
' ★ 최근 N일 데이터 기반 파라미터 자동 산출
' ★ TickSum 임계값, RSI 하한, ATR 기반 손절폭
' ★ Fixed/Adaptive 모드 전환
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

#Region "산출 결과 모델"

    ''' <summary>Adaptive 파라미터 산출 결과</summary>
    Public Class AdaptiveResult
        ''' <summary>산출 성공 여부</summary>
        Public Property IsValid As Boolean = False

        ''' <summary>산출 기반 데이터 일수</summary>
        Public Property DataDays As Integer = 0

        ''' <summary>산출 기반 캔들 수</summary>
        Public Property TotalCandles As Integer = 0

        ''' <summary>산출 시각</summary>
        Public Property CalculatedAt As DateTime = DateTime.Now

        ' ── 산출된 파라미터 ──

        ''' <summary>TickSum 임계값 (평균 × 배수)</summary>
        Public Property TickSumThreshold As Double = 5.0

        ''' <summary>RSI 모멘텀 하한 (백분위 기반)</summary>
        Public Property RSI_MomentumLower As Double = 60.0

        ''' <summary>ATR 기반 손절폭 (%)</summary>
        Public Property StopLossRate As Double = -3.0

        ''' <summary>ATR 기반 익절폭 (%)</summary>
        Public Property TakeProfitRate As Double = 5.0

        ''' <summary>ATR 기반 트레일링 스톱 (%)</summary>
        Public Property TrailingStopRate As Double = -1.5

        ' ── 산출 근거 (디버그/로그용) ──

        ''' <summary>평균 TickSum (정규화)</summary>
        Public Property AvgTickSum As Double = 0

        ''' <summary>RSI 25% 백분위 값</summary>
        Public Property RSI_Percentile25 As Double = 0

        ''' <summary>평균 ATR (가격 대비 %)</summary>
        Public Property AvgATRPercent As Double = 0

        ''' <summary>산출 로그 요약</summary>
        Public Function ToSummary() As String
            If Not IsValid Then Return "Adaptive 산출 실패 (데이터 부족)"
            Return $"Adaptive [{DataDays}일/{TotalCandles}봉] " &
                   $"TickSum임계={TickSumThreshold:F1}(평균{AvgTickSum:F1}×배수) " &
                   $"RSI하한={RSI_MomentumLower:F0}(P25={RSI_Percentile25:F0}) " &
                   $"손절={StopLossRate:F1}% 익절={TakeProfitRate:F1}% " &
                   $"트레일링={TrailingStopRate:F1}% (ATR={AvgATRPercent:F2}%)"
        End Function
    End Class

#End Region

    ''' <summary>
    ''' 최근 N일 데이터를 분석하여 전략 파라미터를 적응형으로 산출한다.
    ''' AdaptiveMode = True일 때만 사용. False이면 SimTradeSettings 고정값 사용.
    ''' </summary>
    Public Class AdaptiveParamCalc

        Private ReadOnly _settings As SimTradeSettings

        Public Sub New(settings As SimTradeSettings)
            _settings = settings
        End Sub


        ' ════════════════════════════════════════
        ' 메인 산출 메서드
        ' ════════════════════════════════════════

        ''' <summary>
        ''' 종목의 과거 캔들 데이터로 적응형 파라미터를 산출한다.
        ''' 최소 요구: lookbackDays × 200봉 이상 (30초봉 기준 약 1거래일 ≈ 750봉)
        ''' </summary>
        ''' <param name="candles">과거 캔들 리스트 (시간순 정렬)</param>
        ''' <param name="currentPrice">현재가 (ATR % 환산용)</param>
        Public Function Calculate(candles As List(Of CandleItem),
                                   Optional currentPrice As Integer = 0) As AdaptiveResult

            Dim result As New AdaptiveResult()

            If candles Is Nothing OrElse candles.Count < 200 Then
                result.IsValid = False
                Return result
            End If

            ' 최근 N일 데이터만 사용
            Dim lookbackDays = _settings.Adaptive_LookbackDays   ' 기본 20
            Dim cutoffDate = DateTime.Today.AddDays(-lookbackDays)
            Dim recentCandles = candles.Where(Function(c) c.Dt.Date >= cutoffDate).ToList()

            If recentCandles.Count < 100 Then
                result.IsValid = False
                Return result
            End If

            result.DataDays = recentCandles.Select(Function(c) c.Dt.Date).Distinct().Count()
            result.TotalCandles = recentCandles.Count

            ' ── TickSum 임계값 산출 ──
            result.TickSumThreshold = CalcTickSumThreshold(recentCandles)
            result.AvgTickSum = result.TickSumThreshold / _settings.Adaptive_TickSumMultiplier

            ' ── RSI 모멘텀 하한 산출 ──
            result.RSI_MomentumLower = CalcRSILowerBound(recentCandles)

            ' ── ATR 기반 손절/익절/트레일링 산출 ──
            Dim atrPercent = CalcATRPercent(recentCandles, currentPrice)
            result.AvgATRPercent = atrPercent
            result.StopLossRate = CalcStopLoss(atrPercent)
            result.TakeProfitRate = CalcTakeProfit(atrPercent)
            result.TrailingStopRate = CalcTrailingStop(atrPercent)

            result.IsValid = True
            result.CalculatedAt = DateTime.Now

            Return result
        End Function


        ''' <summary>
        ''' 산출 결과를 SimTradeSettings에 적용한다.
        ''' AdaptiveMode = True일 때만 호출해야 한다.
        ''' </summary>
        Public Sub ApplyToSettings(result As AdaptiveResult)
            If Not result.IsValid Then Return
            If Not _settings.AdaptiveMode Then Return

            _settings.TICKINT_Threshold = result.TickSumThreshold
            _settings.RSI_MomentumLower = result.RSI_MomentumLower
            _settings.StopLossRate = result.StopLossRate
            _settings.TakeProfitRate = result.TakeProfitRate
            _settings.TrailingStopRate = result.TrailingStopRate
        End Sub


        ''' <summary>
        ''' 기준봉(Reference Candle) 데이터를 산출한다.
        ''' 최근 N일에서 거래량 > 20일평균×2, 양봉인 캔들 중 TickSum 최대값.
        ''' </summary>
        Public Function CalcReferenceCandle(candles As List(Of CandleItem)) As ReferenceCandle
            Dim rc As New ReferenceCandle()

            If candles Is Nothing OrElse candles.Count < 100 Then Return rc

            Dim lookbackDays = _settings.RefCandle_LookbackDays  ' 기본 10
            Dim cutoffDate = DateTime.Today.AddDays(-lookbackDays)
            Dim recentCandles = candles.Where(Function(c) c.Dt.Date >= cutoffDate).ToList()
            If recentCandles.Count < 20 Then Return rc

            ' 20일 평균 거래량
            Dim avgVolume = recentCandles.Average(Function(c) CDbl(c.Volume))
            Dim volumeThreshold = CLng(avgVolume * _settings.RefCandle_VolumeMultiple)

            ' 기준봉 후보: 거래량 > 임계값 AND 양봉
            Dim candidates = recentCandles.Where(
                Function(c) c.Volume > volumeThreshold AndAlso c.Close >= c.Open
            ).ToList()

            If candidates.Count = 0 Then Return rc

            ' TickSum 최대값인 캔들 선택
            Dim best = candidates.OrderByDescending(Function(c) c.NormalizedTickSum).First()

            rc.IsValid = True
            rc.High = CInt(best.High)
            rc.TickSum = best.NormalizedTickSum
            rc.Volume = best.Volume
            rc.CandleDate = best.Dt

            Return rc
        End Function


        ' ════════════════════════════════════════
        ' 개별 산출 로직
        ' ════════════════════════════════════════

        ''' <summary>
        ''' TickSum 임계값 = 최근 N일 평균 NormalizedTickSum × 배수(기본 1.2)
        ''' 평균 이상의 참여도가 있을 때만 진입.
        ''' </summary>
        Private Function CalcTickSumThreshold(candles As List(Of CandleItem)) As Double
            Dim tickSums = candles.Where(
                Function(c) c.NormalizedTickSum > 0
            ).Select(Function(c) c.NormalizedTickSum).ToList()

            If tickSums.Count = 0 Then Return _settings.TICKINT_Threshold  ' 기본값 폴백

            Dim avg = tickSums.Average()
            Dim threshold = avg * _settings.Adaptive_TickSumMultiplier  ' 기본 ×1.2

            ' 최솟값 보장: 고정 기본값의 50% 이상
            Dim minThreshold = _settings.TICKINT_Threshold * 0.5
            If threshold < minThreshold Then threshold = minThreshold

            Return threshold
        End Function


        ''' <summary>
        ''' RSI 모멘텀 하한 = 최근 N일 RSI 값의 지정 백분위 (기본 25%).
        ''' 상승 종목에서 RSI가 이 값 이상이어야 충분한 모멘텀으로 판단.
        ''' 
        ''' ※ RSI 값은 IndicatorEngine에서 캔들별로 산출되지만,
        '''   여기서는 캔들 자체에 RSI가 저장되지 않으므로
        '''   간이 RSI를 직접 계산한다.
        ''' </summary>
        Private Function CalcRSILowerBound(candles As List(Of CandleItem)) As Double
            Dim rsiValues = CalcRSISeries(candles, _settings.RSI_Period)
            If rsiValues.Count < 20 Then Return _settings.RSI_MomentumLower

            ' 백분위 계산
            rsiValues.Sort()
            Dim percentileIndex = CInt(Math.Floor(rsiValues.Count * _settings.Adaptive_RSI_Percentile / 100.0))
            percentileIndex = Math.Max(0, Math.Min(percentileIndex, rsiValues.Count - 1))

            Dim pValue = rsiValues(percentileIndex)

            ' 범위 제한: 40 ~ 70
            If pValue < 40 Then pValue = 40
            If pValue > 70 Then pValue = 70

            Return pValue
        End Function


        ''' <summary>
        ''' ATR(Average True Range) % = 최근 N일 ATR의 평균을 현재가 대비 %로 환산.
        ''' </summary>
        Private Function CalcATRPercent(candles As List(Of CandleItem),
                                         currentPrice As Integer) As Double
            If candles.Count < 15 OrElse currentPrice <= 0 Then Return 1.5  ' 기본 1.5%

            Dim atrValues As New List(Of Double)
            For i = 1 To candles.Count - 1
                Dim c = candles(i)
                Dim prev = candles(i - 1)
                Dim tr = Math.Max(
                    CDbl(c.High - c.Low),
                    Math.Max(
                        Math.Abs(CDbl(c.High) - prev.Close),
                        Math.Abs(CDbl(c.Low) - prev.Close)
                    ))
                atrValues.Add(tr)
            Next

            If atrValues.Count < 14 Then Return 1.5

            ' 최근 14개 ATR 평균
            Dim recentATR = atrValues.Skip(atrValues.Count - 14).Average()
            Dim atrPercent = (recentATR / currentPrice) * 100.0

            Return atrPercent
        End Function


        ''' <summary>ATR 기반 손절폭: ATR% × 2 (최소 -1.5%, 최대 -5%)</summary>
        Private Function CalcStopLoss(atrPercent As Double) As Double
            Dim sl = -atrPercent * 2.0
            If sl > -1.5 Then sl = -1.5
            If sl < -5.0 Then sl = -5.0
            Return sl
        End Function

        ''' <summary>ATR 기반 익절폭: |손절| × MinRiskReward (최소 3%, 최대 10%)</summary>
        Private Function CalcTakeProfit(atrPercent As Double) As Double
            Dim sl = Math.Abs(CalcStopLoss(atrPercent))
            Dim tp = sl * _settings.MinRiskReward   ' 기본 1.2배
            If tp < 3.0 Then tp = 3.0
            If tp > 10.0 Then tp = 10.0
            Return tp
        End Function

        ''' <summary>ATR 기반 트레일링 스톱: ATR% × 1.0 (최소 -0.5%, 최대 -3%)</summary>
        Private Function CalcTrailingStop(atrPercent As Double) As Double
            Dim ts = -atrPercent * 1.0
            If ts > -0.5 Then ts = -0.5
            If ts < -3.0 Then ts = -3.0
            Return ts
        End Function


        ' ════════════════════════════════════════
        ' 간이 RSI 계산 (내부용)
        ' ════════════════════════════════════════

        ''' <summary>캔들 리스트에서 RSI 시리즈를 계산한다.</summary>
        Private Function CalcRSISeries(candles As List(Of CandleItem), period As Integer) As List(Of Double)
            Dim result As New List(Of Double)
            If candles.Count <= period Then Return result

            Dim gains As New List(Of Double)
            Dim losses As New List(Of Double)

            For i = 1 To candles.Count - 1
                Dim change = CDbl(candles(i).Close) - candles(i - 1).Close
                gains.Add(If(change > 0, change, 0))
                losses.Add(If(change < 0, Math.Abs(change), 0))
            Next

            If gains.Count < period Then Return result

            ' 초기 평균
            Dim avgGain = gains.Take(period).Average()
            Dim avgLoss = losses.Take(period).Average()

            For i = period To gains.Count - 1
                If i > period Then
                    avgGain = (avgGain * (period - 1) + gains(i)) / period
                    avgLoss = (avgLoss * (period - 1) + losses(i)) / period
                End If

                If avgLoss = 0 Then
                    result.Add(100.0)
                Else
                    Dim rs = avgGain / avgLoss
                    result.Add(100.0 - (100.0 / (1.0 + rs)))
                End If
            Next

            Return result
        End Function

    End Class


#Region "기준봉 결과 모델"

    ''' <summary>기준봉 산출 결과</summary>
    Public Class ReferenceCandle
        Public Property IsValid As Boolean = False
        Public Property High As Integer = 0
        Public Property TickSum As Double = 0
        Public Property Volume As Long = 0
        Public Property CandleDate As DateTime = DateTime.MinValue
    End Class

#End Region

End Namespace
