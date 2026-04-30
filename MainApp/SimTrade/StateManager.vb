' ═══════════════════════════════════════════════════════════════
' StateManager.vb — 종목 상태 단일 진실 소스 (제13조 13-4)
' ═══════════════════════════════════════════════════════════════
' 원칙서 v4.0. 모든 종목 상태를 중앙 관리.
' UI는 스냅샷으로만 읽고, Decision Layer만 상태를 변경한다.
'
' [v4.1 수정] 2026-03-20
'   ① IsValidTransition: Detected→Ready, Downloading→Ready 허용
'     (캐시 히트 시 Analyzing 단계 불필요, 현재 아키텍처에서 Analyzing 미사용)
'   ② TransitionTo: SyncLock 범위 최소화, 로깅 확인용 반환값 유지
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Concurrent

Namespace SimTrade

    Public Class StateManager

        Private ReadOnly _states As New ConcurrentDictionary(Of String, StockState)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _lock As New Object()

        ' ── 이벤트 ──
        Public Event StateChanged(code As String, oldState As DataState, newState As DataState)
        Public Event StockAdded(code As String)
        Public Event StockRemoved(code As String)

        ' ════════════════════════════════════════
        ' 종목 추가/제거
        ' ════════════════════════════════════════

        ''' <summary>종목 등록 (DETECTED 상태로 시작)</summary>
        Public Function AddStock(code As String, name As String) As StockState
            Dim state As New StockState()
            state.Code = code
            state.Name = name
            state.State = DataState.Detected
            state.AddedTime = DateTime.Now

            If _states.TryAdd(code, state) Then
                RaiseEvent StockAdded(code)
                Return state
            End If

            ' 이미 존재하면 기존 반환
            Return _states(code)
        End Function

        ''' <summary>종목 제거</summary>
        Public Sub RemoveStock(code As String)
            Dim removed As StockState = Nothing
            If _states.TryRemove(code, removed) Then
                RaiseEvent StockRemoved(code)
            End If
        End Sub

        ''' <summary>전체 초기화</summary>
        Public Sub Clear()
            _states.Clear()
        End Sub

        ' ════════════════════════════════════════
        ' 상태 전이 (제12조 상태머신)
        ' ════════════════════════════════════════

        ''' <summary>상태 전이. 유효하지 않은 전이는 무시하고 False 반환.</summary>
        Public Function TransitionTo(code As String, newState As DataState, Optional reason As String = "") As Boolean
            Dim state As StockState = Nothing
            If Not _states.TryGetValue(code, state) Then Return False

            Dim oldState = state.State

            ' 유효한 전이 검증
            If Not IsValidTransition(oldState, newState) Then Return False

            SyncLock _lock
                state.State = newState
                If newState = DataState.Excluded Then
                    state.ExclusionReason = reason
                End If
            End SyncLock

            RaiseEvent StateChanged(code, oldState, newState)
            Return True
        End Function

        ''' <summary>
        ''' 유효한 상태 전이 규칙
        ''' [v4.1 수정] Detected→Ready, Downloading→Ready 허용 (캐시 히트/직접 전이 지원)
        ''' </summary>
        Private Function IsValidTransition(from As DataState, [to] As DataState) As Boolean
            Select Case from
                Case DataState.None
                    Return [to] = DataState.Detected

                Case DataState.Detected
                    ' v4.1: 캐시 히트 시 Detected→Ready 직접 전이 허용
                    Return [to] = DataState.Downloading OrElse
                           [to] = DataState.Ready OrElse
                           [to] = DataState.Excluded

                Case DataState.Downloading
                    ' v4.1: 캔들 수신 후 Downloading→Ready 직접 전이 허용 (Analyzing 단계 생략)
                    Return [to] = DataState.Analyzing OrElse
                           [to] = DataState.Ready OrElse
                           [to] = DataState.Excluded

                Case DataState.Analyzing
                    Return [to] = DataState.Ready OrElse [to] = DataState.Excluded

                Case DataState.Ready
                    Return [to] = DataState.Trading OrElse [to] = DataState.Excluded

                Case DataState.Trading
                    Return [to] = DataState.Closed
                    ' Trading 중에는 Excluded 불가 (보유 종목 제외 금지 — 금지 ⑪)

                Case DataState.Closed
                    Return False  ' 최종 상태

                Case DataState.Excluded
                    Return False  ' 최종 상태

                Case Else
                    Return False
            End Select
        End Function

        ' ════════════════════════════════════════
        ' 조회
        ' ════════════════════════════════════════

        ''' <summary>종목 상태 조회 (Nothing이면 미등록)</summary>
        Public Function GetState(code As String) As StockState
            Dim state As StockState = Nothing
            _states.TryGetValue(code, state)
            Return state
        End Function

        ''' <summary>특정 DataState인 종목 목록</summary>
        Public Function GetStocksByState(dataState As DataState) As List(Of StockState)
            Return _states.Values.Where(Function(s) s.State = dataState).ToList()
        End Function

        ''' <summary>READY 이상 (매매 대상) 종목 목록</summary>
        Public Function GetActiveStocks() As List(Of StockState)
            Return _states.Values.Where(Function(s) s.State = DataState.Ready OrElse s.State = DataState.Trading).ToList()
        End Function

        ''' <summary>보유 중인 종목 목록</summary>
        Public Function GetHoldingStocks() As List(Of StockState)
            Return _states.Values.Where(Function(s) s.HasPosition).ToList()
        End Function

        ''' <summary>전체 종목 수</summary>
        Public ReadOnly Property TotalCount As Integer
            Get
                Return _states.Count
            End Get
        End Property

        ''' <summary>특정 상태 종목 수</summary>
        Public Function CountByState(dataState As DataState) As Integer
            Return _states.Values.AsEnumerable.Where(Function(s) s.State = dataState).Count
        End Function

        ' ════════════════════════════════════════
        ' UI용 스냅샷 (제13조 — UI는 스냅샷으로만 읽기)
        ' ════════════════════════════════════════

        ''' <summary>전 종목의 표시용 스냅샷 반환</summary>
        Public Function GetSnapshot() As List(Of StockStateSnapshot)
            Return _states.Values.Select(Function(s) CreateSnapshot(s)).ToList()
        End Function

        Private Function CreateSnapshot(s As StockState) As StockStateSnapshot
            Dim snap As New StockStateSnapshot()
            snap.Code = s.Code
            snap.Name = s.Name
            snap.State = s.State
            snap.ExclusionReason = s.ExclusionReason
            snap.CurrentPrice = s.CurrentPrice
            snap.ChangeRate = s.ChangeRate
            snap.DayVolume = s.DayVolume
            snap.DayAmount = s.DayAmount
            snap.ST_Direction = s.ST_Direction
            snap.JMA_Direction = s.JMA_Direction
            snap.TickSum_Normalized = s.TickSum_Normalized
            snap.OBV_Direction = s.OBV_Direction
            snap.RSI_Value = s.RSI_Value
            snap.MACD_Histogram = s.MACD_Histogram
            snap.Volume_Ratio = s.Volume_Ratio
            snap.HasPosition = s.HasPosition
            snap.CurrentPnLRate = s.CurrentPnLRate
            snap.LastSignal = s.LastSignal
            snap.TopNRank = s.TopNRank
            snap.TopNScore = s.TopNScore
            snap.TopTickScore = s.TopTickScore
            snap.TopAmountScore = s.TopAmountScore
            snap.TopTrendScore = s.TopTrendScore
            snap.HighSinceBuy = s.HighSinceBuy
            snap.CandleCount = If(s.Candles IsNot Nothing, s.Candles.Count, 0)
            snap.TickBarCount = s.TickBarCount

            Return snap
        End Function

        ' ════════════════════════════════════════
        ' 시세 업데이트 (Data Layer에서 호출)
        ' ════════════════════════════════════════

        ''' <summary>틱 수신 시 가격 정보 갱신</summary>
        Public Sub UpdatePrice(code As String, price As Integer, volume As Long,
                               ask1 As Integer, bid1 As Integer, changeRate As Double)
            Dim state As StockState = Nothing
            If Not _states.TryGetValue(code, state) Then Return

            state.CurrentPrice = price
            state.DayVolume = volume
            state.Ask1 = ask1
            state.Bid1 = bid1
            state.ChangeRate = changeRate

            ' 보유 중이면 고점/손익 갱신
            If state.HasPosition AndAlso price > state.HighSinceBuy Then
                state.HighSinceBuy = price
            End If
            If state.HasPosition AndAlso state.BuyPrice > 0 Then
                state.CurrentPnLRate = (price - state.BuyPrice) / CDbl(state.BuyPrice) * 100.0
            End If
        End Sub

        ''' <summary>지표 최신값 갱신 (캔들 완성 후 호출)</summary>
        Public Sub UpdateIndicators(code As String,
                                     stDir As Double, jmaDir As Double, jmaPrevDir As Double,
                                     tickSum As Double, tickMA5 As Double, tickMA20 As Double,
                                     obvDir As Double, rsi As Double,
                                     macdHist As Double, volRatio As Double)
            Dim state As StockState = Nothing
            If Not _states.TryGetValue(code, state) Then Return

            ' JMA 전환 봉 추적
            If jmaDir > 0 AndAlso jmaPrevDir <= 0 Then
                state.JMA_TurnBar = 0  ' 방금 전환
            ElseIf state.JMA_TurnBar >= 0 Then
                state.JMA_TurnBar += 1
            End If

            state.ST_Direction = stDir
            state.JMA_Direction = jmaDir
            state.JMA_PrevDirection = jmaPrevDir
            state.TickSum_Normalized = tickSum
            state.TickMA5_Normalized = tickMA5
            state.TickMA20_Normalized = tickMA20
            state.OBV_Direction = obvDir
            state.RSI_Value = rsi
            state.MACD_Histogram = macdHist
            state.Volume_Ratio = volRatio

            ' 당일 최고 TickSum 갱신
            If tickSum > state.DayMaxTickSum Then
                state.DayMaxTickSum = tickSum
            End If
        End Sub

        ''' <summary>포지션 등록 (매수 체결 시)</summary>
        Public Sub RegisterPosition(code As String, buyPrice As Integer, qty As Integer)
            Dim state As StockState = Nothing
            If Not _states.TryGetValue(code, state) Then Return

            state.HasPosition = True
            state.BuyPrice = buyPrice
            state.BuyQty = qty
            state.BuyTime = DateTime.Now
            state.HighSinceBuy = buyPrice
            state.CurrentPnLRate = 0
            state.LastBuyTime = DateTime.Now

            TransitionTo(code, DataState.Trading)
        End Sub

        ''' <summary>포지션 해제 (매도 체결 시)</summary>
        Public Sub ClearPosition(code As String)
            Dim state As StockState = Nothing
            If Not _states.TryGetValue(code, state) Then Return

            state.HasPosition = False
            state.BuyPrice = 0
            state.BuyQty = 0
            state.HighSinceBuy = 0
            state.CurrentPnLRate = 0

            TransitionTo(code, DataState.Closed)
        End Sub

    End Class

    ' ════════════════════════════════════════
    ' UI용 읽기 전용 스냅샷
    ' ════════════════════════════════════════

    Public Class StockStateSnapshot
        Public Property Code As String = ""
        Public Property Name As String = ""
        Public Property State As DataState = DataState.None
        Public Property ExclusionReason As String = ""
        Public Property CurrentPrice As Integer = 0
        Public Property ChangeRate As Double = 0
        Public Property DayVolume As Long = 0
        Public Property DayAmount As Long = 0
        Public Property ST_Direction As Double = Double.NaN
        Public Property JMA_Direction As Double = Double.NaN
        Public Property TickSum_Normalized As Double = Double.NaN
        Public Property OBV_Direction As Double = Double.NaN
        Public Property RSI_Value As Double = Double.NaN
        Public Property MACD_Histogram As Double = Double.NaN
        Public Property Volume_Ratio As Double = Double.NaN
        Public Property HasPosition As Boolean = False
        Public Property CurrentPnLRate As Double = 0
        Public Property LastSignal As String = ""
        Public Property TopNRank As Integer = 0
        Public Property TopNScore As Double = 0
        Public Property TopTickScore As Double = 0
        Public Property TopAmountScore As Double = 0
        Public Property TopTrendScore As Double = 0
        Public Property HighSinceBuy As Integer = 0
        Public Property CandleCount As Integer = 0
        Public Property TickBarCount As Integer = 0

    End Class

End Namespace




