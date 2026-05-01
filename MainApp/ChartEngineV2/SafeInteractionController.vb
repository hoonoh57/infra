Option Strict On
Option Explicit On
Option Infer Off

Imports System
Imports System.Windows.Forms

Public Class SafeInteractionController
    Private Const RIGHT_DRAG_PADDING_BARS As Integer = 12

    Private ReadOnly _state As SafeChartState
    Private ReadOnly _invalidate As Action

    Public Sub New(state As SafeChartState, invalidateAction As Action)
        _state = state
        _invalidate = invalidateAction
    End Sub

    Public Sub OnMouseDown(e As MouseEventArgs,
                           totalCount As Integer,
                           chartRight As Single,
                           priceHigh As Single,
                           priceLow As Single)
        If e Is Nothing Then Return

        _state.MouseInside = True
        _state.MouseX = e.X
        _state.MouseY = e.Y

        If e.Button = MouseButtons.Left Then
            If e.X >= CInt(chartRight) Then
                _state.IsDraggingPrice = True
                _state.IsDragging = False
                _state.DragStartY = e.Y
                _state.DragStartPriceHigh = priceHigh
                _state.DragStartPriceLow = priceLow
                _state.AutoScaleY = False
            Else
                _state.IsDragging = True
                _state.IsDraggingPrice = False
                _state.DragStartX = e.X
                _state.DragStartIndex = _state.StartIndex
            End If
        End If

        If _invalidate IsNot Nothing Then _invalidate.Invoke()
    End Sub

    Public Sub OnMouseUp(e As MouseEventArgs)
        _state.IsDragging = False
        _state.IsDraggingPrice = False
        If _invalidate IsNot Nothing Then _invalidate.Invoke()
    End Sub

    Public Sub OnMouseMove(e As MouseEventArgs, totalCount As Integer)
        If e Is Nothing Then Return

        _state.MouseInside = True
        _state.MouseX = e.X
        _state.MouseY = e.Y

        If _state.IsDraggingPrice Then
            ApplyPriceDrag(e.Y)
        ElseIf _state.IsDragging Then
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
        If Not _state.IsDragging AndAlso Not _state.IsDraggingPrice Then
            If _invalidate IsNot Nothing Then _invalidate.Invoke()
        End If
    End Sub

    Public Sub OnMouseWheel(e As MouseEventArgs, totalCount As Integer)
        If e Is Nothing OrElse totalCount <= 0 Then Return

        Dim oldVisible As Integer = Math.Max(1, _state.VisibleCount)
        Dim newVisible As Integer = oldVisible

        If e.Delta > 0 Then
            newVisible = Math.Max(10, CInt(Math.Round(oldVisible * 0.85R)))
        Else
            newVisible = Math.Min(totalCount, CInt(Math.Round(oldVisible * 1.15R)) + 1)
        End If

        If newVisible = oldVisible Then Return

        Dim oldLocal As Double = 0.0R
        Dim unit As Double = CDbl(Math.Max(1.0F, _state.CandleWidth + _state.Gap))
        oldLocal = CDbl(e.X) / unit
        If oldLocal < 0.0R Then oldLocal = 0.0R
        If oldLocal > CDbl(oldVisible - 1) Then oldLocal = CDbl(oldVisible - 1)

        Dim centerIndex As Double = CDbl(_state.StartIndex) + oldLocal
        Dim newLocal As Double = oldLocal * (CDbl(newVisible) / CDbl(oldVisible))

        _state.VisibleCount = newVisible
        _state.StartIndex = CInt(Math.Round(centerIndex - newLocal))
        _state.Clamp(totalCount)

        If _invalidate IsNot Nothing Then _invalidate.Invoke()
    End Sub

    Public Sub ResetPriceScale()
        _state.ResetManualPriceScale()
        If _invalidate IsNot Nothing Then _invalidate.Invoke()
    End Sub

    Public Sub MoveToLatest(totalCount As Integer)
        If totalCount <= 0 Then Return
        Dim latestStart As Integer = Math.Max(0, totalCount - Math.Max(1, _state.VisibleCount))
        _state.StartIndex = latestStart + Math.Min(RIGHT_DRAG_PADDING_BARS, Math.Max(3, CInt(Math.Round(_state.VisibleCount * 0.08R))))
        _state.Clamp(totalCount)
        If _invalidate IsNot Nothing Then _invalidate.Invoke()
    End Sub

    Private Sub ApplyPriceDrag(currentY As Integer)
        Dim oldHigh As Single = _state.DragStartPriceHigh
        Dim oldLow As Single = _state.DragStartPriceLow
        If oldHigh <= oldLow Then Return

        Dim range As Single = oldHigh - oldLow
        Dim dy As Integer = currentY - _state.DragStartY
        Dim factor As Double = Math.Exp(CDbl(dy) / 180.0R)
        If factor < 0.15R Then factor = 0.15R
        If factor > 8.0R Then factor = 8.0R

        Dim mid As Single = (oldHigh + oldLow) / 2.0F
        Dim newRange As Single = CSng(CDbl(range) * factor)
        If newRange <= 0.0F Then Return

        _state.ManualPriceHigh = mid + newRange / 2.0F
        _state.ManualPriceLow = mid - newRange / 2.0F
        _state.AutoScaleY = False
    End Sub
End Class
