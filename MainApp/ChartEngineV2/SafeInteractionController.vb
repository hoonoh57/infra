Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Windows.Forms

Public Class SafeInteractionController
    Private ReadOnly _state As SafeChartState
    Private ReadOnly _invalidate As Action

    Public Sub New(state As SafeChartState, invalidateAction As Action)
        _state = state
        _invalidate = invalidateAction
    End Sub

    Public Sub OnMouseDown(e As MouseEventArgs)
        If e.Button = MouseButtons.Left Then
            _state.IsDragging = True
            _state.DragStartX = e.X
            _state.DragStartIndex = _state.StartIndex
        End If
    End Sub

    Public Sub OnMouseUp(e As MouseEventArgs)
        _state.IsDragging = False
    End Sub

    Public Sub OnMouseMove(e As MouseEventArgs, totalCount As Integer)
        _state.MouseInside = True
        _state.MouseX = e.X
        _state.MouseY = e.Y

        If _state.IsDragging Then
            Dim unit As Single = Math.Max(1.0F, _state.CandleWidth + _state.Gap)
            Dim dx As Integer = e.X - _state.DragStartX
            Dim shift As Integer = CInt(Math.Round(-dx / unit))
            _state.StartIndex = _state.DragStartIndex + shift
            _state.Clamp(totalCount)
        End If

        If _invalidate IsNot Nothing Then _invalidate.Invoke()
    End Sub

    Public Sub OnMouseLeave()
        _state.MouseInside = False
        If _invalidate IsNot Nothing Then _invalidate.Invoke()
    End Sub

    Public Sub OnMouseWheel(e As MouseEventArgs, totalCount As Integer)
        If totalCount <= 0 Then Return

        Dim oldVisible As Integer = _state.VisibleCount
        If e.Delta > 0 Then
            _state.VisibleCount = Math.Max(10, CInt(_state.VisibleCount * 0.85R))
        Else
            _state.VisibleCount = Math.Min(totalCount, CInt(_state.VisibleCount * 1.15R) + 1)
        End If

        If oldVisible <> _state.VisibleCount Then
            _state.Clamp(totalCount)
            If _invalidate IsNot Nothing Then _invalidate.Invoke()
        End If
    End Sub
End Class
