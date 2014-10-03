Imports System.Security.Cryptography
Imports System.IO
Imports System.Text

Imports CREA2014

Public Class Item
    Inherits SHAREDDATA

    Public Sub New()
        MyBase.New(0)
    End Sub

    Private m_id As Sha256Hash
    Private m_idByAdministrator As String
    Private m_title As String
    'Private m_importances As 

    Private m_projectId As Sha256Hash
    Private m_categoryId As Sha256Hash


    Private m_description As String

    Protected Overrides ReadOnly Property StreamInfo As Func(Of STREAMDATA(Of SHAREDDATA.MainDataInfomation).ReaderWriter, IEnumerable(Of SHAREDDATA.MainDataInfomation))
        Get

        End Get
    End Property
End Class

Public Class Project
    Inherits SHAREDDATA

    Public Sub New()
        MyBase.New(0)

        childProjectsIdCache = New CachedData(Of Sha256Hash())(
            Function()
                SyncLock childProjectsIdLock
                    Return m_childProjectsId.ToArray()
                End SyncLock
            End Function)
    End Sub

    Public Sub LoadVersion0(_name As String, _description As String)
        Version = 0

        m_name = _name
        m_description = _description
    End Sub

    Private m_name As String
    Private m_description As String
    Private m_childProjectsId As List(Of Sha256Hash)
    Private m_items As List(Of Item)

    Public ReadOnly Property Name As String
        Get
            If Version <> 0 Then
                Throw New NotSupportedException()
            End If

            Return m_name
        End Get
    End Property

    Public ReadOnly Property Description As String
        Get
            If Version <> 0 Then
                Throw New NotSupportedException()
            End If

            Return m_description
        End Get
    End Property

    Private ReadOnly childProjectsIdLock As Object = New Object()
    Private ReadOnly childProjectsIdCache As CachedData(Of Sha256Hash())
    Public ReadOnly Property ChildProjectsId As Sha256Hash()
        Get
            If Version <> 0 Then
                Throw New NotSupportedException()
            End If

            Return childProjectsIdCache.Data
        End Get
    End Property

    Private ReadOnly itemsLock As Object = New Object()
    Private ReadOnly itemsCache As CachedData(Of Item())
    Public ReadOnly Property Items As Item()
        Get
            If Version <> 0 Then
                Throw New NotSupportedException()
            End If

            Return itemsCache.Data
        End Get
    End Property

    Protected Overrides ReadOnly Property StreamInfo As Func(Of STREAMDATA(Of SHAREDDATA.MainDataInfomation).ReaderWriter, IEnumerable(Of SHAREDDATA.MainDataInfomation))
        Get

        End Get
    End Property
    Public Overrides ReadOnly Property IsVersioned As Boolean
        Get
            Return True
        End Get
    End Property
    Public Overrides ReadOnly Property IsCorruptionChecked As Boolean
        Get
            If Version = 0 Then
                Return True
            End If

            Throw New NotSupportedException()
        End Get
    End Property
End Class



Public Class BTSUse
    Public Sub ReportBug()
    End Sub
End Class

Public Class BTSTest
    Public Sub ReportBug()
    End Sub
End Class

Public Class BTSDevelopment
    Public Sub ReportBugFix()
    End Sub
End Class

Public Class BTSAdministration
    Public Sub AssignBugToDeveloper()
    End Sub

    Public Sub CheckBugFix()
    End Sub
End Class

Public Interface IBTSUser
    Sub ReportBug()
End Interface

Public Interface IBTSTester
    Inherits IBTSUser
End Interface

Public Interface IBTSDeveloper
    Inherits IBTSTester

    Sub ReportBugFix()
End Interface

Public Interface IBTSAdministrator
    Inherits IBTSDeveloper

    Sub AssignBugToDeveloper()
    Sub CheckBugFix()
End Interface

Public Class BTSUser
    Implements IBTSUser

    Public Sub New()
        use = New BTSUse()
    End Sub

    Private ReadOnly use As BTSUse

    Public Sub ReportBug() Implements IBTSUser.ReportBug
        use.ReportBug()
    End Sub
End Class

