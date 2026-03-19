' ═══════════════════════════════════════════════════════════════
' RefCandleAnalyzer.vb — 기준봉 분석기 (원칙서 v4.0)
' ═══════════════════════════════════════════════════════════════
' ★ 기준봉 식별: 거래량돌파 + 양봉 + 최대 TickSum
' ★ 기준봉 대비 현재가 위치 분석
' ★ AdaptiveParamCalc.CalcReferenceCandle과 연동
' ═══════════════════════════════════════════════════════════════

Namespace SimTrade

    ''' <summary>기준봉 정보</summary>
    Public Class RefCandleInfo
        Public Property IsValid As Boolean = False
        Public Property CandleIndex As Integer = -1
        Public Property CandleDate As DateTime = DateTime.MinValue
        Public Property High As Single = 0
        Public Property Low As Single = 0
        Public Property Close As Single = 0
        Public Property Volume As Long = 0
        Public Property TickSum As Double = 0
        Public Property NormalizedTickSum As Double = 0
        Public Property AvgVolume20 As Long = 0
        Public Property VolumeRatio As Single = 0

        Public Function ToSummary() As String
            If Not IsValid Then Return "기준봉 없음"
            Return $"기준봉[{CandleDate:HH:mm:ss}] H={High:N0} TickSum={NormalizedTickSum:F1} VolRatio={VolumeRatio:F1}x"
        End Function
    End Class

    ''' <summary>현재가 vs 기준봉 위치 분석</summary>
    Public Class RefCandlePosition
        Public Property IsAboveHigh As Boolean = False
        Public Property IsAboveClose As Boolean = False
        Public Property IsBelowLow As Boolean = False
        Public Property DistanceFromHighPct As Single = 0
        Public Property DistanceFromClosePct As Single = 0
        Public Property BarsFromRefCandle As Integer = 0

        Public Function ToSummary() As String
            Dim pos As String
            If IsAboveHigh Then
                pos = "기준봉 고점 위"
            ElseIf IsAboveClose Then
                pos = "기준봉 종가~고점"
            ElseIf IsBelowLow Then
                pos = "기준봉 저점 아래"
            Else
                pos = "기준봉 저점~종가"
            End If
            Return $"{pos} (고점대비={DistanceFromHighPct:F1}%, {BarsFromRefCandle}봉 경과)"
        End Function
    End Class

    ''' <summary>기준봉 분석기</summary>
    Public Class RefCandleAnalyzer

        Private ReadOnly _settings As SimTradeSettings
        Private ReadOnly _refCandles As New Dictionary(Of String, RefCandleInfo)(StringComparer.OrdinalIgnoreCase)

        Public Sub New(settings As SimTradeSettings)
            _settings = settings
        End Sub

        ''' <summary>기준봉 식별 — 캔들 목록에서 최적 기준봉 선택</summary>
        Public Function FindReferenceCandle(code As String, candles As List(Of CandleItem)) As RefCandleInfo
            Dim result As New RefCandleInfo()
            If candles Is Nothing OrElse candles.Count < 20 Then
                Return result
            End If

            ' 20봉 평균 거래량
            Dim lookback = Math.Min(candles.Count, _settings.RefCandle_LookbackDays)
            Dim startIdx = Math.Max(0, candles.Count - lookback)
            Dim volSum As Long = 0
            Dim volCount As Integer = 0

            For i = startIdx To candles.Count - 1
                volSum += candles(i).Volume
                volCount += 1
            Next
            Dim avgVol As Long = If(volCount > 0, CLng(volSum / volCount), 1)

            ' 기준봉 후보: 거래량 > 평균 × 2 + 양봉
            Dim bestIdx = -1
            Dim bestTickSum As Double = 0

            For i = startIdx To candles.Count - 2   ' 마지막 봉은 미완성이므로 제외
                Dim c = candles(i)
                If c.Volume < avgVol * 2 Then Continue For
                If c.Close <= c.Open Then Continue For  ' 양봉만

                Dim tickSum = c.NormalizedTickSum
                If tickSum > bestTickSum Then
                    bestTickSum = tickSum
                    bestIdx = i
                End If
            Next

            If bestIdx < 0 Then Return result

            Dim best = candles(bestIdx)
            result.IsValid = True
            result.CandleIndex = bestIdx
            result.CandleDate = best.Dt
            result.High = best.High
            result.Low = best.Low
            result.Close = best.Close
            result.Volume = best.Volume
            result.TickSum = best.TickCount
            result.NormalizedTickSum = best.NormalizedTickSum
            result.AvgVolume20 = avgVol
            If avgVol > 0 Then
                result.VolumeRatio = CSng(best.Volume / avgVol)
            End If

            ' 캐시 저장
            _refCandles(code) = result
            Return result
        End Function

        ''' <summary>현재가 vs 기준봉 위치 분석</summary>
        Public Function AnalyzePosition(code As String, currentPrice As Single, currentCandleIndex As Integer) As RefCandlePosition
            Dim pos As New RefCandlePosition()
            Dim refCandle As RefCandleInfo = Nothing

            If Not _refCandles.TryGetValue(code, refCandle) OrElse Not refCandle.IsValid Then
                Return pos
            End If

            If refCandle.High <= 0 Then Return pos

            pos.BarsFromRefCandle = currentCandleIndex - refCandle.CandleIndex
            pos.DistanceFromHighPct = (currentPrice / refCandle.High - 1) * 100
            pos.DistanceFromClosePct = (currentPrice / refCandle.Close - 1) * 100
            pos.IsAboveHigh = currentPrice > refCandle.High
            pos.IsAboveClose = currentPrice > refCandle.Close
            pos.IsBelowLow = currentPrice < refCandle.Low

            Return pos
        End Function

        ''' <summary>캐시된 기준봉 조회</summary>
        Public Function GetCachedRefCandle(code As String) As RefCandleInfo
            Dim rc As RefCandleInfo = Nothing
            If _refCandles.TryGetValue(code, rc) Then Return rc
            Return New RefCandleInfo()
        End Function

        ''' <summary>기준봉 갱신 필요 여부 (새 기준봉 후보 등장)</summary>
        Public Function NeedsUpdate(code As String, candles As List(Of CandleItem)) As Boolean
            Dim cached As RefCandleInfo = Nothing
            If Not _refCandles.TryGetValue(code, cached) OrElse Not cached.IsValid Then
                Return True
            End If

            ' 마지막 캔들이 기존 기준봉보다 TickSum이 높으면 갱신
            If candles IsNot Nothing AndAlso candles.Count > 1 Then
                Dim last = candles(candles.Count - 2)  ' 직전 완성 봉
                If last.Close > last.Open AndAlso
                   last.NormalizedTickSum > cached.NormalizedTickSum * 1.5 Then
                    Return True
                End If
            End If

            Return False
        End Function

        ''' <summary>종목 제거</summary>
        Public Sub RemoveStock(code As String)
            If _refCandles.ContainsKey(code) Then _refCandles.Remove(code)
        End Sub

        ''' <summary>전체 초기화</summary>
        Public Sub Clear()
            _refCandles.Clear()
        End Sub

    End Class

End Namespace
