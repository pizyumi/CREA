Imports System.IO
Imports System.Text
Imports System.Security.Cryptography
Imports System.Runtime.CompilerServices

Module Module1

    Sub Main()

    End Sub

    Private haSha256 As HashAlgorithm = Nothing

    <Extension()> _
    Public Function ComputeSha256(bytes As Byte()) As Byte()
        If haSha256 Is Nothing Then
            haSha256 = HashAlgorithm.Create("SHA-256")
        End If

        Return haSha256.ComputeHash(bytes)
    End Function

    <Extension()> _
    Public Function Combine(Of T)(self As T(), ParamArray array As T()()) As T()
        Dim combined As T() = New T(self.Length + array.Sum(Function(a As T()) a.Length)) {}

        Dim index As Integer = 0
        For i As Integer = 0 To array.Length - 1
            For j As Integer = 0 To array(i).Length - 1
                combined(index) = array(i)(j)
                index += 1
            Next
        Next

        Return combined
    End Function

End Module