' ═══════════════════════════════════════════════════════════════
' StrategySignal.vb — 전략 매매 신호 모델
' ═══════════════════════════════════════════════════════════════

''' <summary>매매 신호 타입</summary>
Public Enum SignalType
    None = 0
    Buy = 1
    Sell = 2
    StrongBuy = 3
    StrongSell = 4
End Enum

''' <summary>전략이 발생시킨 매매 신호</summary>
Public Class StrategySignal
    Public Property StockCode As String = ""
    Public Property StrategyName As String = ""
    Public Property SignalType As SignalType = SignalType.None
    Public Property Price As Single = 0
    Public Property Reason As String = ""
    Public Property Confidence As Single = 0   ' 0~1
    Public Property Timestamp As DateTime = DateTime.Now

    Public Overrides Function ToString() As String
        Return $"[{SignalType}] {StockCode} @{Price:N0} by {StrategyName} ({Reason})"
    End Function
End Class
