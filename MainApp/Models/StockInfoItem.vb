Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports [Shared]

Public Enum DataSourceType
    조건검색 = 0
    주도섹터 = 1
    프로그램순매수 = 2
    관심종목 = 3
    코스피추종 = 4
    코스닥추종 = 5
    수동추가 = 6
    테마 = 7
End Enum

Public Enum DataReadyState
    None = 0
    InfoLoaded = 1
    CandleLoaded = 2
    RealtimeOn = 3
    Ready = 4
    Filtered = 5
End Enum

Public Enum SelectionState
    None = 0
    BaseRejected = 1
    Candidate = 2
    Top10 = 3
    EntryCandidate = 4
    Holding = 5
    ExitBlocked = 6
End Enum

Public Class StockInfoItem

#Region "Identity / Source"
    Public Property Code As String = ""
    Public Property Name As String = ""
    Public Property Sources As New HashSet(Of DataSourceType)()
    Public Property SourceDetail As String = ""
    Public Property AddedTime As DateTime = DateTime.Now
#End Region

#Region "Realtime Quote"
    Public Property Price As Integer = 0
    Public Property PrevClose As Integer = 0
    Public Property Change As Integer = 0
    Public Property ChangeRate As Double = 0
    Public Property Volume As Long = 0
    Public Property Open As Integer = 0
    Public Property High As Integer = 0
    Public Property Low As Integer = 0
    Public Property Ask1 As Integer = 0
    Public Property Bid1 As Integer = 0
    Public Property Strength As Double = 0
    Public Property MarketCap As Long = 0
    Public Property PER As Double = 0
    Public Property ListedShares As Long = 0
#End Region

#Region "State"
    Public Property State As DataReadyState = DataReadyState.None
    Public Property CandleCount As Integer = 0
    Public Property LastTickTime As DateTime = DateTime.MinValue
    Public Property IsRealtimeSubscribed As Boolean = False
    Public Property FilterPassed As Boolean = True
    Public Property FilterReason As String = ""

#End Region

#Region "Capture / Outcome"
    Public Property CaptureTimeStamp As DateTime = DateTime.MinValue
    Public Property CapturePrice As Integer = 0
    Public Property CaptureChangeRate As Double = 0
    Public Property HighestPriceAfterCapture As Integer = 0
    Public Property HighestRisePct As Double = 0
    Public Property LowestPriceAfterCapture As Integer = 0
    Public Property LowestDrawdownPct As Double = 0
#End Region

#Region "Indicator Snapshot"
    Public Property Ma120 As Double = 0
    Public Property Vwap As Double = 0
    Public Property SuperTrend As Double = 0
    Public Property Jma As Double = 0
    Public Property JmaPrev As Double = 0
    Public Property JmaSlope As Double = 0
    Public Property Tick5 As Double = 0
    Public Property Tick20 As Double = 0
    Public Property TickAccel As Double = 0
    Public Property Obv As Double = 0
    Public Property ObvSignal As Double = 0
    Public Property ObvSlope As Double = 0
    Public Property MacdHist As Double = 0
    Public Property MacdHistPrev As Double = 0
    Public Property MacdHistSlope As Double = 0
    Public Property ValueNow As Double = 0
    Public Property ValueAccel As Double = 0
    Public Property PriceAccel As Double = 0
#End Region

#Region "Selection / Ranking"
    Public Property BaseInvariantPassed As Boolean = False
    Public Property CandidateScore As Integer = 0
    Public Property EntryFitScore As Integer = 0
    Public Property FinalScore As Integer = 0
    Public Property ScoreRank As Integer = 0
    Public Property ChangeRateRank As Integer = 0
    Public Property HighestRiseRank As Integer = 0
    Public Property ScoreVsChangeRankGap As Integer = 0
    Public Property ScoreVsHighRankGap As Integer = 0
    Public Property IsTop10 As Boolean = False
    Public Property IsEntryCandidate As Boolean = False
    Public Property SelectionState As SelectionState = SelectionState.None
    Public Property HasReachedTargetProfit As Boolean = False
    Public Property IsReEntryBlocked As Boolean = False
    Public Property LastExitReason As String = ""
    Public Property LastScoreUpdateTimeStamp As DateTime = DateTime.MinValue
