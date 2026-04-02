// Copyright (c) 2026 Rickard Nordström Pettersson. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Source: https://github.com/RickardPettersson/RustPlus_FCM

using System.Text;

/// <summary>
/// Lightweight protobuf writer for encoding messages without generated code.
/// </summary>
public sealed class ProtobufWriter
{
    private readonly MemoryStream _stream = new();

    public byte[] ToArray() => _stream.ToArray();

    public void WriteVarint(ulong value)
    {
        while (value > 0x7F)
        {
            _stream.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        _stream.WriteByte((byte)value);
    }

    public void WriteTag(int fieldNumber, int wireType)
    {
        WriteVarint((ulong)((fieldNumber << 3) | wireType));
    }

    public void WriteInt32(int fieldNumber, int value)
    {
        WriteTag(fieldNumber, 0);
        WriteVarint((ulong)value);
    }

    public void WriteInt64(int fieldNumber, long value)
    {
        WriteTag(fieldNumber, 0);
        WriteVarint((ulong)value);
    }

    public void WriteBool(int fieldNumber, bool value)
    {
        WriteTag(fieldNumber, 0);
        WriteVarint(value ? 1UL : 0UL);
    }

    public void WriteString(int fieldNumber, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteTag(fieldNumber, 2);
        WriteVarint((ulong)bytes.Length);
        _stream.Write(bytes);
    }

    public void WriteBytes(int fieldNumber, byte[] value)
    {
        WriteTag(fieldNumber, 2);
        WriteVarint((ulong)value.Length);
        _stream.Write(value);
    }

    public void WriteMessage(int fieldNumber, ProtobufWriter subMessage)
    {
        var bytes = subMessage.ToArray();
        WriteTag(fieldNumber, 2);
        WriteVarint((ulong)bytes.Length);
        _stream.Write(bytes);
    }

    public void WriteFixed64(int fieldNumber, ulong value)
    {
        WriteTag(fieldNumber, 1);
        Span<byte> buf = stackalloc byte[8];
        BitConverter.TryWriteBytes(buf, value);
        _stream.Write(buf);
    }
}

/// <summary>
/// Lightweight protobuf reader for decoding messages without generated code.
/// </summary>
public sealed class ProtobufReader
{
    private readonly byte[] _data;
    private int _pos;

    public ProtobufReader(byte[] data)
    {
        _data = data;
        _pos = 0;
    }

    public bool HasData => _pos < _data.Length;
    public int Position => _pos;

    public ulong ReadVarint()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            byte b = _data[_pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }

    public (int fieldNumber, int wireType) ReadTag()
    {
        var tag = ReadVarint();
        return ((int)(tag >> 3), (int)(tag & 0x07));
    }

    public byte[] ReadBytes()
    {
        var length = (int)ReadVarint();
        var bytes = new byte[length];
        Array.Copy(_data, _pos, bytes, 0, length);
        _pos += length;
        return bytes;
    }

    public string ReadString()
    {
        return Encoding.UTF8.GetString(ReadBytes());
    }

    public ulong ReadFixed64()
    {
        var value = BitConverter.ToUInt64(_data, _pos);
        _pos += 8;
        return value;
    }

    public uint ReadFixed32()
    {
        var value = BitConverter.ToUInt32(_data, _pos);
        _pos += 4;
        return value;
    }

    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case 0: ReadVarint(); break;           // varint
            case 1: _pos += 8; break;              // 64-bit
            case 2:                                // length-delimited
                int len = (int)ReadVarint();       // advances _pos past the length varint
                _pos += len;                       // then skip the data bytes
                break;
            case 3:                                // start group (deprecated)
                while (true)
                {
                    var (_, innerWireType) = ReadTag();
                    if (innerWireType == 4) break; // end group
                    Skip(innerWireType);
                }
                break;
            case 4: break;                         // end group (handled by case 3)
            case 5: _pos += 4; break;              // 32-bit
            default:
                throw new InvalidDataException($"Unknown wire type: {wireType}");
        }
    }
}
