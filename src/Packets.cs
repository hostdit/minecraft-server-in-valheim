using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace McValheim
{
    public class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }

    public static class VarInt
    {
        public static byte[] encode(int value)
        {
            var bytes = new List<byte>(5);
            var v = unchecked((uint)value);
            do
            {
                var b = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0) b |= 0x80;
                bytes.Add(b);
            } while (v != 0);
            return bytes.ToArray();
        }

        public static bool tryDecode(byte[] buffer, int offset, int length, out int value, out int bytesRead)
        {
            value = 0;
            bytesRead = 0;
            for (var i = 0; i < 5; i++)
            {
                if (offset + i >= offset + length) return false;
                if (i >= length) return false;
                var b = buffer[offset + i];
                value |= (b & 0x7F) << (7 * i);
                bytesRead = i + 1;
                if ((b & 0x80) == 0) return true;
            }
            throw new ProtocolException("varint longer than five bytes");
        }
    }

    public class PacketWriter
    {
        private readonly MemoryStream stream = new MemoryStream();

        public PacketWriter(int packetId)
        {
            writeVarInt(packetId);
        }

        public void writeVarInt(int value)
        {
            var bytes = VarInt.encode(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        public void writeByte(byte value)
        {
            stream.WriteByte(value);
        }

        public void writeBool(bool value)
        {
            stream.WriteByte(value ? (byte)1 : (byte)0);
        }

        public void writeShort(short value)
        {
            writeBigEndian(BitConverter.GetBytes(value));
        }

        public void writeUShort(ushort value)
        {
            writeBigEndian(BitConverter.GetBytes(value));
        }

        public void writeInt(int value)
        {
            writeBigEndian(BitConverter.GetBytes(value));
        }

        public void writeLong(long value)
        {
            writeBigEndian(BitConverter.GetBytes(value));
        }

        public void writeFloat(float value)
        {
            writeBigEndian(BitConverter.GetBytes(value));
        }

        public void writeDouble(double value)
        {
            writeBigEndian(BitConverter.GetBytes(value));
        }

        public void writeString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writeVarInt(bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        public void writeBytes(byte[] value)
        {
            stream.Write(value, 0, value.Length);
        }

        public void writePosition(int x, int y, int z)
        {
            var packed = ((long)(x & 0x3FFFFFF) << 38) | ((long)(y & 0xFFF) << 26) | (long)(z & 0x3FFFFFF);
            writeLong(packed);
        }

        public void writeFixedPoint(double value)
        {
            writeInt((int)Math.Round(value * 32.0));
        }

        public void writeAngle(float degrees)
        {
            var wrapped = degrees % 360f;
            if (wrapped < 0) wrapped += 360f;
            writeByte((byte)((int)Math.Round(wrapped * 256f / 360f) & 0xFF));
        }

        public void writeUuid(Guid value)
        {
            writeBytes(uuidBytes(value));
        }

        public static byte[] uuidBytes(Guid value)
        {
            var bytes = value.ToByteArray();
            Array.Reverse(bytes, 0, 4);
            Array.Reverse(bytes, 4, 2);
            Array.Reverse(bytes, 6, 2);
            return bytes;
        }

        public byte[] frame()
        {
            var body = stream.ToArray();
            var length = VarInt.encode(body.Length);
            var framed = new byte[length.Length + body.Length];
            Buffer.BlockCopy(length, 0, framed, 0, length.Length);
            Buffer.BlockCopy(body, 0, framed, length.Length, body.Length);
            return framed;
        }

        private void writeBigEndian(byte[] bytes)
        {
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    public class PacketReader
    {
        private readonly byte[] data;
        private int offset;

        public int packetId { get; private set; }

        public PacketReader(byte[] body)
        {
            data = body;
            offset = 0;
            packetId = readVarInt();
        }

        public int remaining => data.Length - offset;

        public int readVarInt()
        {
            int value;
            int read;
            if (!VarInt.tryDecode(data, offset, data.Length - offset, out value, out read))
                throw new ProtocolException("truncated varint");
            offset += read;
            return value;
        }

        public byte readByte()
        {
            if (offset >= data.Length) throw new ProtocolException("truncated byte");
            return data[offset++];
        }

        public bool readBool()
        {
            return readByte() != 0;
        }

        public ushort readUShort()
        {
            return BitConverter.ToUInt16(bigEndian(2), 0);
        }

        public int readInt()
        {
            return BitConverter.ToInt32(bigEndian(4), 0);
        }

        public long readLong()
        {
            return BitConverter.ToInt64(bigEndian(8), 0);
        }

        public float readFloat()
        {
            return BitConverter.ToSingle(bigEndian(4), 0);
        }

        public double readDouble()
        {
            return BitConverter.ToDouble(bigEndian(8), 0);
        }

        public string readString()
        {
            var length = readVarInt();
            if (length < 0 || offset + length > data.Length) throw new ProtocolException("truncated string");
            var value = Encoding.UTF8.GetString(data, offset, length);
            offset += length;
            return value;
        }

        private byte[] bigEndian(int count)
        {
            if (offset + count > data.Length) throw new ProtocolException("truncated number");
            var bytes = new byte[count];
            Buffer.BlockCopy(data, offset, bytes, 0, count);
            offset += count;
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return bytes;
        }
    }

    public class PacketStream
    {
        private const int maxPacketLength = 2 * 1024 * 1024;

        private byte[] buffer = new byte[0];

        public void feed(byte[] chunk, int count)
        {
            var grown = new byte[buffer.Length + count];
            Buffer.BlockCopy(buffer, 0, grown, 0, buffer.Length);
            Buffer.BlockCopy(chunk, 0, grown, buffer.Length, count);
            buffer = grown;
        }

        public byte[] next()
        {
            int length;
            int headerSize;
            if (!VarInt.tryDecode(buffer, 0, buffer.Length, out length, out headerSize)) return null;
            if (length < 0 || length > maxPacketLength) throw new ProtocolException("packet length out of range: " + length);
            if (buffer.Length - headerSize < length) return null;

            var body = new byte[length];
            Buffer.BlockCopy(buffer, headerSize, body, 0, length);

            var rest = new byte[buffer.Length - headerSize - length];
            Buffer.BlockCopy(buffer, headerSize + length, rest, 0, rest.Length);
            buffer = rest;

            return body;
        }
    }
}