Public Class BTSTester
    Implements IBTSTester

    Public Sub New()
        use = New BTSUse()
        test = New BTSTest()
    End Sub

    Private ReadOnly use As BTSUse
    Private ReadOnly test As BTSTest

    Public Sub ReportBug() Implements IBTSUser.ReportBug
        test.ReportBug()
    End Sub
End Class

Public Class BTSDeveloper
    Implements IBTSDeveloper

    Public Sub New()
        use = New BTSUse()
        test = New BTSTest()
        development = New BTSDevelopment()
    End Sub

    Private ReadOnly use As BTSUse
    Private ReadOnly test As BTSTest
    Private ReadOnly development As BTSDevelopment

    Public Sub ReportBug() Implements IBTSUser.ReportBug
        test.ReportBug()
    End Sub

    Public Sub ReportBugFix() Implements IBTSDeveloper.ReportBugFix
        development.ReportBugFix()
    End Sub
End Class

Public Class BTSAdministrator
    Implements IBTSAdministrator

    Public Sub New()
        use = New BTSUse()
        test = New BTSTest()
        development = New BTSDevelopment()
        administration = New BTSAdministration()
    End Sub

    Private ReadOnly use As BTSUse
    Private ReadOnly test As BTSTest
    Private ReadOnly development As BTSDevelopment
    Private ReadOnly administration As BTSAdministration

    Public Sub ReportBug() Implements IBTSUser.ReportBug
        test.ReportBug()
    End Sub

    Public Sub AssignBugToDeveloper() Implements IBTSAdministrator.AssignBugToDeveloper
        administration.AssignBugToDeveloper()
    End Sub

    Public Sub ReportBugFix() Implements IBTSDeveloper.ReportBugFix
        development.ReportBugFix()
    End Sub

    Public Sub CheckBugFix() Implements IBTSAdministrator.CheckBugFix
        administration.CheckBugFix()
    End Sub
End Class



Public MustInherit Class DATA
End Class

Public MustInherit Class INTERNALDATA
    Inherits DATA
End Class

