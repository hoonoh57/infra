' ═══════════════════════════════════════════════════════════════
' CandleBuilder.vb — 동적 캔들 빌더 (원칙서 v4.0 제13조)
' ═══════════════════════════════════════════════════════════════
' ★ 시간대별 캔들 간격 자동 전환 (10/20/30초)
' ★ 구간 전환 시 진행 중 캔들 강제 마감
' ★ 틱 → 캔들 집계 + 캔들 완성 이벤트
' ★ TickIntensity 1분 정규화 포함
' ★ SimTradeForm.BuildCandle()을 대체
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

    ''' <summary>
    ''' 종목별 캔들 빌더. 틱 데이터를 수신하여 시간대별 동적 간격으로 캔들을 생성한다.
    ''' 원칙서 v4.0:
    '''   09:00~09:10 = 10초 (개장 고변동)
    '''   09:10~09:30 = 20초 (안정화)
    '''   09:30~14:30 = 30초 (정상)
    '''   14:30~15:15 = 30초 (장마감, 청산 전용)
    ''' </summary>
    Public Class CandleBuilder

        ' ── 설정 참조 ──
        Private ReadOnly _settings As SimTradeSettings

        ' ── 이벤트 ──

        ''' <summary>캔들이 완성(Close)되었을 때 발생. code, 완성 캔들, 전체 캔들 리스트.</summary>
        Public Event CandleCompleted(code As String, candle As CandleItem, candles As List(Of CandleItem))

        ''' <summary>구간 전환으로 캔들이 강제 마감되었을 때 발생.</summary>
        Public Event CandleForceClosedOnPhaseChange(code As String, oldIntervalSec As Integer, newIntervalSec As Integer)

        ' ── 종목별 빌드 상태 ──
        Private ReadOnly _buildStates As New Dictionary(Of String, CandleBuildState)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _lock As New Object()

        ' ── 상수 ──
        Private Const MAX_CANDLES As Integer = 500

        ''' <summary>생성자</summary>
        Public Sub New(settings As SimTradeSettings)
            _settings = settings
        End Sub


        ' ════════════════════════════════════════
        ' 공개 메서드
        ' ════════════════════════════════════════

        ''' <summary>
        ''' 틱 데이터 수신 시 호출.
        ''' 캔들 경계를 판단하여 기존 캔들 업데이트 또는 새 캔들 생성.
        ''' 캔들 완성 시 CandleCompleted 이벤트를 발화한다.
        ''' </summary>
        ''' <param name="code">종목코드</param>
        ''' <param name="price">체결가</param>
        ''' <param name="volume">체결수량 (이 틱 1건의 거래량)</param>
        ''' <param name="tickTime">체결시각</param>
        ''' <param name="candles">해당 종목의 캔들 리스트 (참조 전달 — 직접 추가됨)</param>
        Public Sub OnTick(code As String, price As Single, volume As Long,
                          tickTime As DateTime, candles As List(Of CandleItem))

            If price <= 0 Then Return

            Dim bs = GetOrCreateBuildState(code)
            Dim currentInterval = GetIntervalForTime(tickTime.TimeOfDay)

            ' ── 구간 전환 감지 ──
            If bs.LastIntervalSec > 0 AndAlso bs.LastIntervalSec <> currentInterval Then
                ForceCloseCurrentCandle(code, bs, candles, tickTime)
                RaiseEvent CandleForceClosedOnPhaseChange(code, bs.LastIntervalSec, currentInterval)
            End If
            bs.LastIntervalSec = currentInterval

            ' ── 캔들 경계 계산 ──
            Dim intervalTicks = TimeSpan.FromSeconds(currentInterval).Ticks
            Dim candleStart = New DateTime(tickTime.Ticks - (tickTime.Ticks Mod intervalTicks))

            ' ── 새 캔들 필요 여부 ──
            Dim needNewCandle = (candles.Count = 0) OrElse (candleStart > bs.CurrentCandleStart)

            If needNewCandle Then
                ' 이전 캔들 완성 처리
                If candles.Count > 0 AndAlso bs.CurrentCandleStart <> DateTime.MinValue Then
                    Dim completed = candles(candles.Count - 1)
                    completed.IntervalSec = bs.LastIntervalSec
                    NormalizeTickIntensity(completed)
                    RaiseEvent CandleCompleted(code, completed, candles)
                End If

                ' 새 캔들 생성
                bs.CurrentCandleStart = candleStart
                bs.BarsSinceStart += 1

                Dim newCandle = CandleItem.Create(candleStart, price)
                newCandle.Volume = volume
                newCandle.TickCount = 1
                newCandle.IntervalSec = currentInterval
                candles.Add(newCandle)

                ' 최대 수 제한
                While candles.Count > MAX_CANDLES
                    candles.RemoveAt(0)
                End While
            Else
                ' 기존 캔들 업데이트 (TickCount는 UpdateFromTick 내부에서 +1)
                Dim last = candles(candles.Count - 1)
                last.UpdateFromTick(price, volume, tickTime)
            End If
        End Sub


        ''' <summary>
        ''' 다운로드된 과거 캔들로 빌드 상태를 초기화할 때 호출.
        ''' </summary>
        Public Sub InitializeFromHistory(code As String, candles As List(Of CandleItem))
            If candles Is Nothing OrElse candles.Count = 0 Then Return

            Dim bs = GetOrCreateBuildState(code)
            Dim lastCandle = candles(candles.Count - 1)
            bs.CurrentCandleStart = lastCandle.Dt
            bs.LastIntervalSec = GetIntervalForTime(lastCandle.Dt.TimeOfDay)
            bs.BarsSinceStart = candles.Count
        End Sub


        ''' <summary>종목 제거 시 빌드 상태 정리.</summary>
        Public Sub RemoveStock(code As String)
            SyncLock _lock
                If _buildStates.ContainsKey(code) Then _buildStates.Remove(code)
            End SyncLock
        End Sub


        ''' <summary>전체 초기화</summary>
        Public Sub Clear()
            SyncLock _lock
                _buildStates.Clear()
            End SyncLock
        End Sub


        ''' <summary>현재 시각 기준 캔들 간격(초) 반환. 외부 참조 가능.</summary>
        Public Function GetCurrentIntervalSec() As Integer
            Return GetIntervalForTime(DateTime.Now.TimeOfDay)
        End Function


        ''' <summary>특정 시각 기준 캔들 간격(초) 반환.</summary>
        Public Function GetIntervalForTime(tod As TimeSpan) As Integer
            If tod < _settings.Phase_Open_End Then
                Return _settings.CandleInterval_Open              ' 기본 10초

            ElseIf tod < _settings.Phase_EarlyMorning_End Then
                Return _settings.CandleInterval_EarlyMorning      ' 기본 20초

            ElseIf tod < _settings.Phase_Normal_End Then
                Return _settings.CandleInterval_Normal            ' 기본 30초

            Else
                Return _settings.CandleInterval_Close             ' 기본 30초
            End If
        End Function


        ''' <summary>디버그/로그용 빌드 정보 문자열.</summary>
        Public Function GetBuildInfo(code As String) As String
            Dim bs As CandleBuildState = Nothing
            SyncLock _lock
                If Not _buildStates.TryGetValue(code, bs) Then Return "미등록"
            End SyncLock
            Return $"Interval={bs.LastIntervalSec}s, Start={bs.CurrentCandleStart:HH:mm:ss}, Bars={bs.BarsSinceStart}"
        End Function


        ' ════════════════════════════════════════
        ' 내부 메서드
        ' ════════════════════════════════════════

        ''' <summary>진행 중 캔들 강제 마감 (구간 전환 시)</summary>
        Private Sub ForceCloseCurrentCandle(code As String, bs As CandleBuildState,
                                            candles As List(Of CandleItem), tickTime As DateTime)
            If candles.Count = 0 Then Return

            Dim last = candles(candles.Count - 1)
            last.IntervalSec = bs.LastIntervalSec
            NormalizeTickIntensity(last)

            ' 완성 이벤트 발화
            RaiseEvent CandleCompleted(code, last, candles)

            ' 빌드 상태 초기화 → 다음 틱에서 새 캔들 생성
            bs.CurrentCandleStart = DateTime.MinValue
        End Sub


        ''' <summary>
        ''' TickIntensity 1분 정규화 (원칙서 제2조 2-2)
        ''' Normalized_TickSum = TickCount × (60 / IntervalSec)
        ''' 예: 10초봉 TickCount=5 → 5 × 6 = 30
        '''     20초봉 TickCount=8 → 8 × 3 = 24
        '''     30초봉 TickCount=10 → 10 × 2 = 20
        ''' </summary>
        Private Sub NormalizeTickIntensity(candle As CandleItem)
            If candle.IntervalSec <= 0 Then Return
            candle.NormalizedTickSum = candle.TickCount * (60.0 / candle.IntervalSec)
        End Sub


        ''' <summary>빌드 상태 조회 또는 생성</summary>
        Private Function GetOrCreateBuildState(code As String) As CandleBuildState
            SyncLock _lock
                Dim bs As CandleBuildState = Nothing
                If Not _buildStates.TryGetValue(code, bs) Then
                    bs = New CandleBuildState()
                    _buildStates(code) = bs
                End If
                Return bs
            End SyncLock
        End Function


        ' ════════════════════════════════════════
        ' 내부 클래스: 종목별 빌드 상태
        ' ════════════════════════════════════════

        Private Class CandleBuildState
            ''' <summary>현재 진행 중인 캔들의 시작 시각</summary>
            Public Property CurrentCandleStart As DateTime = DateTime.MinValue

            ''' <summary>직전 캔들의 간격(초) — 구간 전환 감지용</summary>
            Public Property LastIntervalSec As Integer = 0

            ''' <summary>이 종목에서 생성된 총 캔들 수 (디버그용)</summary>
            Public Property BarsSinceStart As Integer = 0
        End Class

    End Class

End Namespace
