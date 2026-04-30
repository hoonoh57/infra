' ═══════════════════════════════════════════════════════════════
' SimTradeConstants.vb — 불변 상수 · 정적 헬퍼 · 인터페이스 정의
' ═══════════════════════════════════════════════════════════════
' [v4.2] 리팩토링: SimTradeForm.vb에서 불변 요소를 분리.
'   이 파일은 수치 변경 없이 유지. 새 상수 추가만 허용.
'   삭제·수정 금지.
' ═══════════════════════════════════════════════════════════════

Imports System.Collections.Concurrent

Namespace SimTrade

#Region "상수 정의"

    ''' <summary>SimTrade 전역 상수 (불변)</summary>
    Public Module SimTradeConst

        ' ── 쓰로틀링 ──
        Public Const TICK_THROTTLE_MS As Integer = 500          ' 종목당 틱 처리 최소 간격
        Public Const LOG_7COND_THROTTLE_SEC As Integer = 5      ' 7조건 상세 로그 간격
        Public Const LOG_TIMER_INTERVAL_MS As Integer = 150     ' 로그 큐 배치 처리 간격
        Public Const REFRESH_TIMER_INTERVAL_MS As Integer = 1000 ' UI 갱신 타이머 간격
        Public Const MAX_LOG_PER_BATCH As Integer = 20          ' 배치당 로그 최대 건수

        ' ── 용량 제한 ──
        Public Const MAX_WATCH_STOCKS As Integer = 50           ' 최대 감시 종목 수
        Public Const MAX_CANDLES As Integer = 500               ' 종목당 최대 캔들 수
        Public Const MAX_LOG_LINES As Integer = 5000            ' RichTextBox 최대 라인

        ' ── 틱 진단 ──
        Public Const TICK_DIAG_COUNT As Integer = 10            ' 진단 로그 출력 건수

        ' ── 7조건 최소 출력 기준 ──
        Public Const MIN_CONDITIONS_FOR_LOG As Integer = 3      ' 3개 이상 충족 시만 로그

        ' ── UI 그리드 컬럼 (Watch) ──
        Public ReadOnly WATCH_COLUMNS As String() =
            {"코드", "종목명", "현재가", "등락률", "거래량",
             "ST", "JMA", "TickSum", "OBV", "RSI", "MACD",
             "TopN", "TopScore", "TopTick", "TopAmt", "TopTrend", "상태", "신호", "봉수"}

        ' ── UI 그리드 컬럼 (Position) ──
        Public ReadOnly POSITION_COLUMNS As String() =
            {"코드", "종목명", "매수가", "현재가", "수량",
             "수익률", "고점", "보유봉", "사유"}

        ' ── UI 그리드 컬럼 (History) ──
        Public ReadOnly HISTORY_COLUMNS As String() =
            {"코드", "종목명", "매수가", "매도가", "수량",
             "순손익", "수익률", "비용", "보유봉", "사유", "시각"}

    End Module

#End Region

#Region "인터페이스"

    ''' <summary>엔진 이벤트를 UI에 전달하는 인터페이스 (불변 계약)</summary>
    Public Interface ISimTradeView

        ''' <summary>로그 메시지 추가</summary>
        Sub Log(message As String)

        ''' <summary>UI 스레드에서 안전하게 실행</summary>
        Sub SafeUI(action As Action)

        ''' <summary>상태바 텍스트 갱신</summary>
        Sub UpdateStatus(text As String, color As System.Drawing.Color)

        ''' <summary>요약 레이블 갱신</summary>
        Sub UpdateSummary(text As String)

        ''' <summary>감시 그리드 갱신 요청</summary>
        Sub RequestWatchRefresh()

        ''' <summary>포지션 그리드 갱신 요청</summary>
        Sub RequestPositionRefresh()

        ''' <summary>매매 이력 그리드에 행 추가</summary>
        Sub AddHistoryRow(record As TradeRecord)

        ''' <summary>실행 중 여부</summary>
        ReadOnly Property IsRunning As Boolean

    End Interface

#End Region

#Region "정적 헬퍼"

    ''' <summary>지표 결과 조회 등 순수 함수 모음 (불변)</summary>
    Public Module SimTradeHelper

        ''' <summary>지표 결과에서 접두사로 찾기</summary>
        Public Function FindResult(results As Dictionary(Of String, List(Of IndicatorResult)),
                                   prefix As String) As List(Of IndicatorResult)
            If results Is Nothing Then Return Nothing
            Dim key = results.Keys.FirstOrDefault(
                Function(k) k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            If key Is Nothing Then Return Nothing
            Dim list As List(Of IndicatorResult) = Nothing
            results.TryGetValue(key, list)
            Return list
        End Function

        ''' <summary>NormalizedTickSum 계산 (불변 공식)</summary>
        Public Function NormalizeTickSum(rawTickSum As Double, intervalSec As Integer) As Double
            If intervalSec <= 0 OrElse Single.IsNaN(CSng(rawTickSum)) Then Return rawTickSum
            Return rawTickSum * (60.0 / intervalSec)
        End Function

        ''' <summary>방향 문자열 (양수 ▲ 음수 ▼)</summary>
        Public Function DirectionChar(value As Double) As String
            If Double.IsNaN(value) Then Return "-"
            Return If(value > 0, "▲", If(value < 0, "▼", "─"))
        End Function

        ''' <summary>조건 충족 아이콘</summary>
        Public Function CondIcon(met As Boolean) As String
            Return If(met, "●", "○")
        End Function

        ''' <summary>7조건 충족 등급 태그</summary>
        Public Function CondGrade(metCount As Integer) As String
            If metCount = 7 Then Return "★★★"
            If metCount >= 5 Then Return "★★"
            Return "★"
        End Function

        ''' <summary>DataState 한글 표시</summary>
        Public Function StateText(state As DataState) As String
            Select Case state
                Case DataState.None : Return "없음"
                Case DataState.Detected : Return "감지"
                Case DataState.Downloading : Return "다운로드"
                Case DataState.Analyzing : Return "분석"
                Case DataState.Ready : Return "준비"
                Case DataState.Trading : Return "매매중"
                Case DataState.Closed : Return "종료"
                Case DataState.Excluded : Return "제외"
                Case Else : Return state.ToString()
            End Select
        End Function

    End Module

#End Region

End Namespace

