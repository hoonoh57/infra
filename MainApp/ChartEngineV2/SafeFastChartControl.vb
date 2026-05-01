Option Strict On
Option Explicit On
Option Infer Off

' SafeFastChartControl
' V2 검증 단계에서는 원본 FastChartControl의 검증된 이벤트/레이아웃/메뉴/전략/지표 로직을 그대로 승계한다.
' 기존 V2 임시 렌더/마우스/패널 로직은 원본 안정 로직과 충돌하므로 폐기한다.
' 향후 원본 FastChartControl 폐기 시 이 클래스명을 기준으로 파일명/클래스명을 정리하면 된다.

Public Class SafeFastChartControl
    Inherits FastChartControl

    Public Sub New()
        MyBase.New()
    End Sub

End Class
