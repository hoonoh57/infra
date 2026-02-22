' ProgramTrade_Indicator.vb ???꾨줈洹몃옩 ?쒕ℓ??(?몃? ?곗씠??

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
        Return String.Concat(ChrW(&HD504), ChrW(&HB85C), ChrW(&HADF8), ChrW(&HB7A8), ChrW(&HC21C), ChrW(&HB9E4), ChrW(&HC218))
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
            Dim isDateMode As Boolean = False
            If _rawData.Count > 0 Then
                Dim timeKeySet As New HashSet(Of Integer)
                For Each d In _rawData
                    timeKeySet.Add(d.Dt.Hour * 10000 + d.Dt.Minute * 100 + d.Dt.Second)
                    If timeKeySet.Count > 3 Then Exit For
                Next
                isDateMode = (timeKeySet.Count <= 3)
            End If

            If isDateMode Then
                Dim byDate As New Dictionary(Of Date, Single)
                For Each d In _rawData
                    byDate(d.Dt.Date) = d.NetBuy
                Next

                Dim firstIndexByDate As New Dictionary(Of Date, Integer)
                For i = 0 To count - 1
                    Dim cd = candles(i).Dt.Date
                    If Not firstIndexByDate.ContainsKey(cd) Then
                        firstIndexByDate(cd) = i
                    End If
                Next

                Dim prevDayNet As Single = Single.NaN
                For i = 0 To count - 1
                    Dim v As Single = Single.NaN
                    Dim cd = candles(i).Dt.Date
                    If byDate.ContainsKey(cd) Then
                        v = byDate(cd)
                    End If
                    Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                        .Values = New Dictionary(Of String, Single)}
                    r.Values("NetBuy") = v
                    If Not Single.IsNaN(v) Then
                        Dim isDayOpen = (firstIndexByDate.ContainsKey(cd) AndAlso firstIndexByDate(cd) = i)
                        If isDayOpen Then
                            If Single.IsNaN(prevDayNet) Then
                                r.Values("DeltaBar") = Single.NaN
                            Else
                                r.Values("DeltaBar") = v - prevDayNet
                            End If
                            prevDayNet = v
                        Else
                            r.Values("DeltaBar") = Single.NaN
                        End If
                    Else
                        r.Values("DeltaBar") = Single.NaN
                    End If
                    results.Add(r)
                Next
                Return results
            End If

            Dim dIdx = 0
            Dim currentNet As Single = Single.NaN
            Dim prevNet As Single = Single.NaN
            Dim currentDataDate As Date = Date.MinValue
            Dim prevNetDate As Date = Date.MinValue
            For i = 0 To count - 1
                Dim cDt = candles(i).Dt
                While dIdx < _rawData.Count AndAlso _rawData(dIdx).Dt <= cDt
                    currentNet = _rawData(dIdx).NetBuy
                    currentDataDate = _rawData(dIdx).Dt.Date
                    dIdx += 1
                End While

                Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                    .Values = New Dictionary(Of String, Single)}

                Dim sameDay As Boolean = (currentDataDate <> Date.MinValue AndAlso cDt.Date = currentDataDate)
                If Single.IsNaN(currentNet) OrElse Not sameDay Then
                    r.Values("NetBuy") = Single.NaN
                    r.Values("DeltaBar") = Single.NaN
                Else
                    r.Values("NetBuy") = currentNet
                    If Single.IsNaN(prevNet) OrElse prevNetDate <> cDt.Date Then
                        r.Values("DeltaBar") = Single.NaN
                    Else
                        r.Values("DeltaBar") = currentNet - prevNet
                    End If
                    prevNet = currentNet
                    prevNetDate = cDt.Date
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