#End Region

#Region "Source Helpers"
    Public Sub AddSource(src As DataSourceType, Optional detail As String = "")
        Sources.Add(src)
        If detail <> "" Then
            If SourceDetail = "" Then
                SourceDetail = detail
            Else
                Dim exists As Boolean = False
                Dim parts() As String = SourceDetail.Split(","c)
                Dim i As Integer
                For i = 0 To parts.Length - 1
                    If String.Equals(parts(i).Trim(), detail.Trim(), StringComparison.OrdinalIgnoreCase) Then
                        exists = True
                        Exit For
                    End If
                Next
                If Not exists Then
                    SourceDetail &= "," & detail
                End If
            End If
        End If
    End Sub

    Public Function HasSource(src As DataSourceType) As Boolean
        Return Sources.Contains(src)
    End Function

    Public Function SourceText() As String
        Return String.Join("/", Sources.Select(Function(s) s.ToString()))
    End Function
#End Region

#Region "Capture Helpers"
    Public Sub EnsureCaptureBaseline()
        If CaptureTimeStamp = DateTime.MinValue Then
            CaptureTimeStamp = DateTime.Now
        End If
        If CapturePrice <= 0 AndAlso Price > 0 Then
            CapturePrice = Price
            CaptureChangeRate = ChangeRate
            HighestPriceAfterCapture = Price
            LowestPriceAfterCapture = Price
        End If
    End Sub

    Public Sub UpdateOutcomeTracking()
        If CapturePrice <= 0 Then Return
        If Price <= 0 Then Return

        If HighestPriceAfterCapture <= 0 OrElse Price > HighestPriceAfterCapture Then
            HighestPriceAfterCapture = Price
        End If
        If LowestPriceAfterCapture <= 0 OrElse Price < LowestPriceAfterCapture Then
            LowestPriceAfterCapture = Price
        End If

        HighestRisePct = ((HighestPriceAfterCapture / CDbl(CapturePrice)) - 1.0R) * 100.0R
        LowestDrawdownPct = ((LowestPriceAfterCapture / CDbl(CapturePrice)) - 1.0R) * 100.0R
    End Sub
#End Region

#Region "Indicator Helpers"
    Public Sub ApplyIndicatorSnapshot(indicators As Dictionary(Of String, Double))
        If indicators Is Nothing Then Return

        Ma120 = GV(indicators, "SMA120", "MA120", "SMA(120)")
        Vwap = GV(indicators, "VWAP", "VWAP.Value")
        SuperTrend = GV(indicators, "SuperTrend", "ST_10_3.0.Value", "SUPERTREND")

        JmaPrev = Jma
        Jma = GV(indicators, "JMA", "JMA.Value", "JMA(75,0,2)")
        If JmaPrev <> 0 Then
            JmaSlope = Jma - JmaPrev
        End If

        Tick5 = GV(indicators, "TickIntensity5", "TickIntensity.Ma5", "TICK5")
        Tick20 = GV(indicators, "TickIntensity20", "TickIntensity.Ma20", "TICK20")
        If Tick20 > 0 Then
            TickAccel = Tick5 / Tick20
        Else
            TickAccel = 0
        End If

        Obv = GV(indicators, "OBV", "OBV.Value")
        ObvSignal = GV(indicators, "OBVSignal", "OBV.Signal", "OBV_MA20")
        If ObvSignal <> 0 Then
            ObvSlope = Obv - ObvSignal
        Else
            ObvSlope = 0
        End If

        MacdHistPrev = MacdHist
        MacdHist = GV(indicators, "MACDHist", "MACD.Hist", "MACD_HIST")
        MacdHistSlope = MacdHist - MacdHistPrev

        ValueNow = GV(indicators, "ValueNow", "TURNOVER", "TRADEVALUE")
        ValueAccel = GV(indicators, "ValueAccel", "TURNOVER_ACCEL")
        PriceAccel = GV(indicators, "PriceAccel", "PRICE_ACCEL")

        LastScoreUpdateTimeStamp = DateTime.Now
    End Sub

    Private Shared Function GV(dict As Dictionary(Of String, Double), ParamArray keys() As String) As Double
        Dim v As Double = 0
        Dim i As Integer = 0
        For i = 0 To keys.Length - 1
            If dict.TryGetValue(keys(i), v) Then
                Return v
            End If
        Next

        Dim kvp As KeyValuePair(Of String, Double)
        For Each kvp In dict
            For i = 0 To keys.Length - 1
                If String.Equals(kvp.Key, keys(i), StringComparison.OrdinalIgnoreCase) Then
                    Return kvp.Value
                End If
            Next
        Next
        Return 0
    End Function
