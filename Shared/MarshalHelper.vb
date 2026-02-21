Imports System.Runtime.InteropServices

Public Module MarshalHelper

    Public Function StructToBytes(Of T As Structure)(value As T) As Byte()
        Dim size = Marshal.SizeOf(Of T)()
        Dim bytes(size - 1) As Byte
        Dim ptr = Marshal.AllocHGlobal(size)
        Try
            Marshal.StructureToPtr(value, ptr, False)
            Marshal.Copy(ptr, bytes, 0, size)
        Finally
            Marshal.FreeHGlobal(ptr)
        End Try
        Return bytes
    End Function

    Public Function BytesToStruct(Of T As Structure)(bytes As Byte()) As T
        Dim size = Marshal.SizeOf(Of T)()
        Dim ptr = Marshal.AllocHGlobal(size)
        Try
            Marshal.Copy(bytes, 0, ptr, size)
            Return Marshal.PtrToStructure(Of T)(ptr)
        Finally
            Marshal.FreeHGlobal(ptr)
        End Try
    End Function

End Module
