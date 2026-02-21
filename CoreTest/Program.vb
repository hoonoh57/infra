Imports System
Imports System.Collections.Generic
Imports [Shared]

Module Program
    Sub Main()
        Console.WriteLine("=== Phase 1 Core Test ===")
        
        ' 1. Msg & MessageBus Test
        Dim received As Msg = Nothing
        MessageBus.I.On(Topics.SYS_ERROR, Sub(m) received = m)
        MessageBus.I.On("test.topic", Sub(m) received = m)
        
        MessageBus.I.Emit("test.topic", "hello", "world", "value", 123, "price", 456.78)
        
        If received IsNot Nothing AndAlso received.Str("hello") = "world" AndAlso received.Int("value") = 123 Then
            Console.WriteLine("[PASS] MessageBus Emit -> Receive")
        Else
            Console.WriteLine("[FAIL] MessageBus Emit -> Receive")
        End If

        ' 2. SimpleSerializer Test (Roundtrip)
        Dim original As New Msg("test.topic")
        original("str_val") = "test_string"
        original("int_val") = 999
        original("dbl_val") = 3.14
        original("bool_val") = True
        
        Dim serializedBytes = SimpleSerializer.Serialize(original)
        Dim deserialized = SimpleSerializer.Deserialize(serializedBytes)
        
        If deserialized("str_val").ToString() = "test_string" AndAlso Convert.ToInt32(deserialized("int_val")) = 999 Then
            Console.WriteLine("[PASS] SimpleSerializer Serialize -> Deserialize")
        Else
            Console.WriteLine("[FAIL] SimpleSerializer Serialize -> Deserialize")
        End If
        
        Console.WriteLine("Phase 1 Tests Completed.")
    End Sub
End Module
