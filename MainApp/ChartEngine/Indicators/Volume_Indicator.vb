' ═══════════════════════════════════════════════════════════════
' Volume_Indicator.vb — 거래량 + 이동평균 (링 버퍼 증분 최적화)
' ═══════════════════════════════════════════════════════════════
' v4.0: 링 버퍼 도입으로 UpdateLast O(1) 달성
'       같은 봉 재호출 시 정확한 복원
' ═══════════════════════════════════════════════════════════════

Public Class Volume_Indicator
    Implements IIndicator

    Private _period As Integer = 20
    Private _params As New Dictionary(Of String, Object) From {{"Period", 20}}

    ' ── 링 버퍼 상태 ──
    Private _ringBuffer() As Single
    Private _ringPos As Integer = 0
    Private _ringCount As Integer = 0
    Private _ringSum As Single = 0
    Private _stateIndex As Integer = -1
    Private _prevRingSum As Single = 0     ' 복원용
    Private _prevRingPos As Integer = 0
    Private _prevRingCount As Integer = 0
    Private _prevVolume As Single = 0

    Public Sub New(Optional period As Integer = 20)
        _period = period
        _params("Period") = _period
        ReDim _ringBuffer(_period - 1)
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"VOL_{_period}" : End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"Vol MA({_period})" : End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 3 : End Get
    End Property
    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params : End Get
        Set(value As Dictionary(Of String, Object))
            _params = value
            If _params.ContainsKey("Period") Then _period = CInt(_params("Period"))
        End Set
    End Property

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)

        ' 링 버퍼 초기화
        ReDim _ringBuffer(_period - 1)
        _ringPos = 0
        _ringCount = 0
        _ringSum = 0

        For i = 0 To count - 1
            Dim vol = CSng(candles(i).Volume)
            PushRing(vol)
            results.Add(MakeResult(i, vol, GetMA()))
        Next

        _stateIndex = count - 1
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim i = candles.Count - 1

        If _stateIndex < 0 Then
            Dim full = Calculate(candles)
            If full.Count > 0 Then Return full(full.Count - 1)
            Return MakeResult(i, 0, Single.NaN)
        End If

        ' ★ 같은 봉 재호출 시 복원
        If i = _stateIndex Then
            UndoRing()
        End If

        Dim vol = CSng(candles(i).Volume)
        PushRing(vol)
        _stateIndex = i

        Return MakeResult(i, vol, GetMA())
    End Function

    ' ── 링 버퍼 ──

    Private Sub PushRing(value As Single)
        If _ringCount >= _period Then
            _ringSum -= _ringBuffer(_ringPos)
        End If
        _ringBuffer(_ringPos) = value
        _ringSum += value
        _ringPos = (_ringPos + 1) Mod _period
        If _ringCount < _period Then _ringCount += 1
    End Sub

    Private Sub UndoRing()
        If _ringCount <= 0 Then Return
        _ringPos = (_ringPos - 1 + _period) Mod _period
        _ringSum -= _ringBuffer(_ringPos)
        If _ringCount <= _period Then _ringCount -= 1
    End Sub

    Private Function GetMA() As Single
        If _ringCount < _period Then Return Single.NaN
        Return _ringSum / _period
    End Function

    Private Function MakeResult(idx As Integer, vol As Single, ma As Single) As IndicatorResult
        Dim r As New IndicatorResult With {.Name = Name, .Index = idx, .PanelIndex = PanelIndex,
            .Values = New Dictionary(Of String, Single)}
        r.Values("Volume") = vol
        r.Values("MA") = ma
        If Not Single.IsNaN(ma) AndAlso ma > 0 Then
            r.Values("Ratio") = vol / ma * 100.0F
        Else
            r.Values("Ratio") = Single.NaN
        End If
        Return r
    End Function
End Class
