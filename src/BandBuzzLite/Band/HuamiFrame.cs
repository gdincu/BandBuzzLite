using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BandBuzzLite.Band;

/// <summary>
/// Huami 2021 chunked framing (Zepp OS force2021=true). Port of Huami2021ChunkedEncoder/Decoder.
/// Wire: 0x03 flags 0x00 handle count [origLen LE32 type LE16 if first] payload; flags 0x01 first / 0x02 last / 0x08 encrypted.
/// </summary>
internal static class HuamiFrame
{
    public static byte[] AesEcbEncrypt(byte[] plain, byte[] key16)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key16;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(plain, 0, plain.Length);
    }

    public static byte[] AesEcbDecrypt(byte[] cipher, byte[] key16)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key16;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data) { crc ^= b; for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1; }
        return crc ^ 0xFFFFFFFFu;
    }

    public sealed class Encoder
    {
        private byte _writeHandle;
        private int _seq;
        private byte[]? _key;
        public int Mtu { get; set; } = 247;

        public void SetEncryptionParameters(int seq, byte[] key16) { _seq = seq; _key = key16; }

        public List<byte[]> Encode(short type, byte[] data, bool encrypt)
        {
            if (encrypt && _key is null) throw new InvalidOperationException("No session key");
            _writeHandle++;
            var origLen = data.Length;
            byte[] payload = data;
            int remaining = data.Length;

            if (encrypt)
            {
                var msgKey = new byte[16];
                for (var i = 0; i < 16; i++) msgKey[i] = (byte)(_key![i] ^ _writeHandle);
                var encLen = origLen + 8;
                var over = encLen % 16;
                if (over != 0) encLen += 16 - over;
                var tmp = new byte[encLen];
                Buffer.BlockCopy(data, 0, tmp, 0, origLen);
                BinaryPrimitives.WriteInt32LittleEndian(tmp.AsSpan(origLen), _seq++);
                BinaryPrimitives.WriteUInt32LittleEndian(tmp.AsSpan(origLen + 4), Crc32(tmp.AsSpan(0, origLen + 4)));
                payload = AesEcbEncrypt(tmp, msgKey);
                remaining = encLen;
            }

            var chunks = new List<byte[]>();
            byte count = 0;
            var header = 11;
            while (remaining > 0)
            {
                var max = Mtu - 3 - header;
                var copy = Math.Min(remaining, max);
                var chunk = new byte[copy + header];
                byte flags = 0;
                if (encrypt) flags |= 0x08;
                if (count == 0) flags |= 0x01;
                if (remaining <= max) flags |= 0x06;
                chunk[0] = 0x03;
                chunk[1] = flags;
                chunk[2] = 0;
                chunk[3] = _writeHandle;
                chunk[4] = count;
                if (count == 0)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(5), origLen);
                    BinaryPrimitives.WriteInt16LittleEndian(chunk.AsSpan(9), type);
                }
                Buffer.BlockCopy(payload, payload.Length - remaining, chunk, header, copy);
                chunks.Add(chunk);
                remaining -= copy;
                header = 5;
                count++;
            }
            return chunks;
        }
    }

    public sealed class Decoder
    {
        private byte? _curHandle;
        private short _curType;
        private int _origLen;
        private MemoryStream? _buf;
        private byte[]? _key;
        public byte LastHandle { get; private set; }
        public byte LastCount { get; private set; }

        public void SetEncryptionParameters(byte[] key16) => _key = key16;

        public (bool needsAck, short type, byte[] payload)? Decode(byte[] data)
        {
            if (data.Length < 4 || data[0] != 0x03) return null;
            var flags = data[1];
            var encrypted = (flags & 0x08) != 0;
            var first = (flags & 0x01) != 0;
            var last = (flags & 0x02) != 0;
            var needsAck = (flags & 0x04) != 0;
            var idx = 3; // skip 0x03 flags 0x00
            var handle = data[idx++];
            if (_curHandle.HasValue && _curHandle.Value != handle) return null;
            LastHandle = handle;
            LastCount = data[idx++];
            if (first)
            {
                _origLen = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(idx)); idx += 4;
                _curType = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(idx)); idx += 2;
                _buf = new MemoryStream();
                _curHandle = handle;
            }
            if (_buf is null) return null;
            _buf.Write(data, idx, data.Length - idx);
            if (!last) return null;
            var buf = _buf.ToArray();
            _buf = null; _curHandle = null;
            if (encrypted)
            {
                if (_key is null) return null;
                var msgKey = new byte[16];
                for (var i = 0; i < 16; i++) msgKey[i] = (byte)(_key[i] ^ handle);
                try
                {
                    buf = AesEcbDecrypt(buf, msgKey);
                    if (buf.Length > _origLen) buf = buf[.._origLen];
                }
                catch { return null; }
            }
            else
            {
                if (buf.Length > _origLen) buf = buf[.._origLen];
            }
            var type = _curType; _curType = 0;
            return (needsAck, type, buf);
        }
    }

    public static byte[] BuildChunkedAck(byte handle, byte count) => [0x04, 0x00, handle, 0x01, count];
}