#End Region

#Region "Realtime Update"
    Public Sub UpdateFromTick(m As Msg)
        Dim p As Integer = Math.Abs(CInt(m.Dbl("price")))
        If p > 0 Then
            Price = p

            Dim pc As Integer = Math.Abs(CInt(m.Dbl("prevClose")))
            If pc > 0 AndAlso PrevClose = 0 Then
                PrevClose = pc
            End If
            If PrevClose > 0 Then
                Change = Price - PrevClose
                ChangeRate = (Change / CDbl(PrevClose)) * 100
            End If
        End If

        Dim v As Double = Math.Abs(m.Dbl("volume"))
        If v > 0 Then Volume += CLng(v)

        Dim o As Integer = Math.Abs(CInt(m.Dbl("open")))
        If o > 0 Then Open = o

        Dim h As Integer = Math.Abs(CInt(m.Dbl("high")))
        If h > 0 AndAlso h > High Then High = h

        Dim l As Integer = Math.Abs(CInt(m.Dbl("low")))
        If l > 0 AndAlso (Low = 0 OrElse l < Low) Then Low = l

        Dim a As Integer = Math.Abs(CInt(m.Dbl("ask1")))
        If a > 0 Then Ask1 = a

        Dim b As Integer = Math.Abs(CInt(m.Dbl("bid1")))
        If b > 0 Then Bid1 = b

        Dim st As Double = m.Dbl("strength")
        If st > 0 Then Strength = st

        Dim cv As Double = Math.Abs(m.Dbl("cumVolume"))
        If cv > 0 Then Volume = CLng(cv)

        LastTickTime = DateTime.Now
        EnsureCaptureBaseline()
        UpdateOutcomeTracking()
    End Sub
#End Region

#Region "Info Update"
    Public Sub UpdateFromInfo(row As Dictionary(Of String, Object))
        If row Is Nothing Then Return

        If row.ContainsKey("종목명") Then Name = row("종목명").ToString()
        If row.ContainsKey("name") Then Name = row("name").ToString()

        Dim p As Integer = SafeInt(row, "현재가", "price")
        If p > 0 Then Price = p

        Dim v As Long = SafeLong(row, "거래량", "volume")
        If v > 0 Then Volume = v

        Dim hi As Integer = SafeInt(row, "고가", "high")
        If hi > 0 Then High = hi

        Dim lo As Integer = SafeInt(row, "저가", "low")
        If lo > 0 Then Low = lo

        Dim op As Integer = SafeInt(row, "시가", "open")
        If op > 0 Then Open = op

        Dim a As Integer = SafeInt(row, "매도호가", "ask1")
        If a > 0 Then Ask1 = a

        Dim b As Integer = SafeInt(row, "매수호가", "bid1")
        If b > 0 Then Bid1 = b

        Dim st As Double = SafeDbl(row, "체결강도", "strength")
        If st > 0 Then Strength = st

        Dim pc As Integer = SafeInt(row, "전일종가", "prevClose")
        If pc > 0 Then PrevClose = pc

        Dim ch As Integer = SafeIntSigned(row, "전일대비", "change")
        If ch <> 0 Then Change = ch

        If PrevClose = 0 AndAlso Price > 0 AndAlso Change <> 0 Then
            PrevClose = Price - Change
        End If

        ChangeRate = SafeDblSigned(row, "등락율", "changeRate", "등락률")

        Dim mc As Long = SafeLong(row, "시가총액", "marketCap")
        If mc > 0 Then MarketCap = mc

        Dim perVal As Double = SafeDbl(row, "PER")
        If perVal <> 0 Then PER = perVal

        Dim ls As Long = SafeLong(row, "상장주식수", "listedShares")
        If ls > 0 Then ListedShares = ls

        State = DataReadyState.InfoLoaded
        EnsureCaptureBaseline()
        UpdateOutcomeTracking()
    End Sub
