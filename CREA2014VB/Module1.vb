Imports System.IO
Imports System.Text
Imports System.Security.Cryptography

Module Module1

    Sub Main()

    End Sub

End Module




Public MustInherit Class DATA
End Class

Public MustInherit Class INTERNALDATA
    Inherits DATA
End Class

Public MustInherit Class STREAMDATA(Of T As STREAMDATA(Of T).StreamInfomation)
    Inherits DATA

    Public MustInherit Class STREAMWRITER
        Public MustOverride Sub WriteBytes(data As Byte(), length As Integer?)
        Public MustOverride Sub WriteBool(data As Boolean)
        Public MustOverride Sub WriteInt(data As Integer)
        Public MustOverride Sub WriteUint(data As UInteger)
        Public MustOverride Sub WriteFloat(data As Single)
        Public MustOverride Sub WriteLong(data As Long)
        Public MustOverride Sub WriteUlong(data As ULong)
        Public MustOverride Sub WriteDouble(data As Double)
        Public MustOverride Sub WriteDateTime(data As Date)
        Public MustOverride Sub WriteString(data As String)
        Public MustOverride Sub WriteSHAREDDATA(data As SHAREDDATA, version As Integer?)
    End Class

    Public MustInherit Class STREAMREADER
        Public MustOverride Function ReadBytes(length As Integer?) As Byte()
        Public MustOverride Function ReadBool() As Boolean
        Public MustOverride Function ReadInt() As Integer
        Public MustOverride Function ReadUint() As UInteger
        Public MustOverride Function ReadFloat() As Single
        Public MustOverride Function ReadLong() As Long
        Public MustOverride Function ReadUlong() As ULong
        Public MustOverride Function ReadDouble() As Double
        Public MustOverride Function ReadDateTime() As Date
        Public MustOverride Function ReadString() As String
        Public MustOverride Function ReadSHAREDDATA(type As Type, version As Integer?) As SHAREDDATA
    End Class

    Public Enum Mode As Integer
        read
        write
        neither
    End Enum

    Public Class ReaderWriter
        Public Class CantReadOrWriteException
            Inherits Exception
        End Class

        Public Sub New(_writer As STREAMWRITER, _reader As STREAMREADER, _mode As Mode)
            writer = _writer
            reader = _reader
            mode = _mode
        End Sub

        Private ReadOnly writer As STREAMWRITER
        Private ReadOnly reader As STREAMREADER
        Private ReadOnly mode As Mode

        Public Function ReadOrWrite(bytes As Byte(), length As Integer?) As Byte()
            If mode = STREAMDATA(Of T).Mode.read Then
                Return reader.ReadBytes(length)
            ElseIf mode = STREAMDATA(Of T).Mode.write Then
                writer.WriteBytes(bytes, length)
                Return Nothing
            Else
                Throw New CantReadOrWriteException()
            End If
        End Function
    End Class

    Public MustInherit Class StreamInfomation
        Public Sub New(_type As Type, _version As Integer?, _length As Integer?, _sender As Func(Of Object), _receiver As Action(Of Object))
            If Not _type.IsArray Then
                Throw New ArgumentException()
            End If

            Dim elementType As Type = _type.GetElementType()
            If Not elementType.IsSubclassOf(GetType(SHAREDDATA)) Then
                Throw New ArgumentException()
            End If
            If elementType.IsAbstract Then
                Throw New ArgumentException()
            End If
            If elementType.IsArray Then
                Throw New ArgumentException()
            End If

            Dim sd As SHAREDDATA = TryCast(Activator.CreateInstance(elementType), SHAREDDATA)
            If (Not sd.IsVersioned And _version IsNot Nothing) Or (sd.IsVersioned And _version Is Nothing) Then
                Throw New ArgumentException()
            End If

            versionBacking = _version
            lengthBacking = _length

            Type = _type
            Sender = _sender
            Receiver = _receiver
        End Sub

        Public Sub New(_type As Type, _lengthOrVersion As Integer?, _sender As Func(Of Object), _receiver As Action(Of Object))
            If _type.IsArray Then
                Dim elementType As Type = _type.GetElementType()
                If elementType.IsSubclassOf(GetType(SHAREDDATA)) Then
                    Throw New ArgumentException()
                End If
                If elementType.IsAbstract Then
                    Throw New ArgumentException()
                End If
                If elementType.IsArray Then
                    Throw New ArgumentException()
                End If

                lengthBacking = _lengthOrVersion
            ElseIf _type.IsSubclassOf(GetType(SHAREDDATA)) Then
                If _type.IsAbstract Then
                    Throw New ArgumentException()
                End If

                Dim sd As SHAREDDATA = TryCast(Activator.CreateInstance(_type), SHAREDDATA)
                If (Not sd.IsVersioned And _lengthOrVersion IsNot Nothing) Or (sd.IsVersioned And _lengthOrVersion Is Nothing) Then
                    Throw New ArgumentException()
                End If

                versionBacking = _lengthOrVersion
            Else
                Throw New ArgumentException()
            End If

            Type = _type
            Sender = _sender
            Receiver = _receiver
        End Sub

        Public Sub New(_type As Type, _sender As Func(Of Object), _receiver As Action(Of Object))
            If _type.IsArray Then
                Throw New ArgumentException()
            End If
            If _type.IsSubclassOf(GetType(SHAREDDATA)) Then
                Throw New ArgumentException()
            End If
            If _type.IsAbstract Then
                Throw New ArgumentException()
            End If

            Type = _type
            Sender = _sender
            Receiver = _receiver
        End Sub

        Public ReadOnly Type As Type
        Public ReadOnly Sender As Func(Of Object)
        Public ReadOnly Receiver As Action(Of Object)

        Private ReadOnly lengthBacking As Integer?
        Public ReadOnly Property Length As Integer?
            Get
                If Type.IsArray Then
                    Return lengthBacking
                Else
                    Throw New NotSupportedException()
                End If
            End Get
        End Property

        Private ReadOnly versionBacking As Integer?
        Public ReadOnly Property Version As Integer
            Get
                Dim sd As SHAREDDATA
                If Type.IsSubclassOf(GetType(SHAREDDATA)) Then
                    sd = TryCast(Activator.CreateInstance(Type), SHAREDDATA)
                ElseIf Type.IsArray Then
                    Dim elementType As Type = Type.GetElementType()
                    If elementType.IsSubclassOf(GetType(SHAREDDATA)) Then
                        sd = TryCast(Activator.CreateInstance(elementType), SHAREDDATA)
                    Else
                        Throw New NotSupportedException()
                    End If
                Else
                    Throw New NotSupportedException()
                End If

                If Not sd.IsVersioned Then
                    Throw New NotSupportedException()
                End If

                Return versionBacking.Value
            End Get
        End Property
    End Class

    Protected MustOverride ReadOnly Property StreamInfo As Func(Of ReaderWriter, IEnumerable(Of T))

    Protected Sub Write(writer As STREAMWRITER, si As StreamInfomation)
        Dim _Write As Action(Of Type, Object) =
            Sub(type As Type, o As Object)
                If type Is GetType(Boolean) Then
                    writer.WriteBool(DirectCast(o, Boolean))
                ElseIf type Is GetType(Integer) Then
                    writer.WriteInt(DirectCast(o, Integer))
                ElseIf type Is GetType(UInteger) Then
                    writer.WriteUint(DirectCast(o, UInteger))
                ElseIf type Is GetType(Single) Then
                    writer.WriteFloat(DirectCast(o, Single))
                ElseIf type Is GetType(Long) Then
                    writer.WriteLong(DirectCast(o, Long))
                ElseIf type Is GetType(ULong) Then
                    writer.WriteUlong(DirectCast(o, ULong))
                ElseIf type Is GetType(Double) Then
                    writer.WriteDouble(DirectCast(o, Double))
                ElseIf type Is GetType(Date) Then
                    writer.WriteDateTime(DirectCast(o, Date))
                ElseIf type Is GetType(String) Then
                    writer.WriteString(DirectCast(o, String))
                ElseIf type.IsSubclassOf(GetType(SHAREDDATA)) Then
                    Dim sd As SHAREDDATA = TryCast(o, SHAREDDATA)

                    writer.WriteSHAREDDATA(sd, If(sd.IsVersioned And Not sd.IsVersionSaved, si.Version, Nothing))
                Else
                    Throw New NotSupportedException()
                End If
            End Sub

        Dim obj As Object = si.Sender()
        If obj.GetType() <> si.Type Then
            Throw New InvalidOperationException()
        End If

        If si.Type Is GetType(Byte()) Then
            writer.WriteBytes(DirectCast(obj, Byte()), si.Length)
        ElseIf si.Type.IsArray Then
            Dim os As Array = TryCast(obj, Array)
            Dim elementType As Type = si.Type.GetElementType()

            If si.Length Is Nothing Then
                writer.WriteInt(os.Length)
            End If
            For Each innerObj As Object In os
                _Write(elementType, innerObj)
            Next
        Else
            _Write(si.Type, obj)
        End If
    End Sub

    Protected Sub Read(reader As STREAMREADER, si As StreamInfomation)
        Dim _Read As Func(Of Type, Object) =
            Function(type As Type)
                If type Is GetType(Boolean) Then
                    Return reader.ReadBool()
                ElseIf type Is GetType(Integer) Then
                    Return reader.ReadInt()
                ElseIf type Is GetType(UInteger) Then
                    Return reader.ReadUint()
                ElseIf type Is GetType(Single) Then
                    Return reader.ReadFloat()
                ElseIf type Is GetType(Long) Then
                    Return reader.ReadLong()
                ElseIf type Is GetType(ULong) Then
                    Return reader.ReadUlong()
                ElseIf type Is GetType(Double) Then
                    Return reader.ReadDouble()
                ElseIf type Is GetType(Date) Then
                    Return reader.ReadDateTime()
                ElseIf type Is GetType(String) Then
                    Return reader.ReadString()
                ElseIf type Is GetType(SHAREDDATA) Then
                    Dim sd As SHAREDDATA = TryCast(Activator.CreateInstance(type), SHAREDDATA)

                    Return reader.ReadSHAREDDATA(type, If(sd.IsVersioned And Not sd.IsVersionSaved, si.Version, Nothing))
                Else
                    Throw New NotSupportedException()
                End If
            End Function

        If si.Type Is GetType(Byte()) Then
            si.Receiver(reader.ReadBytes(si.Length))
        ElseIf si.Type.IsArray Then
            Dim elementType As Type = si.Type.GetElementType()
            Dim os As Array = Array.CreateInstance(elementType, If(si.Length Is Nothing, reader.ReadInt(), si.Length.Value))

            For i As Integer = 0 To os.Length - 1
                os.SetValue(_Read(elementType), i)
            Next

            si.Receiver(os)
        Else
            si.Receiver(_Read(si.Type))
        End If
    End Sub
