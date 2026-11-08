using System;
using System.Text;

namespace Kindling.Core.Serialization
{
    /// <summary>
    /// Little-endian binary writer over a growable buffer. Reused between messages (call
    /// <see cref="Reset"/>) so steady-state sending doesn't allocate.
    /// </summary>
    public sealed class PacketWriter
    {
        byte[] m_Buffer;
        int m_Length;

        public PacketWriter(int capacity = 1500)
        {
            m_Buffer = new byte[Math.Max(16, capacity)];
        }

        public byte[] Buffer => m_Buffer;

        public int Length => m_Length;

        public void Reset() => m_Length = 0;

        /// <summary>Copies the written bytes into a new array.</summary>
        public byte[] ToArray()
        {
            var result = new byte[m_Length];
            System.Buffer.BlockCopy(m_Buffer, 0, result, 0, m_Length);
            return result;
        }

        /// <summary>Rewinds to an earlier length (drops what was written after it).</summary>
        public void Truncate(int length)
        {
            if (length < 0 || length > m_Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            m_Length = length;
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            m_Buffer[m_Length++] = value;
        }

        public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUShort(ushort value)
        {
            Ensure(2);
            m_Buffer[m_Length++] = (byte)value;
            m_Buffer[m_Length++] = (byte)(value >> 8);
        }

        public void WriteShort(short value) => WriteUShort(unchecked((ushort)value));

        /// <summary>Signed 24-bit integer (range ±8,388,607).</summary>
        public void WriteInt24(int value)
        {
            if (value < -8388608 || value > 8388607)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Out of 24-bit range.");
            }

            Ensure(3);
            m_Buffer[m_Length++] = (byte)value;
            m_Buffer[m_Length++] = (byte)(value >> 8);
            m_Buffer[m_Length++] = (byte)(value >> 16);
        }

        public void WriteUInt(uint value)
        {
            Ensure(4);
            m_Buffer[m_Length++] = (byte)value;
            m_Buffer[m_Length++] = (byte)(value >> 8);
            m_Buffer[m_Length++] = (byte)(value >> 16);
            m_Buffer[m_Length++] = (byte)(value >> 24);
        }

        public void WriteInt(int value) => WriteUInt(unchecked((uint)value));

        public void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));

        /// <summary>Unsigned LEB128 variable-length integer (1 byte below 128).</summary>
        public void WriteVarUInt(uint value)
        {
            while (value >= 0x80)
            {
                WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            WriteByte((byte)value);
        }

        public void WriteBytes(byte[] data, int offset, int count)
        {
            if (count == 0)
            {
                return;
            }

            Ensure(count);
            System.Buffer.BlockCopy(data, offset, m_Buffer, m_Length, count);
            m_Length += count;
        }

        /// <summary>Length-prefixed byte array (null is written as empty).</summary>
        public void WriteBlob(byte[] data)
        {
            int count = data?.Length ?? 0;
            WriteVarUInt((uint)count);
            if (count > 0)
            {
                WriteBytes(data, 0, count);
            }
        }

        /// <summary>Length-prefixed UTF-8 string, truncated to <paramref name="maxBytes"/> on a character boundary.</summary>
        public void WriteString(string value, int maxBytes = 256)
        {
            value ??= string.Empty;
            int count = Encoding.UTF8.GetByteCount(value);
            if (count > maxBytes)
            {
                value = Truncate(value, maxBytes);
                count = Encoding.UTF8.GetByteCount(value);
            }

            WriteVarUInt((uint)count);
            Ensure(count);
            m_Length += Encoding.UTF8.GetBytes(value, 0, value.Length, m_Buffer, m_Length);
        }

        static string Truncate(string value, int maxBytes)
        {
            int bytes = 0;
            int i = 0;
            while (i < value.Length)
            {
                int charCount = char.IsHighSurrogate(value[i]) && i + 1 < value.Length ? 2 : 1;
                int size = Encoding.UTF8.GetByteCount(value.ToCharArray(i, charCount));
                if (bytes + size > maxBytes)
                {
                    break;
                }

                bytes += size;
                i += charCount;
            }

            return value.Substring(0, i);
        }

        void Ensure(int extra)
        {
            int needed = m_Length + extra;
            if (needed <= m_Buffer.Length)
            {
                return;
            }

            int size = m_Buffer.Length * 2;
            while (size < needed)
            {
                size *= 2;
            }

            Array.Resize(ref m_Buffer, size);
        }
    }
}