#End Region

#Region "Safe Parse"
    Private Shared Function SafeIntSigned(d As Dictionary(Of String, Object), ParamArray keys() As String) As Integer
        Dim i As Integer = 0
        For i = 0 To keys.Length - 1
            If d.ContainsKey(keys(i)) Then
                Dim valStr As String = If(d(keys(i)), "").ToString()
                If valStr <> "" Then
                    Dim v As Integer = 0
                    If Integer.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
                        Return v
                    End If
                End If
            End If
        Next
        Return 0
    End Function

    Private Shared Function SafeDblSigned(d As Dictionary(Of String, Object), ParamArray keys() As String) As Double
        Dim i As Integer = 0
        For i = 0 To keys.Length - 1
            If d.ContainsKey(keys(i)) Then
                Dim valStr As String = If(d(keys(i)), "").ToString()
                If valStr <> "" Then
                    Dim v As Double = 0
                    If Double.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
                        Return v
                    End If
                End If
            End If
        Next
        Return 0
    End Function

    Private Shared Function SafeInt(d As Dictionary(Of String, Object), ParamArray keys() As String) As Integer
        Dim i As Integer = 0
        For i = 0 To keys.Length - 1
            If d.ContainsKey(keys(i)) Then
                Dim valStr As String = If(d(keys(i)), "").ToString()
                If valStr <> "" Then
                    Dim v As Integer = 0
                    If Integer.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
                        Return Math.Abs(v)
                    End If
                End If
            End If
        Next
        Return 0
    End Function

    Private Shared Function SafeLong(d As Dictionary(Of String, Object), ParamArray keys() As String) As Long
        Dim i As Integer = 0
        For i = 0 To keys.Length - 1
            If d.ContainsKey(keys(i)) Then
                Dim valStr As String = If(d(keys(i)), "").ToString()
                If valStr <> "" Then
                    Dim v As Long = 0
                    If Long.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
                        Return Math.Abs(v)
                    End If
                End If
            End If
        Next
        Return 0
    End Function

    Private Shared Function SafeDbl(d As Dictionary(Of String, Object), ParamArray keys() As String) As Double
        Dim i As Integer = 0
        For i = 0 To keys.Length - 1
            If d.ContainsKey(keys(i)) Then
                Dim valStr As String = If(d(keys(i)), "").ToString()
                If valStr <> "" Then
                    Dim v As Double = 0
                    If Double.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
                        Return v
                    End If
                End If
            End If
        Next
        Return 0
    End Function
#End Region

End Class



'' ═══════════════════════════════════════════════════════════════
'' StockInfoItem.vb — 표준화된 종목정보 모델
'' ═══════════════════════════════════════════════════════════════
'' 모든 데이터소스에서 추출된 종목은 이 하나의 모델로 통일.
'' ═══════════════════════════════════════════════════════════════

'Imports [Shared]

'Public Enum DataSourceType
'    조건검색 = 0
'    주도섹터 = 1
'    프로그램순매수 = 2
'    관심종목 = 3
'    코스피추종 = 4
'    코스닥추종 = 5
'    수동추가 = 6
'    테마 = 7
'End Enum

'Public Enum DataReadyState
'    None = 0            ' 코드만 있음
'    InfoLoaded = 1      ' 기본정보 로드됨
'    CandleLoaded = 2    ' 캔들 다운로드 완료
'    RealtimeOn = 3      ' 실시간 구독 중
'    Ready = 4           ' 필터 통과, 매매 가능
'    Filtered = 5        ' 필터에서 제외됨
'End Enum

'Public Class StockInfoItem

'    ' ─── 식별 ───
'    Public Property Code As String = ""
'    Public Property Name As String = ""

'    ' ─── 데이터소스 ───
'    Public Property Sources As New HashSet(Of DataSourceType)()
'    Public Property SourceDetail As String = ""     ' 조건식명, 섹터명 등
'    Public Property AddedTime As DateTime = DateTime.Now

