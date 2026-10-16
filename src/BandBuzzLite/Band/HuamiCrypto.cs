using System.Security.Cryptography;

namespace BandBuzzLite.Band;

/// <summary>
/// Huami/Zepp OS 2021 crypto, port of InitOperation2021 + CryptoUtils + CheckSums.
/// Gadgetbridge: service/devices/huami/operations/init/InitOperation2021.java:92-176,
/// util/CryptoUtils.java (AES/ECB), util/CheckSums.java (CRC32)
/// AuthKey (16B) is same file as Xiaomi (32 hex), used XORed into ECDH shared point.
/// </summary>
internal static class HuamiCrypto
{
    public static byte[] RandomPrivateEC()
    {
        // 24B random, but must pass degree >= 81; retry like ecdh_generate_keys does
        while (true)
        {
            var prv = new byte[24];
            RandomNumberGenerator.Fill(prv);
            var pub = EcdhB163.GeneratePublic(prv);
            if (pub is not null) return prv;
        }
    }

    /// <summary>
    /// Step1: generate ephemeral privateEC (24B) + publicEC (48B) -> send 04 02 00 02 + 48B via endpoint 0x0082.
    /// Callee returns 16B remoteRandom + 48B remotePublicEC via decode of 10 04 01.
    /// </summary>
    public static (byte[] privateEC, byte[] publicEC) GenerateKeyPair()
    {
        var prv = RandomPrivateEC();
        var pub = EcdhB163.GeneratePublic(prv)!;
        return (prv, pub);
    }

    /// <summary>
    /// Derive finalSharedSession (16B) and encryptedSequenceNr from sharedEC (48B) + secretKey (16B authKey).
    /// Gadgetbridge InitOperation2021.java:154-162
    /// </summary>
    public static (int seq, byte[] sessionKey) DeriveSession(byte[] sharedEC48, byte[] secretKey16)
    {
        var seq = BitConverter.ToInt32(sharedEC48, 0); // LE32(sharedEC[0..3])
        var key = new byte[16];
        for (var i = 0; i < 16; i++) key[i] = (byte)(sharedEC48[i + 8] ^ secretKey16[i]);
        return (seq, key);
    }

    public static byte[] AesEcb(byte[] plain16aligned, byte[] key16) => HuamiFrame.AesEcbEncrypt(plain16aligned, key16);

    /// <summary>
    /// Build 33B auth reply: 05 + AES(secretKey, remoteRandom)[16] + AES(finalSession, remoteRandom)[16]
    /// Gadgetbridge InitOperation2021.java:166-176
    /// </summary>
    public static byte[] BuildAuthStep2(byte[] secretKey16, byte[] sessionKey16, byte[] remoteRandom16)
    {
        var out_ = new byte[33];
        out_[0] = 0x05;
        Buffer.BlockCopy(AesEcb(remoteRandom16, secretKey16), 0, out_, 1, 16);
        Buffer.BlockCopy(AesEcb(remoteRandom16, sessionKey16), 0, out_, 17, 16);
        return out_;
    }
}
