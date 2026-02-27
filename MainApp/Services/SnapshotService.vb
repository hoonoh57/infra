' ═══════════════════════════════════════════════════════════════
' SnapshotService.vb — 시장 데이터 스냅샷 생성 서비스
' ═══════════════════════════════════════════════════════════════

Imports MainApp.Models
Imports MainApp.ChartEngine.Models
Imports System.Collections.Generic
Imports System

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
                        Dim indName = If(kvp.Key, "")
                        Dim results = kvp.Value
                        If results Is Nothing OrElse i >= results.Count Then Continue For
                            Dim res = results(i)
                            If res Is Nothing OrElse res.Values Is Nothing Then Continue For

                            For Each kvpVal In res.Values
                                Dim key = kvpVal.Key
                                Dim value = kvpVal.Value
                                If String.IsNullOrWhiteSpace(key) Then Continue For

                                ' 1) 원본 키
                                snap.SetIndicator(key, value)

                                ' 2) 지표명.키 (충돌 방지)
                                If Not String.IsNullOrWhiteSpace(indName) Then
                                    snap.SetIndicator($"{indName}.{key}", value)
                                End If

                                ' 3) 전략에서 쓰는 표준 별칭
                                SetStrategyAliases(snap, indName, key, value)
                            Next
                    Next
                End If

                snapshots.Add(snap)
            Next

            Return snapshots
        End Function

        Private Shared Sub SetStrategyAliases(snap As MarketSnapshot, indicatorName As String, valueKey As String, value As Single)
            If snap Is Nothing Then Return
            Dim name = If(indicatorName, "").ToUpperInvariant()
            Dim key = If(valueKey, "").ToUpperInvariant()

            ' SuperTrend: Price vs SuperTrend
            If (name.StartsWith("ST_") OrElse name.Contains("SUPERTREND")) AndAlso key = "VALUE" Then
                snap.SetIndicator("SuperTrend", value)
                snap.SetIndicator("SUPERTREND", value)
                Return
            End If

            ' MA/JMA/RSI: Value를 지표명으로 직접 접근 가능하게
            If key = "VALUE" Then
                If name.StartsWith("SMA_") OrElse name.StartsWith("EMA_") OrElse name.StartsWith("WMA_") OrElse
                   name.StartsWith("JMA_") OrElse name.StartsWith("RSI_") Then
                    snap.SetIndicator(name, value)
                    Return
                End If
            End If

            ' MACD alias
            If name.StartsWith("MACD_") Then
                If key = "MACD" Then snap.SetIndicator("MACD_LINE", value)
                If key = "SIGNAL" Then snap.SetIndicator("MACD_SIGNAL", value)
                If key = "MACD" Then snap.SetIndicator("MACD_Line", value)
                If key = "SIGNAL" Then snap.SetIndicator("MACD_Signal", value)
                Return
            End If

            ' TickIntensity alias
            If name.StartsWith("TICKINT_") OrElse name.StartsWith("TICKINTENSITY") Then
                If key = "RATIO" Then snap.SetIndicator("TICK_RAT", value)
                Return
            End If

            ' ProgramTrade alias
            If name.StartsWith("PROG_TRADE") OrElse name.StartsWith("PROGRAM") Then
                If key = "NETBUY" Then snap.SetIndicator("PROGRAM_NET", value)
                If key = "DELTABAR" Then snap.SetIndicator("PROGRAM_DELTA", value)
            End If
        End Sub
    End Class
End Namespace
