' ═══════════════════════════════════════════════════════════════
' RSI_Indicator.vb — RSI Wilder smoothing (증분 계산 최적화)
' ═══════════════════════════════════════════════════════════════
' v4.0: _stateIndex 추가, _avgGain/_avgLoss 이중 보관
'       UpdateLast 재호출 시 편향 없는 복원
' ═══════════════════════════════════════════════════════════════

Public Class RSI_Indicator
    Implements IIndicator

    Private _period As Integer = 14
    Private _params As New Dictionary(Of String, Object) From {{"Period", 14}}

    ' ── 증분 계산 상태 ──
    Private _avgGain As Single = Single.NaN        ' 마지막 확정 봉의 평균 이득
    Private _avgLoss As Single = Single.NaN        ' 마지막 확정 봉의 평균 손실
    Private _prevAvgGain As Single = Single.NaN    ' 복원용 (직전 확정 값)
    Private _prevAvgLoss As Single = Single.NaN    ' 복원용
    Private _stateIndex As Integer = -1            ' 마지막으로 확정 계산된 캔들 인덱스

    Public Sub New(Optional period As Integer = 14)
        _period = period
        _params("Period") = _period
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"RSI_{_period}" : End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"RSI({_period})" : End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 1 : End Get
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

        Dim gains(count - 1) As Single
        Dim losses(count - 1) As Single
        For i = 1 To count - 1
            Dim diff = candles(i).Close - candles(i - 1).Close
            If diff > 0 Then gains(i) = diff Else losses(i) = Math.Abs(diff)
        Next

        Dim ag As Single = 0
        Dim al As Single = 0
        _prevAvgGain = Single.NaN
        _prevAvgLoss = Single.NaN

        For i = 0 To count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            If i < _period Then
                r.Values("Value") = Single.NaN
            ElseIf i = _period Then
                Dim sumG As Single = 0, sumL As Single = 0
                For j = 1 To _period : sumG += gains(j) : sumL += losses(j) : Next
                ag = sumG / _period
                al = sumL / _period
                r.Values("Value") = CalcRSI(ag, al)
            Else
                _prevAvgGain = ag
                _prevAvgLoss = al
                ag = (ag * (_period - 1) + gains(i)) / _period
                al = (al * (_period - 1) + losses(i)) / _period
                r.Values("Value") = CalcRSI(ag, al)
            End If
            r.Values("Upper") = 70
            r.Values("Lower") = 30
            results.Add(r)
        Next

        _avgGain = ag
        _avgLoss = al
        _stateIndex = count - 1
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim i = candles.Count - 1

        ' 상태 없으면 전체 계산 폴백
        If _stateIndex < 0 OrElse i < _period OrElse Single.IsNaN(_avgGain) Then
            Dim full = Calculate(candles)
            If full.Count > 0 Then Return full(full.Count - 1)
            Return MakeEmpty(i)
        End If

        ' ★ 같은 봉 재호출 시 이전 상태로 복원
        If i = _stateIndex AndAlso Not Single.IsNaN(_prevAvgGain) Then
            _avgGain = _prevAvgGain
            _avgLoss = _prevAvgLoss
        End If

        ' 현재 봉 계산
        _prevAvgGain = _avgGain
        _prevAvgLoss = _avgLoss

        Dim diff = candles(i).Close - candles(i - 1).Close
        Dim g As Single = If(diff > 0, diff, 0)
        Dim l As Single = If(diff < 0, Math.Abs(diff), 0)

        _avgGain = (_avgGain * (_period - 1) + g) / _period
        _avgLoss = (_avgLoss * (_period - 1) + l) / _period
        _stateIndex = i

        Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
            .Values = New Dictionary(Of String, Single)}
        r.Values("Value") = CalcRSI(_avgGain, _avgLoss)
        r.Values("Upper") = 70
        r.Values("Lower") = 30
        Return r
    End Function

    Private Function CalcRSI(ag As Single, al As Single) As Single
        If al = 0 Then Return 100.0F
        Return 100.0F - 100.0F / (1.0F + ag / al)
    End Function

    Private Function MakeEmpty(idx As Integer) As IndicatorResult
        Dim r As New IndicatorResult With {.Name = Name, .Index = idx, .PanelIndex = PanelIndex,
            .Values = New Dictionary(Of String, Single)}
        r.Values("Value") = Single.NaN
        r.Values("Upper") = 70
        r.Values("Lower") = 30
        Return r
    End Function
End Class
