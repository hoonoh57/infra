' ═══════════════════════════════════════════════════════════════
' IChartHost.vb — 차트 호스트 인터페이스
' ═══════════════════════════════════════════════════════════════

''' <summary>
''' 차트 컨트롤이 데이터를 요청할 때 사용하는 호스트 인터페이스.
''' 폼이나 매니저가 구현하여 차트에 주입한다.
''' </summary>
Public Interface IChartHost
    Function GetStockName(stockCode As String) As String
    Sub RequestCandles(stockCode As String, chartType As String, count As Integer)
    Sub SubscribeRealtime(stockCode As String)
    Sub UnsubscribeRealtime(stockCode As String)
End Interface
