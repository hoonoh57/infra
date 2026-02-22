' TickIntensity_Indicator.vb

Public Class TickIntensity_Indicator
    Implements IIndicator

    Private Const SMA5_PERIOD As Integer = 5
    Private Const SMA20_PERIOD As Integer = 20
    Private _timeframeMinutes As Integer = 1
    Private _params As New Dictionary(Of String, Object) From {{"TimeframeMinutes", 1}}
    Private _tickBars As New List(Of DateTime)
    Private ReadOnly _tickLock As New Object()

    Public Sub New(Optional timeframeMinutes As Integer = 1)
        _timeframeMinutes = timeframeMinutes
        _params("TimeframeMinutes") = _timeframeMinutes
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"TICKINT_{_timeframeMinutes}"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"TickIntensity({_timeframeMinutes}m)"
        End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 4
        End Get
    End Property
    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params
        End Get
        Set(value As Dictionary(Of String, Object))
            _params = value
            If _params.ContainsKey("TimeframeMinutes") Then
                _timeframeMinutes = CInt(_params("TimeframeMinutes"))
            End If
        End Set
    End Property

    Public Sub SetTickBars(tickTimestamps As List(Of DateTime))
        SyncLock _tickLock
            _tickBars = If(tickTimestamps, New List(Of DateTime))
            ' 사이보스 틱봉은 분단위 시각만 제공되므로 시간 정렬(Sort) 금지.
            ' 내림차순 응답일 때만 앞뒤 반전(Reverse)으로 방향만 맞춘다.
            If _tickBars.Count > 1 AndAlso _tickBars(0) > _tickBars(_tickBars.Count - 1) Then
                _tickBars.Reverse()
            End If
        End SyncLock
    End Sub

    Public Sub AddTick(ts As DateTime)
        SyncLock _tickLock
            _tickBars.Add(ts)
            ' 실시간 틱도 Sort 금지. 수신 순서를 그대로 유지한다.
        End SyncLock
    End Sub

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        Dim span = New TimeSpan(0, _timeframeMinutes, 0)
        Dim tickSums(count - 1) As Single
        Dim hasAnyTick As Boolean
        SyncLock _tickLock
            hasAnyTick = (_tickBars.Count > 0)
            Dim tickIdx = 0
            For i = 0 To count - 1
                Dim pStart = candles(i).Dt
                Dim pEnd = pStart.Add(span)
                Dim cnt = 0
                While tickIdx < _tickBars.Count AndAlso _tickBars(tickIdx) < pStart
                    tickIdx += 1
                End While
                Dim k = tickIdx
                While k < _tickBars.Count AndAlso _tickBars(k) < pEnd
                    cnt += 1
                    k += 1
                End While
                tickSums(i) = If(hasAnyTick, CSng(cnt), Single.NaN)
            Next
        End SyncLock
        Dim sma5 = CalcSMA(tickSums, SMA5_PERIOD)
        Dim sma20 = CalcSMA(tickSums, SMA20_PERIOD)
        For i = 0 To count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            r.Values("TickSum") = tickSums(i)
            r.Values("MA5") = sma5(i)
            r.Values("MA20") = sma20(i)
            If Not Single.IsNaN(sma20(i)) AndAlso sma20(i) > 0 Then
                r.Values("Ratio") = tickSums(i) / sma20(i) * 100.0F
            Else
                r.Values("Ratio") = Single.NaN
            End If
            results.Add(r)
        Next
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim i = candles.Count - 1
        Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
            .Values = New Dictionary(Of String, Single)}
        If i < 0 Then
            r.Values("TickSum") = Single.NaN
            r.Values("MA5") = Single.NaN
            r.Values("MA20") = Single.NaN
            r.Values("Ratio") = Single.NaN
            Return r
        End If
        Dim span = New TimeSpan(0, _timeframeMinutes, 0)
        Dim pStart = candles(i).Dt
        Dim pEnd = pStart.Add(span)
        Dim cnt = 0
        Dim hasAnyTick As Boolean
        SyncLock _tickLock
            hasAnyTick = (_tickBars.Count > 0)
            Dim lo = 0
            Dim hi = _tickBars.Count - 1
            While lo <= hi
                Dim mid = (lo + hi) \ 2
                If _tickBars(mid) < pStart Then
                    lo = mid + 1
                Else
                    hi = mid - 1
                End If
            End While
            Dim k = lo
            While k < _tickBars.Count AndAlso _tickBars(k) < pEnd
                cnt += 1
                k += 1
            End While
        End SyncLock
        If Not hasAnyTick Then
            r.Values("TickSum") = Single.NaN
            r.Values("MA5") = Single.NaN
            r.Values("MA20") = Single.NaN
            r.Values("Ratio") = Single.NaN
            Return r
        End If
        r.Values("TickSum") = CSng(cnt)
        Dim sum5 As Single = CSng(cnt)
        Dim valid5 = 1
        If prevResults IsNot Nothing Then
            Dim sJ = Math.Max(0, prevResults.Count - SMA5_PERIOD + 1)
            For j = prevResults.Count - 1 To sJ Step -1
                If valid5 >= SMA5_PERIOD Then Exit For
                Dim tsVal = prevResults(j).Val("TickSum")
                If Not Single.IsNaN(tsVal) Then
                    sum5 += tsVal
                    valid5 += 1
                End If
            Next
        End If
        r.Values("MA5") = If(valid5 >= SMA5_PERIOD, sum5 / SMA5_PERIOD, Single.NaN)
        Dim sum20 As Single = CSng(cnt)
        Dim valid20 = 1
        If prevResults IsNot Nothing Then
            Dim sJ = Math.Max(0, prevResults.Count - SMA20_PERIOD + 1)
            For j = prevResults.Count - 1 To sJ Step -1
                If valid20 >= SMA20_PERIOD Then Exit For
                Dim tsVal = prevResults(j).Val("TickSum")
                If Not Single.IsNaN(tsVal) Then
                    sum20 += tsVal
                    valid20 += 1
                End If
            Next
        End If
        Dim ma20Val = If(valid20 >= SMA20_PERIOD, sum20 / SMA20_PERIOD, Single.NaN)
        r.Values("MA20") = ma20Val
        If Not Single.IsNaN(ma20Val) AndAlso ma20Val > 0 Then
            r.Values("Ratio") = CSng(cnt) / ma20Val * 100.0F
        Else
            r.Values("Ratio") = Single.NaN
        End If
        Return r
    End Function

    Private Shared Function CalcSMA(data As Single(), period As Integer) As Single()
        Dim count = data.Length
        Dim result(count - 1) As Single
        Dim sum As Single = 0
        For i = 0 To count - 1
            sum += data(i)
            If i >= period Then
                sum -= data(i - period)
            End If
            If i < period - 1 Then
                result(i) = Single.NaN
            Else
                result(i) = sum / period
            End If
        Next
        Return result
    End Function
End Class
