Option Strict On
Option Explicit On
Option Infer Off

Imports System

Public Class RangeSignalQualitySummary

    Public Property Code As String = ""
    Public Property Name As String = ""

    Public Property SignalCount As Integer
    Public Property FirstSignalTime As DateTime

    Public Property BestMFE10M As Double
    Public Property BestMFE30M As Double
    Public Property BestMFE60M As Double

    Public Property WorstMAE10M As Double
    Public Property AvgMAE10M As Double

    Public Property Target10Count As Integer
    Public Property Target30Count As Integer
    Public Property Target60Count As Integer

    Public Property BestExitReason As String = ""
    Public Property RiskFlags As String = ""

    Public Property LeaderScore As Double
    Public Property Rank As Integer

End Class
