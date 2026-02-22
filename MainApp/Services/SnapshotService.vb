' ═══════════════════════════════════════════════════════════════
' SnapshotService.vb — 시장 데이터 스냅샷 생성 서비스
' ═══════════════════════════════════════════════════════════════

Imports MainApp.Models
Imports MainApp.ChartEngine.Models
Imports System.Collections.Generic

Namespace Services
    Public Class SnapshotService
        Public Shared Function CreateSnapshots(stockCode As String,
                                                candles As List(Of CandleItem),
                                                indicatorResults As Dictionary(Of String, List(Of IndicatorResult)),
                                                Optional prevClose As Double = 0) As List(Of MarketSnapshot)
            Dim snapshots As New List(Of MarketSnapshot)
            If candles Is Nothing OrElse candles.Count = 0 Then Return snapshots

            For i As Integer = 0 To candles.Count - 1
                Dim candle = candles(i)
                Dim snap As New MarketSnapshot With {
                    .Time = candle.Dt,
                    .Code = stockCode,
                    .Open = candle.Open,
                    .High = candle.High,
                    .Low = candle.Low,
                    .Close = candle.Close
                }

                ' 기본 시장 지표 계산
                If candle.Open > 0 Then
                    snap.SetIndicator("CHG_OPEN_PCT", (candle.Close - candle.Open) / candle.Open * 100.0)
                End If

                If prevClose > 0 Then
                    snap.SetIndicator("VI_UP_99", prevClose * 1.09) ' VI 상한가 직전 (약 9% 상승 시)
                    snap.SetIndicator("PREV_CLOSE", prevClose)
                End If

                ' 지표 데이터 매핑
                If indicatorResults IsNot Nothing Then
                    For Each kvp In indicatorResults
                        Dim results = kvp.Value
                        If i < results.Count Then
                            Dim res = results(i)
                            If res IsNot Nothing Then
                                For Each kvpVal In res.Values
                                    snap.SetIndicator(kvpVal.Key, kvpVal.Value)
                                Next
                            End If
                        End If
                    Next
                End If

                snapshots.Add(snap)
            Next

            Return snapshots
        End Function
    End Class
End Namespace
