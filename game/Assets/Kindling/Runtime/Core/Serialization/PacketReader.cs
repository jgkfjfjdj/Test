using System;
using System.Text;

namespace Kindling.Core.Serialization
{
    /// <summary>
    /// Little-endian binary reader over a byte range. Every read is bounds-checked: reading past the end
    /// throws <see cref="ProtocolException"/> instead of returning garbage.
    /// </summary>
    public struct PacketReader
    {
        readonly byte[] m_Data;
        readonly int m_End;
        int m_Position;

        public PacketReader(byte[] data, int offset, int count)
        {
            if (data == null || offset < 0 || count < 0 || offset + count > data.Length)
            {
                throw new ProtocolException("Invalid packet range.");
            }

            m_Data = data;
            m_Position = offset;
            m_End = offset + count;
        }

        public PacketReader(byte[] data)
            : this(data, 0, data?.Length ?? 0)
        {
        }

        public int Remaining => m_End - m_Position;

        public bool AtEnd => m_Position >= m_End;

        public byte ReadByte()
        {
            Require(1);
            return m_Data[m_Position++];
        }

        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        public bool ReadBool()
        {
            byte b = ReadByte();
            if (b > 1)
            {
                throw new ProtocolException("Invalid bool.");
            }

            return b == 1;
        }

        public ushort ReadUShort()
        {
            Require(2);
            int v = m_Data[m_Position] | (m_Data[m_Position + 1] << 8);
            m_Position += 2;
            return (ushort)v;
        }

        public short ReadShort() => unchecked((short)ReadUShort());

        public int ReadInt24()
        {
            Require(3);
            int v = m_Data[m_Position] | (m_Data[m_Position + 1] << 8) | (m_Data[m_Position + 2] << 16);
            m_Position += 3;
            return (v << 8) >> 8; // sign-extend
        }

        public uint ReadUInt()
        {
            Require(4);
            uint v = (uint)(m_Data[m_Position] | (m_Data[m_Position + 1] << 8) | (m_Data[m_Position + 2] << 16) | (m_Data[m_Position + 3] << 24));
            m_Position += 4;
            return v;
        }

        public int ReadInt() => unchecked((int)ReadUInt());

        public float ReadFloat()
        {
            float value = BitConverter.Int32BitsToSingle(ReadInt());
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ProtocolException("Non-finite float.");
            }

            return value;
        }

        public uint ReadVarUInt()
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = ReadByte();
                if (shift == 28 && b > 0x0F)
                {
                    throw new ProtocolException("VarUInt overflow.");
                }

                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return result;
                }
            }

            throw new ProtocolException("VarUInt too long.");
        }

        public byte[] ReadBlob(int maxLength)
        {
            uint count = ReadVarUInt();
            if (count > maxLength)
            {
                throw new ProtocolException($"Blob longer than {maxLength} bytes.");
            }

            Require((int)count);
            var result = new byte[count];
            System.Buffer.BlockCopy(m_Data, m_Position, result, 0, (int)count);
            m_Position += (int)count;
            return result;
        }

        public string ReadString(int maxBytes = 256)
        {
            uint count = ReadVarUInt();
            if (count > maxBytes)
            {
                throw new ProtocolException($"String longer than {maxBytes} bytes.");
            }

            Require((int)count);
            string value = Encoding.UTF8.GetString(m_Data, m_Position, (int)count);
            m_Position += (int)count;
            return value;
        }

        void Require(int count)
        {
            if (count < 0 || m_Position + count > m_End)
            {
                throw new ProtocolException("Unexpected end of packet.");
            }
        }
    }
}
