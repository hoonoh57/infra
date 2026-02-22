' TradeStrength_Indicator.vb — 체결강도 (외부 데이터)

Public Class TradeStrength_Indicator
    Implements IIndicator

    Private _maPeriod As Integer = 10
    Private _params As New Dictionary(Of String, Object) From {{"MAPeriod", 10}}
    Private _rawData As New List(Of (Dt As DateTime, Strength As Single))
    Private ReadOnly _dataLock As New Object()

    Public Sub New(Optional maPeriod As Integer = 10)
        _maPeriod = maPeriod
        _params("MAPeriod") = _maPeriod
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"TSTR_{_maPeriod}"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"체결강도(MA{_maPeriod})"
        End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 8
        End Get
    End Property
    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params
        End Get
        Set(value As Dictionary(Of String, Object))
            _params = value
            If _params.ContainsKey("MAPeriod") Then _maPeriod = CInt(_params("MAPeriod"))
        End Set
    End Property

    Public Sub SetData(data As List(Of (Dt As DateTime, Strength As Single)))
        SyncLock _dataLock
            _rawData = If(data, New List(Of (Dt As DateTime, Strength As Single)))
            EnsureDataSortedLocked()
        End SyncLock
    End Sub

    Public Sub AddData(dt As DateTime, strength As Single)
        SyncLock _dataLock
            _rawData.Add((dt, strength))
            If _rawData.Count > 1 AndAlso _rawData(_rawData.Count - 1).Dt < _rawData(_rawData.Count - 2).Dt Then
                EnsureDataSortedLocked()
            End If
        End SyncLock
    End Sub

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        Dim mapped(count - 1) As Single
        SyncLock _dataLock
            Dim dIdx = 0
            For i = 0 To count - 1
                Dim cDt = candles(i).Dt
                Dim sum As Single = 0
                Dim cnt = 0
                Dim nextDt = If(i < count - 1, candles(i + 1).Dt, DateTime.MaxValue)
                While dIdx < _rawData.Count AndAlso _rawData(dIdx).Dt < nextDt
                    If _rawData(dIdx).Dt >= cDt Then
                        sum += _rawData(dIdx).Strength
                        cnt += 1
                    End If
                    dIdx += 1
                End While
                If cnt > 0 Then
                    mapped(i) = sum / cnt
                Else
                    mapped(i) = Single.NaN
                End If
            Next
        End SyncLock
        For i = 0 To count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            r.Values("Value") = mapped(i)
            If Not Single.IsNaN(mapped(i)) Then
                r.Values("Baseline") = 100.0F
                If mapped(i) >= 100 Then
                    r.Values("Direction") = 1.0F
                Else
                    r.Values("Direction") = -1.0F
                End If
            Else
                r.Values("Baseline") = Single.NaN
                r.Values("Direction") = Single.NaN
            End If
            results.Add(r)
        Next
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim full = Calculate(candles)
        If full.Count > 0 Then Return full(full.Count - 1)
        Dim emptyR As New IndicatorResult With {.Name = Name, .Index = candles.Count - 1, .PanelIndex = PanelIndex}
        emptyR.Values = New Dictionary(Of String, Single)
        emptyR.Values("Value") = Single.NaN
        emptyR.Values("Baseline") = Single.NaN
        emptyR.Values("Direction") = Single.NaN
        Return emptyR
    End Function

    Private Sub EnsureDataSortedLocked()
        If _rawData.Count <= 1 Then Return
        Dim sorted = True
        For j = 1 To _rawData.Count - 1
            If _rawData(j).Dt < _rawData(j - 1).Dt Then
                sorted = False
                Exit For
            End If
        Next
        If Not sorted Then
            _rawData.Sort(Function(a, b) a.Dt.CompareTo(b.Dt))
        End If
    End Sub
End Class