End Class

Public MustInherit Class SHAREDDATA
    Inherits STREAMDATA(Of SHAREDDATA.MainDataInformation)

    Public Class MyStreamWriter
        Inherits STREAMWRITER

        Public Sub New(_stream As Stream)
            stream = _stream
        End Sub

        Private ReadOnly stream As Stream

        Public Overrides Sub WriteBool(data As Boolean)
            stream.Write(BitConverter.GetBytes(data), 0, 1)
        End Sub

        Public Overrides Sub WriteBytes(data() As Byte, length As Integer?)
            If length Is Nothing Then
                stream.Write(BitConverter.GetBytes(data.Length), 0, 4)
            End If
            stream.Write(data, 0, data.Length)
        End Sub

        Public Overrides Sub WriteDateTime(data As Date)
            WriteLong(data.ToBinary())
        End Sub

        Public Overrides Sub WriteDouble(data As Double)
            stream.Write(BitConverter.GetBytes(data), 0, 8)
        End Sub

        Public Overrides Sub WriteFloat(data As Single)
            stream.Write(BitConverter.GetBytes(data), 0, 4)
        End Sub

        Public Overrides Sub WriteInt(data As Integer)
            stream.Write(BitConverter.GetBytes(data), 0, 4)
        End Sub

        Public Overrides Sub WriteLong(data As Long)
            stream.Write(BitConverter.GetBytes(data), 0, 8)
        End Sub

        Public Overrides Sub WriteSHAREDDATA(data As SHAREDDATA, version As Integer?)
            If data.IsVersioned And Not data.IsVersionSaved And data.Version <> version Then
                Throw New ArgumentException()
            End If

            'Dim bytes As byte() = data.
        End Sub

        Public Overrides Sub WriteString(data As String)
            Dim bytes As Byte() = Encoding.UTF8.GetBytes(data)
            stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4)
            stream.Write(bytes, 0, bytes.Length)
        End Sub

        Public Overrides Sub WriteUint(data As UInteger)
            stream.Write(BitConverter.GetBytes(data), 0, 4)
        End Sub

        Public Overrides Sub WriteUlong(data As ULong)
            stream.Write(BitConverter.GetBytes(data), 0, 8)
        End Sub
    End Class

    Public Class MyStreamReader
        Inherits STREAMREADER

        Public Sub New(_stream As Stream)
            stream = _stream
        End Sub

        Private ReadOnly stream As Stream

        Public Overrides Function ReadBool() As Boolean
            Dim bytes As Byte() = New Byte(1) {}
            stream.Read(bytes, 0, 1)
            Return BitConverter.ToBoolean(bytes, 0)
        End Function

        Public Overrides Function ReadBytes(length As Integer?) As Byte()
            If length Is Nothing Then
                Dim lengthBytes As Byte() = New Byte(4) {}
                stream.Read(lengthBytes, 0, 4)
                length = BitConverter.ToInt32(lengthBytes, 0)
            End If

            Dim bytes As Byte() = New Byte(length.Value) {}
            stream.Read(bytes, 0, length.Value)
            Return bytes
        End Function

        Public Overrides Function ReadDateTime() As Date
            Return Date.FromBinary(ReadLong())
        End Function

        Public Overrides Function ReadDouble() As Double
            Dim bytes As Byte() = New Byte(8) {}
            stream.Read(bytes, 0, 8)
            Return BitConverter.ToDouble(bytes, 0)
        End Function

        Public Overrides Function ReadFloat() As Single
            Dim bytes As Byte() = New Byte(4) {}
            stream.Read(bytes, 0, 4)
            Return BitConverter.ToSingle(bytes, 0)
        End Function

        Public Overrides Function ReadInt() As Integer
            Dim bytes As Byte() = New Byte(4) {}
            stream.Read(bytes, 0, 4)
            Return BitConverter.ToInt32(bytes, 0)
        End Function

        Public Overrides Function ReadLong() As Long
            Dim bytes As Byte() = New Byte(8) {}
            stream.Read(bytes, 0, 8)
            Return BitConverter.ToInt64(bytes, 0)
        End Function

        Public Overrides Function ReadSHAREDDATA(type As Type, version As Integer?) As SHAREDDATA

        End Function

        Public Overrides Function ReadString() As String

        End Function

        Public Overrides Function ReadUint() As UInteger
            Dim bytes As Byte() = New Byte(4) {}
            stream.Read(bytes, 0, 4)
            Return BitConverter.ToUInt32(bytes, 0)
        End Function

        Public Overrides Function ReadUlong() As ULong
            Dim bytes As Byte() = New Byte(8) {}
            stream.Read(bytes, 0, 8)
            Return BitConverter.ToUInt64(bytes, 0)
        End Function
    End Class

    Public Class MainDataInformation
        Inherits StreamInfomation

        Public Sub New(_type As Type, _version As Integer?, _length As Integer?, _getter As Func(Of Object), _setter As Action(Of Object))

        End Sub

        Public ReadOnly Property Getter As Func(Of Object)
            Get
                Return Sender
            End Get
        End Property

        Public ReadOnly Property Setter As Action(Of Object)
            Get
                Return Receiver
            End Get
        End Property
    End Class

    Public Overridable ReadOnly Property IsVersioned As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overridable ReadOnly Property IsVersionSaved As Boolean
        Get
            Return True
        End Get
    End Property

    Public Overridable ReadOnly Property IsCorruptionChecked As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overridable ReadOnly Property IsSigned As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overridable ReadOnly Property IsSignatureChecked As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overridable Property PubKey As Byte()
        Get
            Throw New NotSupportedException()
        End Get
        Protected Set(value As Byte())
            Throw New NotSupportedException()
        End Set
    End Property

    Public Overridable ReadOnly Property PrivKey As Byte()
        Get
            Throw New NotSupportedException()
        End Get
    End Property

    Private signature As Byte()
    Public ReadOnly Property IsValidSignature As Boolean
        Get
            If Not IsSigned Then
                Throw New NotSupportedException()
            End If
            If signature Is Nothing Then
                Throw New NotSupportedException()
            End If

            Return
        End Get
    End Property

    Private versionBacking As Integer?
    Public Property Version As Integer
        Get
            If Not IsVersioned Then
                Throw New NotSupportedException()
            End If

            Return versionBacking.Value
        End Get
        Set(value As Integer)
            If Not IsVersioned Then
                Throw New NotSupportedException()
            End If

            versionBacking = value
        End Set
    End Property

    Public ReadOnly Property LengthAll As Integer?
        Get
            Dim length As Integer? = 
        End Get
    End Property

    Public ReadOnly Property LengthMain As Integer?
        Get

        End Get
    End Property

    Protected Function ToBinaryMainData(si As Func(Of ReaderWriter, IEnumerable(Of MainDataInformation))) As Byte()
        Using ms As MemoryStream = New MemoryStream()
            Dim writer As MyStreamWriter = New MyStreamWriter(ms)

            'For Each mdi As MainDataInformation In si(New ReaderWriter(writer, New MyStream))
        End Using
    End Function

    Private Function VerifySignature() As Boolean
        Using dsa As ECDsaCng = New ECDsaCng(CngKey.Import(PubKey, CngKeyBlobFormat.EccPublicBlob))
            dsa.HashAlgorithm = CngAlgorithm.Sha256

            Return dsa.VerifyData(PubKey)
        End Using

    End Function
End Class