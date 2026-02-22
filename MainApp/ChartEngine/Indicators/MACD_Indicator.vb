' MACD_Indicator.vb — MACD (12,26,9)

Public Class MACD_Indicator
    Implements IIndicator

    Private _fast As Integer = 12
    Private _slow As Integer = 26
    Private _signal As Integer = 9
    Private _params As New Dictionary(Of String, Object) From {{"Fast", 12}, {"Slow", 26}, {"Signal", 9}}
    Private _emaFastVal As Single = Single.NaN
    Private _emaSlowVal As Single = Single.NaN
    Private _emaSignalVal As Single = Single.NaN
    Private _prevEmaFastVal As Single = Single.NaN
    Private _prevEmaSlowVal As Single = Single.NaN
    Private _prevEmaSignalVal As Single = Single.NaN
    Private _stateIndex As Integer = -1

    Public Sub New(Optional fast As Integer = 12, Optional slow As Integer = 26, Optional signal As Integer = 9)
        _fast = fast
        _slow = slow
        _signal = signal
        _params("Fast") = _fast
        _params("Slow") = _slow
        _params("Signal") = _signal
    End Sub

    Public ReadOnly Property Name As String Implements IIndicator.Name
        Get
            Return $"MACD_{_fast}_{_slow}_{_signal}"
        End Get
    End Property
    Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
        Get
            Return $"MACD({_fast},{_slow},{_signal})"
        End Get
    End Property
    Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
        Get
            Return 2
        End Get
    End Property
    Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
        Get
            Return _params
        End Get
        Set(value As Dictionary(Of String, Object))
            _params = value
            If _params.ContainsKey("Fast") Then _fast = CInt(_params("Fast"))
            If _params.ContainsKey("Slow") Then _slow = CInt(_params("Slow"))
            If _params.ContainsKey("Signal") Then _signal = CInt(_params("Signal"))
        End Set
    End Property

    Public Function Calculate(candles As List(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
        Dim count = candles.Count
        Dim results As New List(Of IndicatorResult)(count)
        Dim emaF = CalcEMA(candles, _fast)
        Dim emaS = CalcEMA(candles, _slow)
        Dim macdLine(count - 1) As Single
        For i = 0 To count - 1
            If Single.IsNaN(emaF(i)) OrElse Single.IsNaN(emaS(i)) Then
                macdLine(i) = Single.NaN
            Else
                macdLine(i) = emaF(i) - emaS(i)
            End If
        Next
        Dim signalLine = CalcEMAFromValues(macdLine, _signal)
        If count > 0 Then
            _emaFastVal = emaF(count - 1)
            _emaSlowVal = emaS(count - 1)
            _emaSignalVal = signalLine(count - 1)
            _stateIndex = count - 1
            If count > 1 Then
                _prevEmaFastVal = emaF(count - 2)
                _prevEmaSlowVal = emaS(count - 2)
                _prevEmaSignalVal = signalLine(count - 2)
            Else
                _prevEmaFastVal = Single.NaN
                _prevEmaSlowVal = Single.NaN
                _prevEmaSignalVal = Single.NaN
            End If
        End If
        For i = 0 To count - 1
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 2,
                .Values = New Dictionary(Of String, Single)}
            r.Values("MACD") = macdLine(i)
            r.Values("Signal") = signalLine(i)
            If Single.IsNaN(macdLine(i)) OrElse Single.IsNaN(signalLine(i)) Then
                r.Values("Histogram") = Single.NaN
            Else
                r.Values("Histogram") = macdLine(i) - signalLine(i)
            End If
            results.Add(r)
        Next
        Return results
    End Function

    Public Function UpdateLast(candles As List(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
        Dim i = candles.Count - 1
        Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 2,
            .Values = New Dictionary(Of String, Single)}
        If i < _slow OrElse Single.IsNaN(_emaFastVal) Then
            Dim full = Calculate(candles)
            If full.Count > 0 Then
                Return full(full.Count - 1)
            End If
            r.Values("MACD") = Single.NaN
            r.Values("Signal") = Single.NaN
            r.Values("Histogram") = Single.NaN
            Return r
        End If
        If i <= 0 Then
            Dim full = Calculate(candles)
            If full.Count > 0 Then Return full(full.Count - 1)
            r.Values("MACD") = Single.NaN
            r.Values("Signal") = Single.NaN
            r.Values("Histogram") = Single.NaN
            Return r
        End If
        Dim price = candles(i).Close
        Dim kF As Single = 2.0F / (_fast + 1)
        Dim kS As Single = 2.0F / (_slow + 1)
        Dim kSig As Single = 2.0F / (_signal + 1)

        Dim baseEF As Single
        Dim baseES As Single
        Dim baseESig As Single
        If _stateIndex = i Then
            baseEF = _prevEmaFastVal
            baseES = _prevEmaSlowVal
            baseESig = _prevEmaSignalVal
        ElseIf _stateIndex = i - 1 Then
            baseEF = _emaFastVal
            baseES = _emaSlowVal
            baseESig = _emaSignalVal
            _prevEmaFastVal = baseEF
            _prevEmaSlowVal = baseES
            _prevEmaSignalVal = baseESig
        Else
            Dim full = Calculate(candles)
            If full.Count > 0 Then Return full(full.Count - 1)
            r.Values("MACD") = Single.NaN
            r.Values("Signal") = Single.NaN
            r.Values("Histogram") = Single.NaN
            Return r
        End If
        If Single.IsNaN(baseEF) OrElse Single.IsNaN(baseES) OrElse Single.IsNaN(baseESig) Then
            Dim full = Calculate(candles)
            If full.Count > 0 Then Return full(full.Count - 1)
            r.Values("MACD") = Single.NaN
            r.Values("Signal") = Single.NaN
            r.Values("Histogram") = Single.NaN
            Return r
        End If

        Dim curEF = price * kF + baseEF * (1 - kF)
        Dim curES = price * kS + baseES * (1 - kS)
        Dim curMacd = curEF - curES
        Dim curSignal = curMacd * kSig + baseESig * (1 - kSig)
        _emaFastVal = curEF
        _emaSlowVal = curES
        _emaSignalVal = curSignal
        _stateIndex = i
        r.Values("MACD") = curMacd
        r.Values("Signal") = curSignal
        r.Values("Histogram") = curMacd - curSignal
        Return r
    End Function

    Private Shared Function CalcEMA(candles As List(Of CandleItem), period As Integer) As Single()
        Dim count = candles.Count
        Dim result(count - 1) As Single
        Dim k As Single = 2.0F / (period + 1)
        For i = 0 To count - 1
            If i < period - 1 Then
                result(i) = Single.NaN
            ElseIf i = period - 1 Then
                Dim s As Single = 0
                For j = 0 To period - 1
                    s += candles(j).Close
                Next
                result(i) = s / period
            Else
                result(i) = candles(i).Close * k + result(i - 1) * (1 - k)
            End If
        Next
        Return result
    End Function

    Private Shared Function CalcEMAFromValues(values As Single(), period As Integer) As Single()
        Dim count = values.Length
        Dim result(count - 1) As Single
        Dim k As Single = 2.0F / (period + 1)
        Dim firstValid = -1
        For i = 0 To count - 1
            If Not Single.IsNaN(values(i)) Then
                firstValid = i
                Exit For
            End If
        Next
        If firstValid < 0 Then
            For i = 0 To count - 1
                result(i) = Single.NaN
            Next
            Return result
        End If
        Dim validCount = 0
        Dim sum As Single = 0
        For i = 0 To count - 1
            If i < firstValid OrElse Single.IsNaN(values(i)) Then
                result(i) = Single.NaN
                Continue For
            End If
            validCount += 1
            If validCount < period Then
                sum += values(i)
                result(i) = Single.NaN
            ElseIf validCount = period Then
                sum += values(i)
                result(i) = sum / period
            Else
                result(i) = values(i) * k + result(i - 1) * (1 - k)
            End If
        Next
        Return result
    End Function
End Class
