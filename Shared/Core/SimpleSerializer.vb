' ═══════════════════════════════════════════════════════════════
' SimpleSerializer.vb — 바이너리 직렬화/역직렬화
' ═══════════════════════════════════════════════════════════════
' 수정 금지. Named Pipe를 통해 Msg를 바이트로 교환.
' 지원 타입: String, Integer, Long, Single, Double, Boolean,
'            DateTime, Byte(), String(), Integer(), Single(),
'            Double(), Long(), Dictionary(Of String, String),
'            List(Of Dictionary(Of String, String))
' ═══════════════════════════════════════════════════════════════

Imports System.IO
Imports System.Text

Public Class SimpleSerializer

    ' ─── 타입 코드 ───
    Private Const T_NULL As Byte = 0
    Private Const T_STRING As Byte = 1
    Private Const T_INT32 As Byte = 2
    Private Const T_INT64 As Byte = 3
    Private Const T_SINGLE As Byte = 4
    Private Const T_DOUBLE As Byte = 5
    Private Const T_BOOLEAN As Byte = 6
    Private Const T_DATETIME As Byte = 7
    Private Const T_BYTES As Byte = 8
    Private Const T_STRING_ARRAY As Byte = 10
    Private Const T_INT_ARRAY As Byte = 11
    Private Const T_SINGLE_ARRAY As Byte = 12
    Private Const T_DOUBLE_ARRAY As Byte = 13
    Private Const T_LONG_ARRAY As Byte = 14
    Private Const T_DICT_SS As Byte = 20        ' Dictionary(Of String, String)
    Private Const T_LIST_DICT_SS As Byte = 21   ' List(Of Dictionary(Of String, String))

    Public Shared Function Serialize(msg As Msg) As Byte()
        Using ms As New MemoryStream()
            Using bw As New BinaryWriter(ms, Encoding.UTF8)
                ' 1) Topic
                WriteString(bw, msg.Topic)

                ' 2) Key count
                bw.Write(CInt(msg.Count))

                ' 3) Each key-value
                For Each kv In msg
                    WriteString(bw, kv.Key)
                    WriteValue(bw, kv.Value)
                Next
            End Using
            Return ms.ToArray()
        End Using
    End Function

    Public Shared Function Deserialize(data As Byte()) As Msg
        Using ms As New MemoryStream(data)
            Using br As New BinaryReader(ms, Encoding.UTF8)
                Dim topic = ReadString(br)
                Dim m As New Msg(topic)

                Dim count = br.ReadInt32()
                For i = 0 To count - 1
                    Dim key = ReadString(br)
                    Dim value = ReadValue(br)
                    m(key) = value
                Next

                Return m
            End Using
        End Using
    End Function

    ' ─── 내부: 문자열 ───

    Private Shared Sub WriteString(bw As BinaryWriter, s As String)
        If s Is Nothing Then
            bw.Write(CInt(-1))
        Else
            Dim bytes = Encoding.UTF8.GetBytes(s)
            bw.Write(bytes.Length)
            bw.Write(bytes)
        End If
    End Sub

    Private Shared Function ReadString(br As BinaryReader) As String
        Dim len = br.ReadInt32()
        If len < 0 Then Return Nothing
        If len = 0 Then Return ""
        Dim bytes = br.ReadBytes(len)
        Return Encoding.UTF8.GetString(bytes)
    End Function

    ' ─── 내부: 값 ───

    Private Shared Sub WriteValue(bw As BinaryWriter, v As Object)
        If v Is Nothing Then
            bw.Write(T_NULL)
            Return
        End If

        Select Case True
            Case TypeOf v Is String
                bw.Write(T_STRING)
                WriteString(bw, CStr(v))

            Case TypeOf v Is Integer
                bw.Write(T_INT32)
                bw.Write(CInt(v))

            Case TypeOf v Is Long
                bw.Write(T_INT64)
                bw.Write(CLng(v))

            Case TypeOf v Is Single
                bw.Write(T_SINGLE)
                bw.Write(CSng(v))

            Case TypeOf v Is Double
                bw.Write(T_DOUBLE)
                bw.Write(CDbl(v))

            Case TypeOf v Is Boolean
                bw.Write(T_BOOLEAN)
                bw.Write(CBool(v))

            Case TypeOf v Is DateTime
                bw.Write(T_DATETIME)
                bw.Write(CDate(v).Ticks)

            Case TypeOf v Is Byte()
                bw.Write(T_BYTES)
                Dim arr = DirectCast(v, Byte())
                bw.Write(arr.Length)
                bw.Write(arr)

            Case TypeOf v Is String()
                bw.Write(T_STRING_ARRAY)
                Dim arr = DirectCast(v, String())
                bw.Write(arr.Length)
                For Each s In arr
                    WriteString(bw, s)
                Next

            Case TypeOf v Is Integer()
                bw.Write(T_INT_ARRAY)
                Dim arr = DirectCast(v, Integer())
                bw.Write(arr.Length)
                For Each n In arr
                    bw.Write(n)
                Next

            Case TypeOf v Is Single()
                bw.Write(T_SINGLE_ARRAY)
                Dim arr = DirectCast(v, Single())
                bw.Write(arr.Length)
                For Each n In arr
                    bw.Write(n)
                Next

            Case TypeOf v Is Double()
                bw.Write(T_DOUBLE_ARRAY)
                Dim arr = DirectCast(v, Double())
                bw.Write(arr.Length)
                For Each n In arr
                    bw.Write(n)
                Next

            Case TypeOf v Is Long()
                bw.Write(T_LONG_ARRAY)
                Dim arr = DirectCast(v, Long())
                bw.Write(arr.Length)
                For Each n In arr
                    bw.Write(n)
                Next

            Case TypeOf v Is Dictionary(Of String, String)
                bw.Write(T_DICT_SS)
                Dim d = DirectCast(v, Dictionary(Of String, String))
                bw.Write(d.Count)
                For Each kv In d
                    WriteString(bw, kv.Key)
                    WriteString(bw, kv.Value)
                Next

            Case TypeOf v Is List(Of Dictionary(Of String, String))
                bw.Write(T_LIST_DICT_SS)
                Dim lst = DirectCast(v, List(Of Dictionary(Of String, String)))
                bw.Write(lst.Count)
                For Each d In lst
                    bw.Write(d.Count)
                    For Each kv In d
                        WriteString(bw, kv.Key)
                        WriteString(bw, kv.Value)
                    Next
                Next

            Case Else
                ' 미지원 타입 → 문자열로 변환
                bw.Write(T_STRING)
                WriteString(bw, v.ToString())
        End Select
    End Sub

    Private Shared Function ReadValue(br As BinaryReader) As Object
        Dim typeCode = br.ReadByte()

        Select Case typeCode
            Case T_NULL
                Return Nothing

            Case T_STRING
                Return ReadString(br)

            Case T_INT32
                Return br.ReadInt32()

            Case T_INT64
                Return br.ReadInt64()

            Case T_SINGLE
                Return br.ReadSingle()

            Case T_DOUBLE
                Return br.ReadDouble()

            Case T_BOOLEAN
                Return br.ReadBoolean()

            Case T_DATETIME
                Return New DateTime(br.ReadInt64())

            Case T_BYTES
                Dim len = br.ReadInt32()
                Return br.ReadBytes(len)

            Case T_STRING_ARRAY
                Dim len = br.ReadInt32()
                Dim arr(len - 1) As String
                For i = 0 To len - 1
                    arr(i) = ReadString(br)
                Next
                Return arr

            Case T_INT_ARRAY
                Dim len = br.ReadInt32()
                Dim arr(len - 1) As Integer
                For i = 0 To len - 1
                    arr(i) = br.ReadInt32()
                Next
                Return arr

            Case T_SINGLE_ARRAY
                Dim len = br.ReadInt32()
                Dim arr(len - 1) As Single
                For i = 0 To len - 1
                    arr(i) = br.ReadSingle()
                Next
                Return arr

            Case T_DOUBLE_ARRAY
                Dim len = br.ReadInt32()
                Dim arr(len - 1) As Double
                For i = 0 To len - 1
                    arr(i) = br.ReadDouble()
                Next
                Return arr

            Case T_LONG_ARRAY
                Dim len = br.ReadInt32()
                Dim arr(len - 1) As Long
                For i = 0 To len - 1
                    arr(i) = br.ReadInt64()
                Next
                Return arr

            Case T_DICT_SS
                Dim cnt = br.ReadInt32()
                Dim d As New Dictionary(Of String, String)(cnt, StringComparer.OrdinalIgnoreCase)
                For i = 0 To cnt - 1
                    d(ReadString(br)) = ReadString(br)
                Next
                Return d

            Case T_LIST_DICT_SS
                Dim listCnt = br.ReadInt32()
                Dim lst As New List(Of Dictionary(Of String, String))(listCnt)
                For i = 0 To listCnt - 1
                    Dim dictCnt = br.ReadInt32()
                    Dim d As New Dictionary(Of String, String)(dictCnt, StringComparer.OrdinalIgnoreCase)
                    For j = 0 To dictCnt - 1
                        d(ReadString(br)) = ReadString(br)
                    Next
                    lst.Add(d)
                Next
                Return lst

            Case Else
                Return Nothing
        End Select
    End Function
End Class
