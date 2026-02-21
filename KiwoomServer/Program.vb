' ═══════════════════════════════════════════════════════════════
' KiwoomServer/Program.vb — 진입점
' ═══════════════════════════════════════════════════════════════

Imports System.Windows.Forms

Module Program
    Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New ServerMain())
    End Sub
End Module
