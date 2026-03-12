Imports System
Imports System.Windows.Forms
Imports StrategyCore.Services

Namespace StrategyLabApp
    Module Program
        <STAThread>
        Sub Main()
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)

            Dim candleProvider As New PipeMarketCandleProvider()
            Dim labFacade As New StrategyLabFacade(candleProvider)
                Dim args = Environment.GetCommandLineArgs()
                If args IsNot Nothing AndAlso Array.Exists(args, Function(arg) String.Equals(arg, "--smoke-test", StringComparison.OrdinalIgnoreCase)) Then
                    Using form As New StrategyLabForm(labFacade)
                        form.RunSmokeTest()
                    End Using
                    Return
                End If

            Application.Run(New StrategyLabForm(labFacade))
        End Sub
    End Module
End Namespace
