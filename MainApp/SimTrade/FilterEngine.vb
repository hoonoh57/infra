' ═══════════════════════════════════════════════════════════════
' FilterEngine.vb — 위험 필터 엔진 (원칙서 v4.0 제8조)
' ═══════════════════════════════════════════════════════════════
' ★ 6종 위험 필터: 갭상승, 페이크돌파, VI근접, 스프레드, 거래량, 시간
' ★ Off / Observe / Block 3단계 모드
' ★ SignalEvaluator 매수 판단 직전에 호출
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

#Region "필터 결과 모델"

    ''' <summary>개별 필터 판정 결과</summary>
    Public Class FilterCheckResult
        Public Property FilterId As String = ""
        Public Property FilterName As String = ""
        Public Property Mode As FilterMode = FilterMode.Off
        Public Property Triggered As Boolean = False
        Public Property Detail As String = ""

        ''' <summary>Block 모드이고 트리거되었으면 True</summary>
        Public ReadOnly Property IsBlocked As Boolean
            Get
                Return Triggered AndAlso Mode = FilterMode.Block
            End Get
        End Property
    End Class

    ''' <summary>전체 필터 판정 종합 결과</summary>
    Public Class FilterResult
        Public Property Passed As Boolean = True
        Public Property BlockedBy As String = ""
        Public Property Details As New List(Of FilterCheckResult)

        ''' <summary>관찰(Observe) 트리거 목록 (로그용)</summary>
        Public ReadOnly Property ObserveWarnings As List(Of FilterCheckResult)
            Get
                Return Details.Where(Function(d) d.Triggered AndAlso d.Mode = FilterMode.Observe).ToList()
            End Get
        End Property

        ''' <summary>로그 요약 문자열</summary>
        Public Function ToSummary() As String
            If Passed Then
                Dim warns = ObserveWarnings
                If warns.Count = 0 Then Return "필터통과"
                Return $"필터통과(경고: {String.Join(", ", warns.Select(Function(w) w.FilterId))})"
            Else
                Return $"필터차단: {BlockedBy}"
            End If
        End Function
    End Class

#End Region

    ''' <summary>
    ''' 위험 필터 엔진. 6종 필터를 순차 실행하고 결과를 반환한다.
    ''' Block 필터가 하나라도 트리거되면 Passed = False.
    ''' </summary>
    Public Class FilterEngine

        Private ReadOnly _settings As SimTradeSettings

        ' ── 필터별 모드 설정 (런타임 변경 가능) ──
        Private ReadOnly _filterModes As New Dictionary(Of String, FilterMode)(StringComparer.OrdinalIgnoreCase)

        ' ── 필터 ID 상수 ──
        Public Const F_GAP As String = "F_GAP"
        Public Const F_FAKE As String = "F_FAKE"
        Public Const F_VI As String = "F_VI"
        Public Const F_SPREAD As String = "F_SPREAD"
        Public Const F_VOLUME As String = "F_VOLUME"
        Public Const F_TIME As String = "F_TIME"

        Public Sub New(settings As SimTradeSettings)
            _settings = settings
            InitDefaultModes()
        End Sub


        ' ════════════════════════════════════════
        ' 공개 메서드
        ' ════════════════════════════════════════

        ''' <summary>
        ''' 모든 필터를 실행하고 종합 결과를 반환한다.
        ''' Block 필터가 트리거되면 즉시 실패 반환.
        ''' </summary>
        Public Function Evaluate(state As StockState) As FilterResult
            Dim result As New FilterResult()

            ' 필터 순서대로 실행
            Dim checks = New List(Of FilterCheckResult) From {
                CheckGap(state),
                CheckFakeBreakout(state),
                CheckVIProximity(state),
                CheckSpread(state),
                CheckVolume(state),
                CheckTime(state)
            }

            result.Details = checks

            For Each chk In checks
                If chk.IsBlocked Then
                    result.Passed = False
                    result.BlockedBy = $"{chk.FilterId}({chk.Detail})"
                    Return result  ' 첫 Block에서 즉시 반환
                End If
            Next

            Return result
        End Function


        ''' <summary>특정 필터의 모드를 변경한다.</summary>
        Public Sub SetMode(filterId As String, mode As FilterMode)
            _filterModes(filterId) = mode
        End Sub

        ''' <summary>특정 필터의 현재 모드를 조회한다.</summary>
        Public Function GetMode(filterId As String) As FilterMode
            Dim m As FilterMode = FilterMode.Off
            _filterModes.TryGetValue(filterId, m)
            Return m
        End Function

        ''' <summary>전체 필터 ID와 모드 목록을 반환한다.</summary>
        Public Function GetAllModes() As Dictionary(Of String, FilterMode)
            Return New Dictionary(Of String, FilterMode)(_filterModes, StringComparer.OrdinalIgnoreCase)
        End Function


        ' ════════════════════════════════════════
        ' 개별 필터 구현
        ' ════════════════════════════════════════

        ''' <summary>
        ''' F_GAP: 갭 상승 필터.
        ''' 당일 시가 vs 전일 종가 갭이 임계값(기본 8%) 이상이면 트리거.
        ''' 과도한 갭 상승은 하락 반전 위험이 높다.
        ''' </summary>
        Private Function CheckGap(state As StockState) As FilterCheckResult
            Dim chk As New FilterCheckResult() With {
                .FilterId = F_GAP, .FilterName = "갭상승",
                .Mode = GetMode(F_GAP)}

            If chk.Mode = FilterMode.Off Then Return chk
            If state.PrevClose <= 0 OrElse state.DayOpen <= 0 Then Return chk

            Dim gapRate = (state.DayOpen - state.PrevClose) / CDbl(state.PrevClose) * 100.0
            Dim threshold = _settings.Exclude_MaxDayGain  ' 기본 20% — 갭 기준은 8%가 적절

            ' 갭 필터 전용 임계값: MaxDayGain의 40% (예: 20% × 0.4 = 8%)
            Dim gapThreshold = threshold * 0.4
            If gapRate >= gapThreshold Then
                chk.Triggered = True
                chk.Detail = $"갭{gapRate:F1}%≥{gapThreshold:F1}%"
            End If

            Return chk
        End Function


        ''' <summary>
        ''' F_FAKE: 페이크 돌파 필터.
        ''' 최근 5봉 중 상승 후 즉시 하락 반전 패턴 감지.
        ''' (윗꼬리 비율이 몸통의 2배 이상인 캔들이 존재)
        ''' </summary>
        Private Function CheckFakeBreakout(state As StockState) As FilterCheckResult
            Dim chk As New FilterCheckResult() With {
                .FilterId = F_FAKE, .FilterName = "페이크돌파",
                .Mode = GetMode(F_FAKE)}

            If chk.Mode = FilterMode.Off Then Return chk
            If state.Candles.Count < 5 Then Return chk

            Dim idx = state.Candles.Count - 1
            Dim fakeCount = 0

            For i = idx To Math.Max(0, idx - 4) Step -1
                Dim c = state.Candles(i)
                Dim body = Math.Abs(c.Close - c.Open)
                Dim upperShadow = c.High - Math.Max(c.Close, c.Open)
                If body > 0 AndAlso upperShadow >= body * 2 Then
                    fakeCount += 1
                End If
            Next

            If fakeCount >= 2 Then
                chk.Triggered = True
                chk.Detail = $"윗꼬리캔들{fakeCount}개/5봉"
            End If

            Return chk
        End Function


        ''' <summary>
        ''' F_VI: VI(변동성 완화장치) 근접 필터.
        ''' 현재가가 상한가의 VI_NearRate(기본 90%) 이상이면 트리거.
        ''' 매수 진입 시 VI 발동 위험이 높은 가격대를 차단.
        ''' </summary>
        Private Function CheckVIProximity(state As StockState) As FilterCheckResult
            Dim chk As New FilterCheckResult() With {
                .FilterId = F_VI, .FilterName = "VI근접",
                .Mode = GetMode(F_VI)}

            If chk.Mode = FilterMode.Off Then Return chk
            If state.UpperLimitPrice <= 0 Then Return chk

            Dim viThreshold = CInt(state.UpperLimitPrice * state.VI_NearRate)
            If state.CurrentPrice >= viThreshold Then
                chk.Triggered = True
                chk.Detail = $"현재가{state.CurrentPrice:N0}≥VI기준{viThreshold:N0}"
            End If

            Return chk
        End Function


        ''' <summary>
        ''' F_SPREAD: 스프레드 필터.
        ''' 매수1호가와 매도1호가 차이가 임계값(기본 0.5%) 초과 시 트리거.
        ''' 유동성이 낮은 종목의 슬리피지 위험 차단.
        ''' </summary>
        Private Function CheckSpread(state As StockState) As FilterCheckResult
            Dim chk As New FilterCheckResult() With {
                .FilterId = F_SPREAD, .FilterName = "스프레드",
                .Mode = GetMode(F_SPREAD)}

            If chk.Mode = FilterMode.Off Then Return chk
            If state.Ask1 <= 0 OrElse state.Bid1 <= 0 Then Return chk

            Dim spreadRate = (state.Ask1 - state.Bid1) / CDbl(state.Bid1) * 100.0
            If spreadRate > _settings.MaxSpreadRate Then
                chk.Triggered = True
                chk.Detail = $"스프레드{spreadRate:F2}%>{_settings.MaxSpreadRate:F1}%"
            End If

            Return chk
        End Function


        ''' <summary>
        ''' F_VOLUME: 거래대금 필터.
        ''' 10시 기준 누적 거래대금이 임계값 미만이면 트리거.
        ''' 유동성 부족 종목 차단.
        ''' </summary>
        Private Function CheckVolume(state As StockState) As FilterCheckResult
            Dim chk As New FilterCheckResult() With {
                .FilterId = F_VOLUME, .FilterName = "거래대금",
                .Mode = GetMode(F_VOLUME)}

            If chk.Mode = FilterMode.Off Then Return chk

            ' 10시 이후에만 체크
            If DateTime.Now.TimeOfDay < TimeSpan.Parse("10:00") Then Return chk

            If state.AmountBy10AM > 0 AndAlso state.AmountBy10AM < _settings.Exclude_MinAmountBy10AM Then
                chk.Triggered = True
                chk.Detail = $"10시대금{state.AmountBy10AM / 100000000.0:F1}억<{_settings.Exclude_MinAmountBy10AM / 100000000.0:F1}억"
            ElseIf state.DayAmount > 0 AndAlso state.DayAmount < _settings.Exclude_MinAvgDailyAmount Then
                chk.Triggered = True
                chk.Detail = $"거래대금{state.DayAmount / 100000000.0:F1}억<{_settings.Exclude_MinAvgDailyAmount / 100000000.0:F1}억"
            End If

            Return chk
        End Function


        ''' <summary>
        ''' F_TIME: 시간대 필터.
        ''' 장초반(09:00~09:05) 또는 매수금지시간 이후 트리거.
        ''' SignalEvaluator에서도 시간 체크하지만, 필터로도 이중 체크.
        ''' </summary>
        Private Function CheckTime(state As StockState) As FilterCheckResult
            Dim chk As New FilterCheckResult() With {
                .FilterId = F_TIME, .FilterName = "시간제한",
                .Mode = GetMode(F_TIME)}

            If chk.Mode = FilterMode.Off Then Return chk

            Dim now = DateTime.Now.TimeOfDay
            If now < _settings.TradingStartTime Then
                chk.Triggered = True
                chk.Detail = $"장전({now:hh\:mm}<{_settings.TradingStartTime:hh\:mm})"
            ElseIf now >= _settings.NoNewBuyAfter Then
                chk.Triggered = True
                chk.Detail = $"매수금지({now:hh\:mm}≥{_settings.NoNewBuyAfter:hh\:mm})"
            End If

            Return chk
        End Function


        ' ════════════════════════════════════════
        ' 초기화
        ' ════════════════════════════════════════

        ''' <summary>기본 필터 모드 설정 (초기: 모두 Observe)</summary>
        Private Sub InitDefaultModes()
            _filterModes(F_GAP) = FilterMode.Observe
            _filterModes(F_FAKE) = FilterMode.Observe
            _filterModes(F_VI) = FilterMode.Observe
            _filterModes(F_SPREAD) = FilterMode.Observe
            _filterModes(F_VOLUME) = FilterMode.Observe
            _filterModes(F_TIME) = FilterMode.Block    ' 시간 필터만 Block 기본
        End Sub

    End Class

End Namespace