'    ' ─── 시세 (실시간 업데이트) ───
'    Public Property Price As Integer = 0
'    Public Property PrevClose As Integer = 0
'    Public Property Change As Integer = 0
'    Public Property ChangeRate As Double = 0
'    Public Property Volume As Long = 0
'    Public Property Open As Integer = 0
'    Public Property High As Integer = 0
'    Public Property Low As Integer = 0
'    Public Property Ask1 As Integer = 0
'    Public Property Bid1 As Integer = 0
'    Public Property Strength As Double = 0          ' 체결강도

'    ' ─── 기본정보 ───
'    Public Property MarketCap As Long = 0           ' 시가총액
'    Public Property PER As Double = 0
'    Public Property ListedShares As Long = 0

'    ' ─── 상태 ───
'    Public Property State As DataReadyState = DataReadyState.None
'    Public Property CandleCount As Integer = 0
'    Public Property LastTickTime As DateTime = DateTime.MinValue
'    Public Property IsRealtimeSubscribed As Boolean = False

'    ' ─── 필터 결과 ───
'    Public Property FilterPassed As Boolean = True
'    Public Property FilterReason As String = ""

'    ' ─── 소스 관리 ───

'    Public Sub AddSource(src As DataSourceType, Optional detail As String = "")
'        Sources.Add(src)
'        If detail <> "" Then
'            If SourceDetail = "" Then
'                SourceDetail = detail
'            ElseIf Not SourceDetail.Contains(detail) Then
'                SourceDetail &= "," & detail
'            End If
'        End If
'    End Sub

'    Public Function HasSource(src As DataSourceType) As Boolean
'        Return Sources.Contains(src)
'    End Function

'    Public Function SourceText() As String
'        Return String.Join("/", Sources.Select(Function(s) s.ToString()))
'    End Function

'    ''' <summary>실시간 틱 데이터로 업데이트</summary>
'    Public Sub UpdateFromTick(m As Msg)
'        Dim p = Math.Abs(CInt(m.Dbl("price")))
'        If p > 0 Then
'            Price = p

'            ' ★ 추가: 틱에서 prevClose가 제공되면 설정
'            Dim pc = Math.Abs(CInt(m.Dbl("prevClose")))
'            If pc > 0 AndAlso PrevClose = 0 Then PrevClose = pc

'            If PrevClose > 0 Then
'                Change = Price - PrevClose
'                ChangeRate = (Change / CDbl(PrevClose)) * 100
'            End If
'        End If

'        Dim v = Math.Abs(m.Dbl("volume"))
'        If v > 0 Then Volume += CLng(v)

'        Dim o = Math.Abs(CInt(m.Dbl("open"))) : If o > 0 Then Open = o
'        Dim h = Math.Abs(CInt(m.Dbl("high"))) : If h > 0 AndAlso h > High Then High = h
'        Dim l = Math.Abs(CInt(m.Dbl("low"))) : If l > 0 AndAlso (Low = 0 OrElse l < Low) Then Low = l
'        Dim a = Math.Abs(CInt(m.Dbl("ask1"))) : If a > 0 Then Ask1 = a
'        Dim b = Math.Abs(CInt(m.Dbl("bid1"))) : If b > 0 Then Bid1 = b
'        Dim st = m.Dbl("strength") : If st > 0 Then Strength = st

'        Dim cv = Math.Abs(m.Dbl("cumVolume")) : If cv > 0 Then Volume = CLng(cv)

'        LastTickTime = DateTime.Now
'    End Sub

'    ''' <summary>OPTKWFID / MarketEye 결과로 업데이트</summary>
'    Public Sub UpdateFromInfo(row As Dictionary(Of String, Object))
'        If row Is Nothing Then Return

'        If row.ContainsKey("종목명") Then Name = row("종목명").ToString()
'        If row.ContainsKey("name") Then Name = row("name").ToString()

'        Dim p = SafeInt(row, "현재가", "price") : If p > 0 Then Price = p
'        Dim v = SafeLong(row, "거래량", "volume") : If v > 0 Then Volume = v
'        Dim hi = SafeInt(row, "고가", "high") : If hi > 0 Then High = hi
'        Dim lo = SafeInt(row, "저가", "low") : If lo > 0 Then Low = lo
'        Dim op = SafeInt(row, "시가", "open") : If op > 0 Then Open = op
'        Dim a = SafeInt(row, "매도호가", "ask1") : If a > 0 Then Ask1 = a
'        Dim b = SafeInt(row, "매수호가", "bid1") : If b > 0 Then Bid1 = b
'        Dim st = SafeDbl(row, "체결강도", "strength") : If st > 0 Then Strength = st

