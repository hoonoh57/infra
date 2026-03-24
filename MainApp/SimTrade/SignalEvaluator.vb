' ═══════════════════════════════════════════════════════════════
' SignalEvaluator.vb — 매수/매도 신호 판단 엔진 (원칙서 v4.0)
' ═══════════════════════════════════════════════════════════════
' ★ 매수: 7조건 동시 AND (제3조)
' ★ 매도: P0~P8 우선순위 청산 (제4조, ST 보호 규칙 포함)
' ★ SimTradeForm.EvaluateBuy/EvaluateSell을 대체
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

#Region "신호 결과 모델"

    ''' <summary>매수 신호 판단 결과</summary>
    Public Class BuySignalResult
        Public Property ShouldBuy As Boolean = False
        Public Property Reason As String = ""
        Public Property RejectReasons As New List(Of String)
        Public Property ConditionsMet As Integer = 0
        Public Property Profile As String = "B"           ' A 또는 B
        Public Property SuggestedPrice As Integer = 0
        Public Property SuggestedQty As Integer = 0

        ''' <summary>7조건 개별 충족 상태 (디버그/로그용)</summary>
        Public Property C1_ST As Boolean = False
        Public Property C2_JMA As Boolean = False
        Public Property C3_TickSum As Boolean = False
        Public Property C4_OBV As Boolean = False
        Public Property C5_Confirm As Boolean = False
        Public Property C6_MACD As Boolean = False
        Public Property C7_Volume As Boolean = False
    End Class

    ''' <summary>매도 신호 판단 결과</summary>
    Public Class SellSignalResult
        Public Property ShouldSell As Boolean = False
        Public Property Reason As String = ""
        Public Property Priority As String = ""           ' P0~P8
        Public Property IsPartialSell As Boolean = False  ' 향후 분할매도용
        Public Property SellRatio As Double = 1.0         ' 1.0 = 전량
    End Class

#End Region

    ''' <summary>
    ''' 매수/매도 신호를 판단하는 핵심 엔진.
    ''' StateManager의 StockState를 읽어 판단하고, 결과 객체를 반환한다.
    ''' 직접 주문하지 않는다 (판단만 담당).
    ''' </summary>
    Public Class SignalEvaluator

        Private ReadOnly _settings As SimTradeSettings

        Public Sub New(settings As SimTradeSettings)
            _settings = settings
        End Sub


        ' ════════════════════════════════════════
        ' 매수 판단 — 7조건 동시 AND (제3조)
        ' ════════════════════════════════════════

        ''' <summary>
        ''' 7조건 동시 충족 여부를 판단한다.
        ''' 1) ST Direction = +1 (상승)
        ''' 2) JMA 상승 전환 (ConfirmBars_JMA 봉 이내)
        ''' 3) TickSum 정규화 > 임계값 AND TickSum > MA5
        ''' 4) OBV Direction = +1
        ''' 5) 최근 ConfirmBars 봉 이내 1~4 동시 충족
        ''' 6) MACD 골든크로스 (ConfirmBars_MACD 봉 이내, 3값 > 0)
        ''' 7) Volume > VOL_MA20
        ''' </summary>
        Public Function EvaluateBuy(state As StockState, holdingCount As Integer,
                                     availableCash As Long, totalEquity As Long) As BuySignalResult

            Dim result As New BuySignalResult()
            Dim now = DateTime.Now.TimeOfDay

            ' ── 시간 차단 ──
            If now < _settings.TradingStartTime Then
                result.Reason = "시간전" : Return result
            End If
            If now >= _settings.NoNewBuyAfter Then
                result.Reason = "매수금지시간" : Return result
            End If

            ' ── 포지션 차단 ──
            If holdingCount >= _settings.MaxPositionCount Then
                result.Reason = "최대종목초과" : Return result
            End If

            ' ── 이미 보유 ──
            If state.HasPosition Then
                result.Reason = "보유중" : Return result
            End If

            ' ── 쿨다운 ──
            If state.LastBuyTime <> DateTime.MinValue Then
                Dim elapsed = (DateTime.Now - state.LastBuyTime).TotalSeconds
                If elapsed < _settings.CooldownSec Then
                    result.Reason = $"쿨다운({CInt(_settings.CooldownSec - elapsed)}초)"
                    Return result
                End If
            End If

            ' ── 캔들 수 부족 ──
            If state.Candles.Count < _settings.MinCandlesForSignal Then
                result.Reason = $"캔들수집중({state.Candles.Count}/{_settings.MinCandlesForSignal})"
                Return result
            End If

            ' ── 제외 상태 ──
            If state.State = DataState.Excluded Then
                result.Reason = $"제외({state.ExclusionReason})" : Return result
            End If

            ' ════════════════════════════════════
            ' 7조건 개별 판단
            ' ════════════════════════════════════

            Dim idx = state.Candles.Count - 1

            ' ── C1: ST Direction = +1 ──
            result.C1_ST = (state.ST_Direction > 0)
            If Not result.C1_ST Then result.RejectReasons.Add($"ST하락(D={state.ST_Direction:F0})")

            ' ── C2: JMA 상승 전환 (ConfirmBars_JMA 봉 이내) ──
            Dim jmaConfirmBars = GetJMAConfirmBars(now)
            result.C2_JMA = (state.JMA_Direction > 0) AndAlso
                            (state.JMA_TurnBar >= 0 AndAlso state.JMA_TurnBar <= jmaConfirmBars)
            If Not result.C2_JMA Then
                If state.JMA_Direction <= 0 Then
                    result.RejectReasons.Add($"JMA하락(D={state.JMA_Direction:F0})")
                ElseIf state.JMA_TurnBar < 0 Then
                    result.RejectReasons.Add("JMA전환없음")
                Else
                    result.RejectReasons.Add($"JMA전환경과({state.JMA_TurnBar}봉>{jmaConfirmBars})")
                End If
            End If

            ' ── C3: TickSum 정규화 > 임계값 AND > MA5 ──
            Dim tickThreshold = GetTickSumThreshold(state)
            Dim tickOk = (Not Double.IsNaN(state.TickSum_Normalized)) AndAlso
                         (state.TickSum_Normalized >= tickThreshold) AndAlso
                         (Not Double.IsNaN(state.TickMA5_Normalized)) AndAlso
                         (state.TickSum_Normalized > state.TickMA5_Normalized)
            result.C3_TickSum = tickOk
            If Not result.C3_TickSum Then
                result.RejectReasons.Add($"TickSum부족({state.TickSum_Normalized:F1}<{tickThreshold:F1})")
            End If

            ' ── C4: OBV Direction = +1 ──
            result.C4_OBV = (state.OBV_Direction > 0)
            If Not result.C4_OBV Then result.RejectReasons.Add($"OBV하락(D={state.OBV_Direction:F0})")

            ' ── C5: 최근 ConfirmBars 봉 이내 1~4 동시 충족 ──
            '   (현재 봉 기준으로 1~4가 모두 True이면 C5도 True)
            result.C5_Confirm = result.C1_ST AndAlso result.C2_JMA AndAlso
                                result.C3_TickSum AndAlso result.C4_OBV
            If Not result.C5_Confirm Then result.RejectReasons.Add("동시충족실패")

            ' ── C6: MACD 골든크로스 (ConfirmBars_MACD 봉 이내) ──
            result.C6_MACD = EvaluateMACDGoldenCross(state)
            If Not result.C6_MACD Then result.RejectReasons.Add("MACD미충족")

            ' ── C7: Volume > VOL_MA20 ──
            result.C7_Volume = False
            If _settings.VOL_RequireAboveMA Then
                If Not Double.IsNaN(state.Volume_Ratio) Then
                    result.C7_Volume = (state.Volume_Ratio >= 100.0)   ' Ratio 100% = 평균 이상
                End If
            Else
                result.C7_Volume = True  ' 설정 OFF이면 무조건 통과
            End If
            If Not result.C7_Volume Then result.RejectReasons.Add($"거래량부족({state.Volume_Ratio:F0}%)")

            ' ── 보조 필터: RSI 모멘텀 하한 ──
            Dim rsiOk = True
            If Not Double.IsNaN(state.RSI_Value) Then
                If state.RSI_Value < _settings.RSI_MomentumLower Then
                    rsiOk = False
                    result.RejectReasons.Add($"RSI부족({state.RSI_Value:F0}<{_settings.RSI_MomentumLower:F0})")
                End If
                If state.RSI_Value > _settings.RSI_OverboughtLimit Then
                    rsiOk = False
                    result.RejectReasons.Add($"RSI과매수({state.RSI_Value:F0})")
                End If
            End If

            ' ── 스프레드 체크 ──
            Dim spreadOk = True
            If state.Ask1 > 0 AndAlso state.Bid1 > 0 Then
                Dim spreadRate = (state.Ask1 - state.Bid1) / CDbl(state.Bid1) * 100.0
                If spreadRate > _settings.MaxSpreadRate Then
                    spreadOk = False
                    result.RejectReasons.Add($"스프레드초과({spreadRate:F2}%>{_settings.MaxSpreadRate:F1}%)")
                End If
            End If

            ' ── 손익비 사전검증 ──
            Dim riskRewardOk = True
            Dim totalCost = _settings.BuyCommissionRate + _settings.SellCommissionRate +
                            _settings.TransactionTaxRate + _settings.EstimatedSlippage
            Dim netProfit = _settings.TakeProfitRate - (totalCost / 100.0 * 100.0)
            Dim netLoss = Math.Abs(_settings.StopLossRate) + (totalCost / 100.0 * 100.0)
            If netLoss > 0 Then
                Dim rr = netProfit / netLoss
                If rr < _settings.MinRiskReward Then
                    riskRewardOk = False
                    result.RejectReasons.Add($"손익비미달({rr:F2}<{_settings.MinRiskReward:F1})")
                End If
            End If

            ' ════════════════════════════════════
            ' 최종 판단: 7조건 + 보조필터 모두 충족
            ' ════════════════════════════════════

            result.ConditionsMet = 0
            If result.C1_ST Then result.ConditionsMet += 1
            If result.C2_JMA Then result.ConditionsMet += 1
            If result.C3_TickSum Then result.ConditionsMet += 1
            If result.C4_OBV Then result.ConditionsMet += 1
            If result.C5_Confirm Then result.ConditionsMet += 1
            If result.C6_MACD Then result.ConditionsMet += 1
            If result.C7_Volume Then result.ConditionsMet += 1

            Dim allConditions = result.C1_ST AndAlso result.C2_JMA AndAlso result.C3_TickSum AndAlso
                                result.C4_OBV AndAlso result.C5_Confirm AndAlso result.C6_MACD AndAlso
                                result.C7_Volume AndAlso rsiOk AndAlso spreadOk AndAlso riskRewardOk

            If allConditions Then
                ' ── 매수 수량/가격 계산 ──
                Dim maxAmt = CLng(totalEquity * _settings.PositionSizeRate)
                If maxAmt > availableCash Then maxAmt = availableCash
                Dim price = GetBuyPrice(state)
                If price <= 0 Then
                    result.Reason = "가격오류" : Return result
                End If
                Dim qty = CInt(maxAmt \ price)
                If qty <= 0 Then
                    result.Reason = "매수금액부족" : Return result
                End If

                result.ShouldBuy = True
                result.SuggestedPrice = price
                result.SuggestedQty = qty
                result.Profile = DetermineProfile(now, state)
                result.Reason = $"★매수({result.ConditionsMet}/7) [{result.Profile}]"
            Else
                result.Reason = $"조건미충족({result.ConditionsMet}/7): {String.Join(", ", result.RejectReasons.Take(3))}"
            End If

            Return result
        End Function


        ' ════════════════════════════════════════
        ' 매도 판단 — P0~P8 우선순위 (제4조)
        ' ════════════════════════════════════════

        ''' <summary>
        ''' 청산 우선순위:
        ''' P0 — Grace Period 조기 청산 (매수 직후 보호 구간)
        ''' P1 — ST 적색 전환 → 전량 청산
        ''' P2 — VI 근접 청산 (수익 ≥5% AND 가격 ≥ 상한가×0.9)
        ''' P3 — 목표수익 + JMA↓ + (ST=-1 OR TickSum약)
        ''' P4 — JMA↓ + TickSum약 (ST=-1일 때만)
        ''' P5 — OBV↓ (ST=-1일 때만)
        ''' P6 — 손절 (-3%)
        ''' P7 — 트레일링 스톱 (고점 대비 -1.5% / 강화 -0.8%)
        ''' P8 — 강제 청산 (15:15)
        ''' </summary>
        Public Function EvaluateSell(state As StockState) As SellSignalResult

            Dim result As New SellSignalResult()
            If Not state.HasPosition OrElse state.BuyPrice <= 0 Then Return result

            Dim profitRate = state.CurrentPnLRate
            Dim now = DateTime.Now.TimeOfDay
            Dim barsSinceBuy = GetBarsSinceBuy(state)
            Dim stUp = (state.ST_Direction > 0)
            Dim jmaDown = (state.JMA_Direction < 0)
            Dim tickWeak = (Not Double.IsNaN(state.TickSum_Normalized)) AndAlso
                           (Not Double.IsNaN(state.TickMA5_Normalized)) AndAlso
                           (state.TickSum_Normalized < state.TickMA5_Normalized)
            Dim obvDown = (state.OBV_Direction < 0)

            ' ── P0: Grace Period (매수 후 첫 N봉) ──
            If barsSinceBuy <= _settings.GracePeriod_Bars AndAlso barsSinceBuy >= 0 Then
                Dim deteriorations = 0
                If jmaDown Then deteriorations += 1
                If tickWeak Then deteriorations += 1
                If obvDown Then deteriorations += 1
                If deteriorations >= _settings.GracePeriod_ExitConditions Then
                    result.ShouldSell = True
                    result.Priority = "P0"
                    result.Reason = $"GracePeriod_악화({deteriorations}개,{barsSinceBuy}봉)"
                    Return result
                End If
                If profitRate < -_settings.MinProfitInGrace AndAlso barsSinceBuy >= _settings.GracePeriod_Bars Then
                    result.ShouldSell = True
                    result.Priority = "P0"
                    result.Reason = $"GracePeriod_수익미달({profitRate:F1}%)"
                    Return result
                End If
            End If

            ' ── P1: ST 적색 전환 → 전량 청산 ──
            If Not stUp Then
                result.ShouldSell = True
                result.Priority = "P1"
                result.Reason = $"ST적색전환(수익{profitRate:F1}%)"
                Return result
            End If

            ' ── P2: VI 근접 청산 ──
            If state.UpperLimitPrice > 0 AndAlso profitRate >= _settings.TakeProfitRate Then
                Dim viThreshold = CInt(state.UpperLimitPrice * state.VI_NearRate)
                If state.CurrentPrice >= viThreshold Then
                    result.ShouldSell = True
                    result.Priority = "P2"
                    result.Reason = $"VI근접({state.CurrentPrice:N0}≥{viThreshold:N0},수익{profitRate:F1}%)"
                    Return result
                End If
            End If

            ' ── P3: 목표수익 + JMA↓ + (ST=-1 OR TickSum약) — ST+1이면 억제 ──
            If profitRate >= _settings.TakeProfitRate AndAlso jmaDown Then
                If Not stUp OrElse tickWeak Then
                    result.ShouldSell = True
                    result.Priority = "P3"
                    result.Reason = $"목표+JMA↓(수익{profitRate:F1}%)"
                    Return result
                End If
                ' ST+1이면 트레일링으로 전환 (매도하지 않음)
            End If

            ' ── P4: JMA↓ + TickSum약 — ST=-1일 때만 ──
            If Not stUp AndAlso jmaDown AndAlso tickWeak Then
                result.ShouldSell = True
                result.Priority = "P4"
                result.Reason = $"JMA↓+TickSum약(수익{profitRate:F1}%)"
                Return result
            End If

            ' ── P5: OBV↓ — ST=-1일 때만 ──
            If Not stUp AndAlso obvDown Then
                result.ShouldSell = True
                result.Priority = "P5"
                result.Reason = $"OBV↓(수익{profitRate:F1}%)"
                Return result
            End If

            ' ── P6: 손절 ──
            If profitRate <= _settings.StopLossRate Then
                result.ShouldSell = True
                result.Priority = "P6"
                result.Reason = $"손절({profitRate:F1}%≤{_settings.StopLossRate:F1}%)"
                Return result
            End If

            ' ── P7: 트레일링 스톱 ──
            If _settings.EnableTrailingStop AndAlso state.HighSinceBuy > 0 AndAlso profitRate > 0 Then
                Dim drawdown = (CDbl(state.CurrentPrice - state.HighSinceBuy) / state.HighSinceBuy) * 100.0
                ' 강화 트레일링: N봉 고점 미갱신 시
                Dim trailingRate = _settings.TrailingStopRate
                If barsSinceBuy > _settings.MaxHoldWithoutNewHigh Then
                    trailingRate = _settings.TightenedTrailingRate
                End If
                If drawdown <= trailingRate Then
                    result.ShouldSell = True
                    result.Priority = "P7"
                    result.Reason = $"트레일링({drawdown:F1}%,수익{profitRate:F1}%,기준{trailingRate:F1}%)"
                    Return result
                End If
            End If

            ' ── P8: 강제 청산 (15:15) ──
            If now >= _settings.ForceCloseTime Then
                result.ShouldSell = True
                result.Priority = "P8"
                result.Reason = $"장마감강제청산(수익{profitRate:F1}%)"
                Return result
            End If

            ' ── 청산 조건 없음 ──
            result.Reason = $"보유유지(수익{profitRate:F1}%,ST+{If(stUp, "1", "0")})"
            Return result
        End Function


        ' ════════════════════════════════════════
        ' 내부 헬퍼
        ' ════════════════════════════════════════

        ''' <summary>시간대별 JMA ConfirmBars (장초반 확대)</summary>
        Private Function GetJMAConfirmBars(tod As TimeSpan) As Integer
            If tod < _settings.Phase_Open_End Then
                Return _settings.EarlyPhase_ConfirmBars_JMA  ' 기본 5
            End If
            Return _settings.ConfirmBars_JMA                 ' 기본 2
        End Function

        ''' <summary>TickSum 임계값 결정 (프로파일/Adaptive 반영)</summary>
        Private Function GetTickSumThreshold(state As StockState) As Double
            ' 프로파일 A: 기준봉 TickSum 기반
            If state.HasReferenceCandle AndAlso
               (_settings.ActiveProfileMode = ProfileMode.OnlyA OrElse
                (_settings.ActiveProfileMode = ProfileMode.Auto AndAlso
                 DateTime.Now.TimeOfDay < _settings.ProfileA_EndTime)) Then
                Return state.ReferenceCandleTickSum * _settings.TICKINT_RatioMin
            End If

            ' 프로파일 B: 고정값 또는 DayMax 비율
            If _settings.ProfileB_TickMode = TickThresholdMode.DayMax AndAlso state.DayMaxTickSum > 0 Then
                Return state.DayMaxTickSum * _settings.ProfileB_DayMaxRatio
            End If

            Return _settings.TICKINT_Threshold   ' 기본 5.0
        End Function

        ''' <summary>MACD 골든크로스 판단 (참고지표 — 완화 적용)</summary>
        Private Function EvaluateMACDGoldenCross(state As StockState) As Boolean
            Dim results = state.Engine.Results
            Dim macdList = FindResult(results, "MACD_")
            If macdList Is Nothing Then Return False

            Dim idx = macdList.Count - 1
            If idx < 1 Then Return False

            ' 현재봉 기준: Histogram > 0 (MACD > Signal) 이면 충족
            Dim histVal = macdList(idx).Val("Histogram")
            Dim macdVal = macdList(idx).Val("MACD")
            Dim signalVal = macdList(idx).Val("Signal")

            If Single.IsNaN(histVal) OrElse Single.IsNaN(macdVal) Then Return False

            ' 기본 조건: MACD가 Signal 위에 있으면 통과 (Histogram > 0)
            If histVal > 0 Then Return True

            ' ConfirmBars 이내에 골든크로스(Histogram 양전환)가 있었으면 통과
            Dim lookback = Math.Min(_settings.ConfirmBars_MACD, idx)
            For i = idx To Math.Max(0, idx - lookback) Step -1
                Dim h = macdList(i).Val("Histogram")
                If i > 0 Then
                    Dim prevH = macdList(i - 1).Val("Histogram")
                    If Not Single.IsNaN(h) AndAlso Not Single.IsNaN(prevH) Then
                        If prevH <= 0 AndAlso h > 0 Then Return True
                    End If
                End If
            Next

            Return False
        End Function

        ''' <summary>매수 직후 경과 봉 수 계산</summary>
        Private Function GetBarsSinceBuy(state As StockState) As Integer
            If state.BuyTime = DateTime.MinValue Then Return -1
            If state.Candles.Count = 0 Then Return -1

            Dim count = 0
            For i = state.Candles.Count - 1 To 0 Step -1
                If state.Candles(i).Dt <= state.BuyTime Then Exit For
                count += 1
            Next
            Return count
        End Function

        ''' <summary>매수 가격 결정</summary>
        Private Function GetBuyPrice(state As StockState) As Integer
            Select Case _settings.BuyOrderType
                Case SimOrderType.LimitBestBid
                    Return If(state.Ask1 > 0, state.Ask1, state.CurrentPrice)
                Case SimOrderType.LimitCurrentPrice
                    Return state.CurrentPrice
                Case Else
                    Return state.CurrentPrice
            End Select
        End Function

        ''' <summary>프로파일 결정 (A/B/Auto)</summary>
        Private Function DetermineProfile(tod As TimeSpan, state As StockState) As String
            Select Case _settings.ActiveProfileMode
                Case ProfileMode.OnlyA : Return "A"
                Case ProfileMode.OnlyB : Return "B"
                Case Else
                    If tod < _settings.ProfileA_EndTime AndAlso state.HasReferenceCandle Then
                        Return "A"
                    End If
                    Return "B"
            End Select
        End Function

        ''' <summary>지표 결과에서 접두사로 찾기</summary>
        Private Shared Function FindResult(results As Dictionary(Of String, List(Of IndicatorResult)),
                                           prefix As String) As List(Of IndicatorResult)
            If results Is Nothing Then Return Nothing
            Dim key = results.Keys.FirstOrDefault(Function(k) k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            If key Is Nothing Then Return Nothing
            Dim list As List(Of IndicatorResult) = Nothing
            results.TryGetValue(key, list)
            Return list
        End Function

    End Class

End Namespace
