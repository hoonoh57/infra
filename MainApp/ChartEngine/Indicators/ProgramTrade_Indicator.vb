' ProgramTrade_Indicator.vb — 프로그램 순매수 (외부 데이터)

Public Class ProgramTrade_Indicator
    Implements IIndicator

    Private _params As New Dictionary(Of String, Object)
    Private _rawData As New List(Of (Dt As DateTime, NetBuy As Single))
    Private ReadOnly _dataLock As New Object()

    Public Sub New()
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return "PROG_TRADE"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return "프로그램순매수"
        End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 7
        End Get
    End Property
    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params
        End Get
        Set(value As Dictionary(Of String, Object))
            _params = value
        End Set
    End Property

    Public Sub SetData(data As List(Of (Dt As DateTime, NetBuy As Single)))
        SyncLock _dataLock
            _rawData = If(data, New List(Of (Dt As DateTime, NetBuy As Single)))
            EnsureDataSortedLocked()
        End SyncLock
    End Sub

    Public Sub AddData(dt As DateTime, netBuy As Single)
        SyncLock _dataLock
            _rawData.Add((dt, netBuy))
            If _rawData.Count > 1 AndAlso _rawData(_rawData.Count - 1).Dt < _rawData(_rawData.Count - 2).Dt Then
                EnsureDataSortedLocked()
            End If
        End SyncLock
    End Sub

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        SyncLock _dataLock
            Dim dIdx = 0
            For i = 0 To count - 1
                Dim cDt = candles(i).Dt
                Dim sum As Single = 0
                Dim matched = False
                Dim nextDt = If(i < count - 1, candles(i + 1).Dt, DateTime.MaxValue)
                While dIdx < _rawData.Count AndAlso _rawData(dIdx).Dt < nextDt
                    If _rawData(dIdx).Dt >= cDt Then
                        sum += _rawData(dIdx).NetBuy
                        matched = True
                    End If
                    dIdx += 1
                End While
                Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                    .Values = New Dictionary(Of String, Single)}
                If matched Then
                    r.Values("NetBuy") = sum
                    If sum >= 0 Then
                        r.Values("Direction") = 1.0F
                    Else
                        r.Values("Direction") = -1.0F
                    End If
                Else
                    r.Values("NetBuy") = Single.NaN
                    r.Values("Direction") = Single.NaN
                End If
                results.Add(r)
            Next
        End SyncLock
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim full = Calculate(candles)
        If full.Count > 0 Then Return full(full.Count - 1)
        Dim emptyR As New IndicatorResult With {.Name = Name, .Index = candles.Count - 1, .PanelIndex = PanelIndex}
        emptyR.Values = New Dictionary(Of String, Single)
        emptyR.Values("NetBuy") = Single.NaN
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
