Public Class SectorLeader_Indicator
    Implements IIndicator

    ' ── 필드 ──
    Private _params As New Dictionary(Of String, Object)
    Private _leaderCode As String = ""
    Private _leaderName As String = ""
    Private _leaderCandles As List(Of CandleItem)
    Private ReadOnly _candleLock As New Object()

    ' ── IIndicator 속성 ──
    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return "SECTOR_LEADER"
        End Get
    End Property

    Public ReadOnly Property DisplayName As String
        Get
            Return "주도섹터/주도주"
        End Get
    End Property

    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 9
        End Get
    End Property

    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params
        End Get
        Set(value As Dictionary(Of String, Object))
            _params = If(value, New Dictionary(Of String, Object))
        End Set
    End Property

    Public ReadOnly Property LeaderCode As String
        Get
            Return _leaderCode
        End Get
    End Property

    Public ReadOnly Property LeaderName As String
        Get
            Return _leaderName
        End Get
    End Property

    Private ReadOnly Property IIndicator_DisplayName As String Implements IIndicator.DisplayName
        Get
            Return DisplayName
        End Get
    End Property

    ' ══════════════════════════════════════
    ' 핵심: 대장주 캔들을 외부에서 주입
    ' ══════════════════════════════════════
    Public Sub SetLeader(code As String, name As String, candles As List(Of CandleItem))
        _leaderCode = If(code, "")
        _leaderName = If(name, "")
        SyncLock _candleLock
            _leaderCandles = candles  ' StockInfoManager에서 가져온 캔들 그대로
        End SyncLock
    End Sub

    ' ══════════════════════════════════════
    ' Calculate: 대장주 캔들 → 정규화 오버레이
    ' ══════════════════════════════════════
    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim results As New List(Of IndicatorResult)(candles.Count)
        If candles Is Nothing OrElse candles.Count = 0 Then Return results

        Dim leaderList As List(Of CandleItem)
        SyncLock _candleLock
            leaderList = _leaderCandles
        End SyncLock

        ' 대장주 캔들이 없으면 0으로 채움
        If leaderList Is Nothing OrElse leaderList.Count = 0 Then
            For i As Integer = 0 To candles.Count - 1
                results.Add(MakeEmptyResult(i))
            Next
            Return results
        End If

        ' ── 대장주 캔들을 시간 기준 Dictionary로 변환 ──
        Dim leaderByTime As New Dictionary(Of Long, CandleItem)
        For Each lc In leaderList
            Dim key = lc.Dt.Ticks
            If Not leaderByTime.ContainsKey(key) Then
                leaderByTime(key) = lc
            End If
        Next

        ' ── 정규화 기준가: 대장주 첫 캔들의 시가 ──
        Dim basePrice As Single = 0
        For Each lc In leaderList
            If lc.Open > 0 Then
                basePrice = lc.Open
                Exit For
            End If
        Next
        If basePrice <= 0 Then basePrice = 1  ' 0 방지

        ' ── 현재 종목 정규화 기준가 ──
        Dim myBasePrice As Single = 0
        For Each mc In candles
            If mc.Open > 0 Then
                myBasePrice = mc.Open
                Exit For
            End If
        Next
        If myBasePrice <= 0 Then myBasePrice = 1

        ' ── 시간 매칭으로 대장주 가격 정규화 ──
        Dim lastLeaderNorm As Single = 0
        Dim lastMyNorm As Single = 0
        Dim leaderIdx As Integer = 0

        For i As Integer = 0 To candles.Count - 1
            Dim mc = candles(i)
            Dim r As New IndicatorResult()
            r.Name = "SECTOR_LEADER"
            r.Index = i
            r.PanelIndex = 9

            ' 시간 정확 매칭 시도
            Dim matched As CandleItem = Nothing
            If leaderByTime.ContainsKey(mc.Dt.Ticks) Then
                matched = leaderByTime(mc.Dt.Ticks)
            Else
                ' 시간 근접 매칭 (±30초 이내)
                While leaderIdx < leaderList.Count - 1 AndAlso leaderList(leaderIdx + 1).Dt <= mc.Dt
                    leaderIdx += 1
                End While
                If leaderIdx < leaderList.Count AndAlso
                   Math.Abs((leaderList(leaderIdx).Dt - mc.Dt).TotalSeconds) < 90 Then
                    matched = leaderList(leaderIdx)
                End If
            End If

            If matched IsNot Nothing AndAlso matched.Close > 0 Then
                ' 대장주: 기준가 대비 등락률 (%)
                lastLeaderNorm = CSng((matched.Close / basePrice - 1) * 100)
                r.Values("LeaderPrice") = matched.Close
            End If

            ' 현재 종목: 기준가 대비 등락률 (%)
            If mc.Close > 0 Then
                lastMyNorm = CSng((mc.Close / myBasePrice - 1) * 100)
            End If

            r.Values("LeaderNorm") = lastLeaderNorm     ' 대장주 정규화 등락률
            r.Values("MyNorm") = lastMyNorm              ' 현재종목 정규화 등락률
            r.Values("Spread") = lastMyNorm - lastLeaderNorm  ' 괴리도
            r.Values("LeaderScore") = lastLeaderNorm     ' 렌더러 호환용

            results.Add(r)
        Next

        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem),
                                prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        ' 마지막 캔들만 재계산
        If candles Is Nothing OrElse candles.Count = 0 Then Return Nothing
        Dim fullResults = Calculate(candles)
        If fullResults.Count > 0 Then Return fullResults(fullResults.Count - 1)
        Return Nothing
    End Function

    Private Function MakeEmptyResult(index As Integer) As IndicatorResult
        Dim r As New IndicatorResult()
        r.Name = "SECTOR_LEADER"
        r.Index = index
        r.PanelIndex = 9
        r.Values("LeaderNorm") = 0
        r.Values("MyNorm") = 0
        r.Values("Spread") = 0
        r.Values("LeaderScore") = 0
        r.Values("LeaderPrice") = 0
        Return r
    End Function
End Class