Public MustInherit Class STREAMDATA(Of T As STREAMDATA(Of T).StreamInfomation)
    Inherits DATA
    Public MustInherit Class STREAMWRITER
        Public MustOverride Sub WriteBytes(data As Byte(), length As Nullable(Of Integer))
        Public MustOverride Sub WriteBool(data As Boolean)
        Public MustOverride Sub WriteInt(data As Integer)
        Public MustOverride Sub WriteUint(data As UInteger)
        Public MustOverride Sub WriteFloat(data As Single)
        Public MustOverride Sub WriteLong(data As Long)
        Public MustOverride Sub WriteUlong(data As ULong)
        Public MustOverride Sub WriteDouble(data As Double)
        Public MustOverride Sub WriteDateTime(data As DateTime)
        Public MustOverride Sub WriteString(data As String)
        Public MustOverride Sub WriteSHAREDDATA(data As SHAREDDATA, version As Nullable(Of Integer))
    End Class

    Public MustInherit Class STREAMREADER
        Public MustOverride Function ReadBytes(length As Nullable(Of Integer)) As Byte()
        Public MustOverride Function ReadBool() As Boolean
        Public MustOverride Function ReadInt() As Integer
        Public MustOverride Function ReadUint() As UInteger
        Public MustOverride Function ReadFloat() As Single
        Public MustOverride Function ReadLong() As Long
        Public MustOverride Function ReadUlong() As ULong
        Public MustOverride Function ReadDouble() As Double
        Public MustOverride Function ReadDateTime() As DateTime
        Public MustOverride Function ReadString() As String
        Public MustOverride Function ReadSHAREDDATA(type As Type, version As Nullable(Of Integer)) As SHAREDDATA
    End Class

    Public Enum Mode
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

        Public Function ReadOrWrite(bytes As Byte(), length As Nullable(Of Integer)) As Byte()
            If mode = mode.read Then
                Return reader.ReadBytes(length)
            ElseIf mode = mode.write Then
                writer.WriteBytes(bytes, length)
                Return Nothing
            Else
                Throw New CantReadOrWriteException()
            End If
        End Function
    End Class

    Public MustInherit Class StreamInfomation
        Public Sub New(_type As Type, _version As Nullable(Of Integer), _length As Nullable(Of Integer), _sender As Func(Of Object), _receiver As Action(Of Object))
            If Not _type.IsArray Then
                Throw New ArgumentException("stream_info_not_array")
            End If

            Dim elementType As Type = _type.GetElementType()
            If Not elementType.IsSubclassOf(GetType(SHAREDDATA)) Then
                Throw New ArgumentException("stream_info_not_sd_array")
            End If
            If elementType.IsAbstract Then
                Throw New ArgumentException("stream_info_sd_array_abstract")
            End If
            If elementType.IsArray Then
                Throw New ArgumentException("stream_info_array_of_array")
            End If

            Dim sd As SHAREDDATA = TryCast(Activator.CreateInstance(elementType), SHAREDDATA)
            If (Not sd.IsVersioned AndAlso _version IsNot Nothing) OrElse (sd.IsVersioned AndAlso _version Is Nothing) Then
                Throw New ArgumentException("stream_info_not_sd_array_is_versioned")
            End If

            m_version = _version
            m_length = _length

            Type = _type
            Sender = _sender
            Receiver = _receiver
        End Sub

        Public Sub New(_type As Type, _lengthOrVersion As Nullable(Of Integer), _sender As Func(Of Object), _receiver As Action(Of Object))
            If _type.IsArray Then
                Dim elementType As Type = _type.GetElementType()
                If elementType.IsSubclassOf(GetType(SHAREDDATA)) Then
                    Throw New ArgumentException("stream_info_sd_array")
                End If
                If elementType.IsAbstract Then
                    Throw New ArgumentException("stream_info_array_abstract")
                End If
                If elementType.IsArray Then
                    Throw New ArgumentException("stream_info_array_of_array")
                End If

                m_length = _lengthOrVersion
            ElseIf _type.IsSubclassOf(GetType(SHAREDDATA)) Then
                If _type.IsAbstract Then
                    Throw New ArgumentException("stream_info_sd_abstract")
                End If

                Dim sd As SHAREDDATA = TryCast(Activator.CreateInstance(_type), SHAREDDATA)
                If (Not sd.IsVersioned AndAlso _lengthOrVersion IsNot Nothing) OrElse (sd.IsVersioned AndAlso _lengthOrVersion Is Nothing) Then
                    Throw New ArgumentException("stream_info_sd_is_versioned")
                End If

                m_version = _lengthOrVersion
            Else
                Throw New ArgumentException("stream_info_not_array_sd")
            End If

            Type = _type
            Sender = _sender
            Receiver = _receiver
        End Sub

        Public Sub New(_type As Type, _sender As Func(Of Object), _receiver As Action(Of Object))
            If _type.IsArray Then
                Throw New ArgumentException("stream_info_array")
            End If
            If _type.IsSubclassOf(GetType(SHAREDDATA)) Then
                Throw New ArgumentException("stream_info_sd")
            End If
            If _type.IsAbstract Then
                Throw New ArgumentException("stream_info_abstract")
            End If

            Type = _type
            Sender = _sender
            Receiver = _receiver
        End Sub

        Public ReadOnly Type As Type
        Public ReadOnly Sender As Func(Of Object)
        Public ReadOnly Receiver As Action(Of Object)

        Private ReadOnly m_length As Nullable(Of Integer)
        Public ReadOnly Property Length() As Nullable(Of Integer)
            Get
                If Type.IsArray Then
                    Return m_length
                Else
                    Throw New NotSupportedException("stream_info_length")
                End If
            End Get
        End Property

        Private ReadOnly m_version As Nullable(Of Integer)
        Public ReadOnly Property Version() As Integer
            Get
                Dim sd As SHAREDDATA
                If Type.IsSubclassOf(GetType(SHAREDDATA)) Then
                    sd = TryCast(Activator.CreateInstance(Type), SHAREDDATA)
                ElseIf Type.IsArray Then
                    Dim elementType As Type = Type.GetElementType()
                    If elementType.IsSubclassOf(GetType(SHAREDDATA)) Then
                        sd = TryCast(Activator.CreateInstance(elementType), SHAREDDATA)
                    Else
                        Throw New NotSupportedException("stream_info_version")
                    End If
                Else
                    Throw New NotSupportedException("stream_info_version")
                End If

                If Not sd.IsVersioned Then
                    Throw New NotSupportedException("stream_info_version")
                End If

                Return m_version.Value
            End Get
        End Property
    End Class

    Protected MustOverride ReadOnly Property StreamInfo() As Func(Of ReaderWriter, IEnumerable(Of T))

    Protected Sub Write(writer As STREAMWRITER, si As StreamInfomation)
        Dim _Write As Action(Of Type, Object) =
            Sub(type, o)
                If type = GetType(Boolean) Then
                    writer.WriteBool(CBool(o))
                ElseIf type = GetType(Integer) Then
                    writer.WriteInt(CInt(o))
                ElseIf type = GetType(UInteger) Then
                    writer.WriteUint(CUInt(o))
                ElseIf type = GetType(Single) Then
                    writer.WriteFloat(CSng(o))
                ElseIf type = GetType(Long) Then
                    writer.WriteLong(CLng(o))
                ElseIf type = GetType(ULong) Then
                    writer.WriteUlong(CULng(o))
                ElseIf type = GetType(Double) Then
                    writer.WriteDouble(CDbl(o))
                ElseIf type = GetType(DateTime) Then
                    writer.WriteDateTime(DirectCast(o, DateTime))
                ElseIf type = GetType(String) Then
                    writer.WriteString(DirectCast(o, String))
                ElseIf type.IsSubclassOf(GetType(SHAREDDATA)) Then
                    Dim sd As SHAREDDATA = TryCast(o, SHAREDDATA)

                    writer.WriteSHAREDDATA(sd, If(sd.IsVersioned AndAlso Not sd.IsVersionSaved, CType(si.Version, Nullable(Of Integer)), Nothing))
                Else
                    Throw New NotSupportedException("sd_write_not_supported")
                End If
            End Sub

        Dim obj As Object = si.Sender()
        If obj.[GetType]() <> si.Type Then
            Throw New InvalidDataException("sd_writer_type_mismatch")
        End If

        If si.Type = GetType(Byte()) Then
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
            Function(type)
                If type = GetType(Boolean) Then
                    Return reader.ReadBool()
                ElseIf type = GetType(Integer) Then
                    Return reader.ReadInt()
                ElseIf type = GetType(UInteger) Then
                    Return reader.ReadUint()
                ElseIf type = GetType(Single) Then
                    Return reader.ReadFloat()
                ElseIf type = GetType(Long) Then
                    Return reader.ReadLong()
                ElseIf type = GetType(ULong) Then
                    Return reader.ReadUlong()
                ElseIf type = GetType(Double) Then
                    Return reader.ReadDouble()
                ElseIf type = GetType(DateTime) Then
                    Return reader.ReadDateTime()
                ElseIf type = GetType(String) Then
                    Return reader.ReadString()
                ElseIf type.IsSubclassOf(GetType(SHAREDDATA)) Then
                    Dim sd As SHAREDDATA = TryCast(Activator.CreateInstance(type), SHAREDDATA)

                    Return reader.ReadSHAREDDATA(type, If(sd.IsVersioned AndAlso Not sd.IsVersionSaved, CType(si.Version, Nullable(Of Integer)), Nothing))
                Else
                    Throw New NotSupportedException("sd_read_not_supported")
                End If
            End Function

        If si.Type = GetType(Byte()) Then
            si.Receiver(reader.ReadBytes(si.Length))
        ElseIf si.Type.IsArray Then
            Dim elementType As Type = si.Type.GetElementType()
            Dim os As Array = TryCast(Array.CreateInstance(elementType, If(si.Length Is Nothing, reader.ReadInt(), CInt(si.Length))), Array)

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
    Inherits STREAMDATA(Of SHAREDDATA.MainDataInfomation)

    Public Class MyStreamWriter
        Inherits STREAMWRITER

        Public Sub New(_stream As Stream)
            stream = _stream
        End Sub

        Private ReadOnly stream As Stream

        Public Overrides Sub WriteBytes(data As Byte(), length As Nullable(Of Integer))
            If length Is Nothing Then
                stream.Write(BitConverter.GetBytes(data.Length), 0, 4)
            End If
            stream.Write(data, 0, data.Length)
        End Sub

        Public Overrides Sub WriteBool(data As Boolean)
            stream.Write(BitConverter.GetBytes(data), 0, 1)
        End Sub

        Public Overrides Sub WriteInt(data As Integer)
            stream.Write(BitConverter.GetBytes(data), 0, 4)
        End Sub

        Public Overrides Sub WriteUint(data As UInteger)
            stream.Write(BitConverter.GetBytes(data), 0, 4)
        End Sub

        Public Overrides Sub WriteFloat(data As Single)
            stream.Write(BitConverter.GetBytes(data), 0, 4)
        End Sub

        Public Overrides Sub WriteLong(data As Long)
            stream.Write(BitConverter.GetBytes(data), 0, 8)
        End Sub

        Public Overrides Sub WriteUlong(data As ULong)
            stream.Write(BitConverter.GetBytes(data), 0, 8)
        End Sub

        Public Overrides Sub WriteDouble(data As Double)
            stream.Write(BitConverter.GetBytes(data), 0, 8)
        End Sub

        Public Overrides Sub WriteDateTime(data As DateTime)
            WriteLong(data.ToBinary())
        End Sub

        Public Overrides Sub WriteString(data As String)
            Dim bytes As Byte() = Encoding.UTF8.GetBytes(data)
            stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4)
            stream.Write(bytes, 0, bytes.Length)
        End Sub

        Public Overrides Sub WriteSHAREDDATA(data As SHAREDDATA, version As Nullable(Of Integer))
            If data.IsVersioned AndAlso Not data.IsVersionSaved AndAlso data.Version <> version Then
                Throw New ArgumentException("write_sd_version_mismatch")
            End If

            Dim bytes As Byte() = data.ToBinary()
            If data.LengthAll Is Nothing Then
                stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4)
            End If
            stream.Write(bytes, 0, bytes.Length)
        End Sub
    End Class

    Public Class MyStreamReader
        Inherits STREAMREADER

        Public Sub New(_stream As Stream)
            stream = _stream
        End Sub

        Private ReadOnly stream As Stream

        Public Overrides Function ReadBytes(length As Nullable(Of Integer)) As Byte()
            If length Is Nothing Then
                Dim lengthBytes As Byte() = New Byte(3) {}
                stream.Read(lengthBytes, 0, 4)
                length = BitConverter.ToInt32(lengthBytes, 0)
            End If

            Dim bytes As Byte() = New Byte(length.Value - 1) {}
            stream.Read(bytes, 0, length.Value)
            Return bytes
        End Function

        Public Overrides Function ReadBool() As Boolean
            Dim bytes As Byte() = New Byte(0) {}
            stream.Read(bytes, 0, 1)
            Return BitConverter.ToBoolean(bytes, 0)
        End Function

        Public Overrides Function ReadInt() As Integer
            Dim bytes As Byte() = New Byte(3) {}
            stream.Read(bytes, 0, 4)
            Return BitConverter.ToInt32(bytes, 0)
        End Function

        Public Overrides Function ReadUint() As UInteger
            Dim bytes As Byte() = New Byte(3) {}
            stream.Read(bytes, 0, 4)
            Return BitConverter.ToUInt32(bytes, 0)
        End Function

        Public Overrides Function ReadFloat() As Single
            Dim bytes As Byte() = New Byte(3) {}
            stream.Read(bytes, 0, 4)
            Return BitConverter.ToSingle(bytes, 0)
        End Function

        Public Overrides Function ReadLong() As Long
            Dim bytes As Byte() = New Byte(7) {}
            stream.Read(bytes, 0, 8)
            Return BitConverter.ToInt64(bytes, 0)
        End Function

        Public Overrides Function ReadUlong() As ULong
            Dim bytes As Byte() = New Byte(7) {}
            stream.Read(bytes, 0, 8)
            Return BitConverter.ToUInt64(bytes, 0)
        End Function

        Public Overrides Function ReadDouble() As Double
            Dim bytes As Byte() = New Byte(7) {}
            stream.Read(bytes, 0, 8)
            Return BitConverter.ToDouble(bytes, 0)
        End Function

        Public Overrides Function ReadDateTime() As DateTime
            Return DateTime.FromBinary(ReadLong())
        End Function

        Public Overrides Function ReadString() As String
            Dim lengthBytes As Byte() = New Byte(3) {}
            stream.Read(lengthBytes, 0, 4)
            Dim length As Integer = BitConverter.ToInt32(lengthBytes, 0)

            Dim bytes As Byte() = New Byte(length - 1) {}
            stream.Read(bytes, 0, length)
            Return Encoding.UTF8.GetString(bytes)
        End Function

        Public Overrides Function ReadSHAREDDATA(type As Type, version As Nullable(Of Integer)) As SHAREDDATA
            Dim sd As SHAREDDATA = TryCast(Activator.CreateInstance(type), SHAREDDATA)
            If sd.IsVersioned AndAlso Not sd.IsVersionSaved Then
                If version Is Nothing Then
                    Throw New ArgumentException("read_sd_version_null")
                End If

                sd.Version = version.Value
            End If

            Dim length As Nullable(Of Integer) = sd.LengthAll
            If length Is Nothing Then
                Dim lengthBytes As Byte() = New Byte(3) {}
                stream.Read(lengthBytes, 0, 4)
                length = BitConverter.ToInt32(lengthBytes, 0)
            End If

            Dim bytes As Byte() = New Byte(length.Value - 1) {}
            stream.Read(bytes, 0, length.Value)

            sd.FromBinary(bytes)

            Return sd
        End Function
    End Class

    Public Class MainDataInfomation
        Inherits StreamInfomation

        Public Sub New(_type As Type, _version As Nullable(Of Integer), _length As Nullable(Of Integer), _getter As Func(Of Object), _setter As Action(Of Object))
            MyBase.New(_type, _version, _length, _getter, _setter)
        End Sub

        Public Sub New(_type As Type, _lengthOrVersion As Nullable(Of Integer), _getter As Func(Of Object), _setter As Action(Of Object))
            MyBase.New(_type, _lengthOrVersion, _getter, _setter)
        End Sub

        Public Sub New(_type As Type, _getter As Func(Of Object), _setter As Action(Of Object))
            MyBase.New(_type, _getter, _setter)
        End Sub

        Public ReadOnly Property Getter() As Func(Of Object)
            Get
                Return Sender
            End Get
        End Property
        Public ReadOnly Property Setter() As Action(Of Object)
            Get
                Return Receiver
            End Get
        End Property
    End Class

    Public Sub New()
        Me.New(Nothing)
    End Sub

    Public Sub New(_version As Nullable(Of Integer))
        If (IsVersioned AndAlso _version Is Nothing) OrElse (Not IsVersioned AndAlso _version IsNot Nothing) Then
            Throw New ArgumentException("sd_is_versioned_and_version")
        End If

        m_version = _version
    End Sub

    Public Overridable ReadOnly Property IsVersioned() As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overridable ReadOnly Property IsVersionSaved() As Boolean
        Get
            Return True
        End Get
    End Property

    Public Overridable ReadOnly Property IsCorruptionChecked() As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overridable ReadOnly Property IsSigned() As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overridable ReadOnly Property IsSignatureChecked() As Boolean
        Get
            Return False
        End Get
    End Property

    Public Overridable Property PubKey() As Byte()
        Get
            Throw New NotSupportedException("sd_pubkey")
        End Get
        Protected Set(value As Byte())
            Throw New NotSupportedException("sd_pubkey_set")
        End Set
    End Property

    Public Overridable ReadOnly Property PrivKey() As Byte()
        Get
            Throw New NotSupportedException("sd_privkey")
        End Get
    End Property

    Private signature As Byte()
    Public ReadOnly Property IsValidSignature() As Boolean
        Get
            If Not IsSigned Then
                Throw New NotSupportedException("sd_is_valid_sig")
            End If
            If signature Is Nothing Then
                Throw New InvalidOperationException("sd_signature")
            End If

            Return VerifySignature()
        End Get
    End Property

    Private m_version As Nullable(Of Integer)
    Public Property Version() As Integer
        Get
            If Not IsVersioned Then
                Throw New NotSupportedException("sd_version")
            End If

            Return m_version.Value
        End Get
        Set(value As Integer)
            If Not IsVersioned Then
                Throw New NotSupportedException("sd_version")
            End If

            m_version = value
        End Set
    End Property

    Public ReadOnly Property LengthAll() As Nullable(Of Integer)
        Get
            Dim length As Nullable(Of Integer) = LengthMain
            If length Is Nothing OrElse IsSigned Then
                Return Nothing
            Else
                If IsVersioned AndAlso IsVersionSaved Then
                    length += 4
                End If
                If IsCorruptionChecked Then
                    length += 4
                End If
                Return length
            End If
        End Get
    End Property

    Public ReadOnly Property LengthMain() As Nullable(Of Integer)
        Get
            Dim _GetLength As Func(Of Type, MainDataInfomation, Nullable(Of Integer)) =
                Function(type, mdi)
                    If type = GetType(Boolean) OrElse type = GetType(Byte) Then
                        Return 1
                    ElseIf type = GetType(Integer) OrElse type = GetType(UInteger) OrElse type = GetType(Single) Then
                        Return 4
                    ElseIf type = GetType(Long) OrElse type = GetType(ULong) OrElse type = GetType(Double) OrElse type = GetType(DateTime) Then
                        Return 8
                    ElseIf type = GetType(String) Then
                        Return Nothing
                    ElseIf type.IsSubclassOf(GetType(SHAREDDATA)) Then
                        Return TryCast(Activator.CreateInstance(type), SHAREDDATA).LengthAll
                    Else
                        Throw New NotSupportedException("sd_length_not_supported")
                    End If
                End Function

            If IsVersioned AndAlso IsVersionSaved Then
                Return Nothing
            End If

            Dim length As Integer = 0
            Try
                For Each mdi In StreamInfo(New ReaderWriter(Nothing, Nothing, Mode.neither))
                    If mdi.Type.IsArray Then
                        If mdi.Length Is Nothing Then
                            Return Nothing
                        Else
                            Dim innerLength As System.Nullable(Of Integer) = _GetLength(mdi.Type.GetElementType(), mdi)
                            If innerLength Is Nothing Then
                                Return Nothing
                            Else
                                length += mdi.Length.Value * innerLength.Value
                            End If
                        End If
                    Else
                        Dim innerLength As System.Nullable(Of Integer) = _GetLength(mdi.Type, mdi)
                        If innerLength Is Nothing Then
                            Return Nothing
                        Else
                            length += innerLength.Value
                        End If
                    End If
                Next
            Catch generatedExceptionName As ReaderWriter.CantReadOrWriteException
                Return Nothing
            End Try
            Return length
        End Get
    End Property

    Protected Function ToBinaryMainData(si As Func(Of ReaderWriter, IEnumerable(Of MainDataInfomation))) As Byte()
        Using ms As New MemoryStream()
            Dim writer As New MyStreamWriter(ms)

            For Each mdi In si(New ReaderWriter(writer, New MyStreamReader(ms), Mode.write))
                Write(writer, mdi)
            Next

            Return ms.ToArray()
        End Using
    End Function

    Public Function ToBinaryMainData() As Byte()
        Return ToBinaryMainData(StreamInfo)
    End Function

    Protected Function ToBinary(si As Func(Of ReaderWriter, IEnumerable(Of MainDataInfomation))) As Byte()
        Dim mainDataBytes As Byte() = ToBinaryMainData(si)

        Using ms As New MemoryStream()
            If IsVersioned AndAlso IsVersionSaved Then
                ms.Write(BitConverter.GetBytes(m_version.Value), 0, 4)
            End If
            If IsCorruptionChecked Then
                ms.Write(mainDataBytes.ComputeSha256(), 0, 4)
            End If
            If IsSigned Then
                ms.Write(BitConverter.GetBytes(PubKey.Length), 0, 4)
                ms.Write(PubKey, 0, PubKey.Length)

                Using dsa As New ECDsaCng(CngKey.Import(PrivKey, CngKeyBlobFormat.EccPrivateBlob))
                    dsa.HashAlgorithm = CngAlgorithm.Sha256

                    signature = dsa.SignData(PubKey.Combine(mainDataBytes))

                    ms.Write(BitConverter.GetBytes(signature.Length), 0, 4)
                    ms.Write(signature, 0, signature.Length)
                End Using
            End If
            ms.Write(mainDataBytes, 0, mainDataBytes.Length)

            Return ms.ToArray()
        End Using
    End Function

    Public Function ToBinary() As Byte()
        Return ToBinary(StreamInfo)
    End Function

    Public Sub FromBinary(binary As Byte())
        Dim mainDataBytes As Byte()
        Using ms As New MemoryStream(binary)
            If IsVersioned AndAlso IsVersionSaved Then
                Dim versionBytes As Byte() = New Byte(3) {}
                ms.Read(versionBytes, 0, 4)
                m_version = BitConverter.ToInt32(versionBytes, 0)
            End If

            Dim check As System.Nullable(Of Integer) = Nothing
            If IsCorruptionChecked Then
                Dim checkBytes As Byte() = New Byte(3) {}
                ms.Read(checkBytes, 0, 4)
                check = BitConverter.ToInt32(checkBytes, 0)
            End If

            If IsSigned Then
                Dim pubKeyLengthBytes As Byte() = New Byte(3) {}
                ms.Read(pubKeyLengthBytes, 0, 4)
                Dim publicKeyLength As Integer = BitConverter.ToInt32(pubKeyLengthBytes, 0)

                Dim pubKey__1 As Byte() = New Byte(publicKeyLength - 1) {}
                ms.Read(pubKey__1, 0, pubKey__1.Length)
                PubKey = pubKey__1

                Dim signatureLengthBytes As Byte() = New Byte(3) {}
                ms.Read(signatureLengthBytes, 0, 4)
                Dim signatureLength As Integer = BitConverter.ToInt32(signatureLengthBytes, 0)

                signature = New Byte(signatureLength - 1) {}
                ms.Read(signature, 0, signature.Length)
            End If

            Dim length As Integer = CInt(ms.Length - ms.Position)
            mainDataBytes = New Byte(length - 1) {}
            ms.Read(mainDataBytes, 0, length)

            If IsCorruptionChecked AndAlso check <> BitConverter.ToInt32(mainDataBytes.ComputeSha256(), 0) Then
                Throw New InvalidDataException("from_binary_check")
            End If
            If IsSigned AndAlso IsSignatureChecked AndAlso Not VerifySignature() Then
                Throw New InvalidDataException("from_binary_signature")
            End If
        End Using
        Using ms As New MemoryStream(mainDataBytes)
            Dim reader As New MyStreamReader(ms)

            For Each mdi In StreamInfo(New ReaderWriter(New MyStreamWriter(ms), reader, Mode.read))
                Read(reader, mdi)
            Next
        End Using
    End Sub

    Public Shared Function FromBinary(Of T As SHAREDDATA)(binary As Byte()) As T
        Dim sd As T = TryCast(Activator.CreateInstance(GetType(T)), T)
        sd.FromBinary(binary)
        Return sd
    End Function

    Public Shared Function FromBinary(Of T As SHAREDDATA)(binary As Byte(), version As Integer) As T
        Dim sd As T = TryCast(Activator.CreateInstance(GetType(T)), T)
        sd.Version = version
        sd.FromBinary(binary)
        Return sd
    End Function

    Private Function VerifySignature() As Boolean
        Using dsa As New ECDsaCng(CngKey.Import(PubKey, CngKeyBlobFormat.EccPublicBlob))
            dsa.HashAlgorithm = CngAlgorithm.Sha256

            Return dsa.VerifyData(PubKey.Combine(ToBinaryMainData()), signature)
        End Using
    End Function
End Class