Option Strict On
Option Explicit On
Option Infer Off

Imports System

Public Class SafeChartState
    Public Property StartIndex As Integer = 0
    Public Property VisibleCount As Integer = 100
    Public Property CandleWidth As Single = 8.0F
    Public Property Gap As Single = 2.0F

    Public Property AutoScaleY As Boolean = True
    Public Property ManualPriceHigh As Single = 0.0F
    Public Property ManualPriceLow As Single = 0.0F

    Public Property MouseInside As Boolean = False
    Public Property MouseX As Single = 0.0F
    Public Property MouseY As Single = 0.0F

    Public Property IsDragging As Boolean = False
    Public Property DragStartX As Integer = 0
    Public Property DragStartIndex As Integer = 0

    Public Function EndIndex(totalCount As Integer) As Integer
        If totalCount <= 0 Then Return -1
        Return Math.Min(totalCount - 1, StartIndex + Math.Max(1, VisibleCount) - 1)
    End Function

    Public Sub Clamp(totalCount As Integer)
        If totalCount <= 0 Then
            StartIndex = 0
            Return
        End If

        If VisibleCount < 5 Then VisibleCount = 5
        If VisibleCount > totalCount Then VisibleCount = totalCount

        Dim maxStart As Integer = Math.Max(0, totalCount - VisibleCount)
        If StartIndex < 0 Then StartIndex = 0
        If StartIndex > maxStart Then StartIndex = maxStart
    End Sub
End Class
