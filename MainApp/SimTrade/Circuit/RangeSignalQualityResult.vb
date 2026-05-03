Option Strict On
Option Explicit On
Option Infer Off

Imports System

Public Class RangeSignalQualityResult

    Public Property Code As String = ""
    Public Property Name As String = ""

    Public Property EntryIndex As Integer
    Public Property EntryTime As DateTime
    Public Property EntryPrice As Double

    Public Property Seq As Integer
    Public Property PrevGap As Integer

    Public Property OpenPct As Double
    Public Property LowPct As Double
    Public Property HighGapPct As Double

    Public Property Tick As Double
    Public Property TickMA5 As Double
    Public Property TickMA20 As Double
    Public Property TickVsMA5 As Double
    Public Property TickVsMA20 As Double

    Public Property MFE10M As Double
    Public Property MFE30M As Double
    Public Property MFE60M As Double

    Public Property MAE10M As Double
    Public Property MAE30M As Double
    Public Property MAE60M As Double

    Public Property T10 As Boolean
    Public Property T30 As Boolean
    Public Property T60 As Boolean

    Public Property ExitReason As String = ""
    Public Property RealizedPct As Double
    Public Property HoldMin As Double

    Public Property BanAfterExit As Boolean
    Public Property RiskFlags As String = ""

    Public Property LeaderScore As Double

End Class
