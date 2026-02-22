' SectorLeader_Indicator.vb — 주도섹터/주도주 (외부 데이터)

Public Class SectorLeader_Indicator
    Implements IIndicator

    Private _params As New Dictionary(Of String, Object)
    Private _sectorRank As Integer = 0
    Private _totalSectors As Integer = 0
    Private _stockRankInSector As Integer = 0
    Private _totalStocksInSector As Integer = 0
    Private _sectorChangeRate As Single = 0
    Private _isLeaderSector As Boolean = False
    Private _isLeaderStock As Boolean = False

    Public Sub New()
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return "SECTOR_LEADER"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
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
            _params = value
        End Set
    End Property

    Public Sub UpdateSnapshot(sectorRank As Integer, totalSectors As Integer,
                               stockRank As Integer, totalStocks As Integer, sectorChange As Single)
        _sectorRank = sectorRank
        _totalSectors = totalSectors
        _stockRankInSector = stockRank
        _totalStocksInSector = totalStocks
        _sectorChangeRate = sectorChange
        _isLeaderSector = (sectorRank <= Math.Max(1, totalSectors * 0.1))
        _isLeaderStock = (stockRank <= Math.Max(1, totalStocks * 0.15))
    End Sub

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        For i = 0 To count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
                .Values = New Dictionary(Of String, Single)}
            If _totalSectors > 0 Then
                Dim secScore = (1.0F - CSng(_sectorRank) / _totalSectors) * 50
                Dim stkScore As Single = 0
                If _totalStocksInSector > 0 Then
                    stkScore = (1.0F - CSng(_stockRankInSector) / _totalStocksInSector) * 50
                End If
                r.Values("LeaderScore") = secScore + stkScore
                If _isLeaderSector Then r.Values("IsLeaderSector") = 1.0F Else r.Values("IsLeaderSector") = 0.0F
                If _isLeaderStock Then r.Values("IsLeaderStock") = 1.0F Else r.Values("IsLeaderStock") = 0.0F
            Else
                r.Values("LeaderScore") = Single.NaN
                r.Values("IsLeaderSector") = Single.NaN
                r.Values("IsLeaderStock") = Single.NaN
            End If
            results.Add(r)
        Next
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim i = candles.Count - 1
        Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = PanelIndex,
            .Values = New Dictionary(Of String, Single)}
        If _totalSectors > 0 Then
            Dim secScore = (1.0F - CSng(_sectorRank) / _totalSectors) * 50
            Dim stkScore As Single = 0
            If _totalStocksInSector > 0 Then
                stkScore = (1.0F - CSng(_stockRankInSector) / _totalStocksInSector) * 50
            End If
            r.Values("LeaderScore") = secScore + stkScore
            If _isLeaderSector Then r.Values("IsLeaderSector") = 1.0F Else r.Values("IsLeaderSector") = 0.0F
            If _isLeaderStock Then r.Values("IsLeaderStock") = 1.0F Else r.Values("IsLeaderStock") = 0.0F
        Else
            r.Values("LeaderScore") = Single.NaN
            r.Values("IsLeaderSector") = Single.NaN
            r.Values("IsLeaderStock") = Single.NaN
        End If
        Return r
    End Function
End Class
