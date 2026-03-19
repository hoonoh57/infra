' ═══════════════════════════════════════════════════════════════
' OBV_Indicator.vb — OBV + SMA Signal (증분 계산 최적화)
' ═══════════════════════════════════════════════════════════════
' v4.0: 링 버퍼 SMA, _stateIndex, _lastOBV 상태 보관
'       UpdateLast에서 Calculate 호출 완전 제거
' ═══════════════════════════════════════════════════════════════

Public Class OBV_Indicator
    Implements IIndicator

    Private _maPeriod As Integer = 20
    Private _params As New Dictionary(Of String, Object) From {{"MAPeriod", 20}}

    ' ── 증분 계산 상태 ──
    Private _lastOBV As Single = 0             ' 직전 캔들까지의 OBV 누적값
    Private _prevOBV As Single = 0             ' 직전-1 캔들의 OBV (UpdateLast 복원용)
    Private _stateIndex As Integer = -1        ' 마지막으로 Calculate된 캔들 인덱스
    Private _ringBuffer() As Single            ' SMA용 링 버퍼 (크기 = _maPeriod)
    Private _ringPos As Integer = 0            ' 링 버퍼 현재 위치
    Private _ringCount As Integer = 0          ' 링 버퍼에 들어간 데이터 수
    Private _ringSum As Single = 0             ' 링 버퍼 합계 (SMA 즉시 계산용)

    Public Sub New(Optional maPeriod As Integer = 20)
        _maPeriod = maPeriod
        _params("MAPeriod") = _maPeriod
        ReDim _ringBuffer(_maPeriod - 1)
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"OBV_{_maPeriod}" : End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"OBV(MA{_maPeriod})" : End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 5 : End Get
    End Property
    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params : End Get
        Set(value As Dictionary(Of String, Object))
            _params = value
            If _params.ContainsKey("MAPeriod") Then _maPeriod = CInt(_params("MAPeriod"))
        End Set
    End Property

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        If count = 0 Then Return results

        ' 상태 초기화
        ReDim _ringBuffer(_maPeriod - 1)
        _ringPos = 0
        _ringCount = 0
        _ringSum = 0
        _lastOBV = 0
        _prevOBV = 0

        Dim obv As Single = CSng(candles(0).Volume)
        _lastOBV = obv
        PushRing(obv)
        results.Add(MakeResult(0, obv, GetRingSMA()))

        For i = 1 To count - 1
            _prevOBV = obv
            If candles(i).Close > candles(i - 1).Close Then
                obv += CSng(candles(i).Volume)
            ElseIf candles(i).Close < candles(i - 1).Close Then
                obv -= CSng(candles(i).Volume)
            End If
            _lastOBV = obv
            PushRing(obv)
            results.Add(MakeResult(i, obv, GetRingSMA()))
        Next

        _stateIndex = count - 1
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim i = candles.Count - 1

        ' 상태가 없으면 전체 계산으로 폴백
        If _stateIndex < 0 OrElse i < 1 Then
            Dim full = Calculate(candles)
            If full.Count > 0 Then Return full(full.Count - 1)
            Return MakeResult(i, 0, Single.NaN)
        End If

        ' ★ 같은 봉 재호출 시 이전 상태로 복원 후 재계산
        If i = _stateIndex Then
            ' 링 버퍼에서 마지막 값 되돌리기
            UndoRing()
            _lastOBV = _prevOBV
        End If

        ' 현재 봉의 OBV 계산
        Dim obv = _lastOBV
        _prevOBV = obv
        If candles(i).Close > candles(i - 1).Close Then
            obv += CSng(candles(i).Volume)
        ElseIf candles(i).Close < candles(i - 1).Close Then
            obv -= CSng(candles(i).Volume)
        End If
        _lastOBV = obv
        PushRing(obv)
        _stateIndex = i

        Return MakeResult(i, obv, GetRingSMA())
    End Function

    ' ── 링 버퍼 메서드 ──

    Private Sub PushRing(value As Single)
        ' 꽉 찼으면 가장 오래된 값을 합계에서 뺌
        If _ringCount >= _maPeriod Then
            _ringSum -= _ringBuffer(_ringPos)
        End If
        _ringBuffer(_ringPos) = value
        _ringSum += value
        _ringPos = (_ringPos + 1) Mod _maPeriod
        If _ringCount < _maPeriod Then _ringCount += 1
    End Sub

    Private Sub UndoRing()
        ' 마지막 Push를 되돌림
        If _ringCount <= 0 Then Return
        _ringPos = (_ringPos - 1 + _maPeriod) Mod _maPeriod
        _ringSum -= _ringBuffer(_ringPos)
        If _ringCount <= _maPeriod Then _ringCount -= 1
    End Sub

    Private Function GetRingSMA() As Single
        If _ringCount < _maPeriod Then Return Single.NaN
        Return _ringSum / _maPeriod
    End Function

    Private Function MakeResult(idx As Integer, obv As Single, sma As Single) As IndicatorResult
        Dim r As New IndicatorResult With {.Name = Name, .Index = idx, .PanelIndex = PanelIndex,
            .Values = New Dictionary(Of String, Single)}
        r.Values("OBV") = obv
        r.Values("Signal") = sma
        If Not Single.IsNaN(sma) Then
            r.Values("Direction") = If(obv > sma, 1.0F, -1.0F)
        Else
            r.Values("Direction") = Single.NaN
        End If
        Return r
    End Function
End Class