'        ' ★ 전일종가를 직접 수신 (MarketEye 필드23)
'        Dim pc = SafeInt(row, "전일종가", "prevClose") : If pc > 0 Then PrevClose = pc

'        ' ★ 전일대비: 대비부호가 반영된 부호 있는 값
'        Dim ch = SafeIntSigned(row, "전일대비", "change")
'        If ch <> 0 Then Change = ch

'        ' PrevClose 역산 폴백
'        If PrevClose = 0 AndAlso Price > 0 AndAlso Change <> 0 Then
'            PrevClose = Price - Change
'        End If

'        ' ★ 등락률: DoMarketEye에서 이미 계산하여 보내줌
'        Dim cr = SafeDblSigned(row, "등락율", "changeRate", "등락률")
'        ChangeRate = cr

'        ' 추가 정보
'        Dim mc = SafeLong(row, "시가총액", "marketCap") : If mc > 0 Then MarketCap = mc
'        Dim per = SafeDbl(row, "PER") : If per <> 0 Then Me.PER = per
'        Dim ls = SafeLong(row, "상장주식수", "listedShares") : If ls > 0 Then ListedShares = ls

'        State = DataReadyState.InfoLoaded
'    End Sub

'    ''' <summary>부호를 보존하는 SafeInt (전일대비 등에 사용)</summary>
'    Private Shared Function SafeIntSigned(d As Dictionary(Of String, Object), ParamArray keys() As String) As Integer
'        For Each k In keys
'            If d.ContainsKey(k) Then
'                Dim valStr = d(k)?.ToString()
'                If Not String.IsNullOrEmpty(valStr) Then
'                    Dim v As Integer = 0
'                    If Integer.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
'                        Return v     ' ★ Math.Abs 제거! 부호 보존
'                    End If
'                End If
'            End If
'        Next
'        Return 0
'    End Function

'    ''' <summary>부호를 보존하는 SafeDbl</summary>
'    Private Shared Function SafeDblSigned(d As Dictionary(Of String, Object), ParamArray keys() As String) As Double
'        For Each k In keys
'            If d.ContainsKey(k) Then
'                Dim valStr = d(k)?.ToString()
'                If Not String.IsNullOrEmpty(valStr) Then
'                    Dim v As Double = 0
'                    If Double.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
'                        Return v     ' ★ 부호 보존
'                    End If
'                End If
'            End If
'        Next
'        Return 0
'    End Function


'    Private Shared Function SafeInt(d As Dictionary(Of String, Object), ParamArray keys() As String) As Integer
'        For Each k In keys
'            If d.ContainsKey(k) Then
'                Dim valStr = d(k)?.ToString()
'                If Not String.IsNullOrEmpty(valStr) Then
'                    Dim v As Integer = 0
'                    If Integer.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
'                        Return Math.Abs(v)
'                    End If
'                End If
'            End If
'        Next
'        Return 0
'    End Function

'    Private Shared Function SafeLong(d As Dictionary(Of String, Object), ParamArray keys() As String) As Long
'        For Each k In keys
'            If d.ContainsKey(k) Then
'                Dim valStr = d(k)?.ToString()
'                If Not String.IsNullOrEmpty(valStr) Then
'                    Dim v As Long = 0
'                    If Long.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
'                        Return Math.Abs(v)
'                    End If
'                End If
'            End If
'        Next
'        Return 0
'    End Function

'    Private Shared Function SafeDbl(d As Dictionary(Of String, Object), ParamArray keys() As String) As Double
'        For Each k In keys
'            If d.ContainsKey(k) Then
'                Dim valStr = d(k)?.ToString()
'                If Not String.IsNullOrEmpty(valStr) Then
'                    Dim v As Double = 0
'                    If Double.TryParse(valStr.Trim().Replace("+", "").Replace(",", ""), v) Then
'                        Return v
'                    End If
'                End If
'            End If
'        Next
'        Return 0
'    End Function

'End Class
