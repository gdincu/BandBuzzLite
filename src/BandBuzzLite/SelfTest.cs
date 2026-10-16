using System.Text;
using BandBuzzLite.Band;

namespace BandBuzzLite;

/// <summary>Hardware-free checks: proto shape, crypto roundtrip, framing. No BLE, no toasts.</summary>
internal static class SelfTest
{
    public static int Run()
    {
        var failures = 0;
        void Check(bool ok, string name, string extra = "")
        {
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(extra.Length == 0 ? "" : " " + extra)}");
            if (!ok)
                failures++;
        }

        // 1. Step1 wire shape: Command{08 01, 10 1A, 1A len{...}} (matches pc_test build_step1).
        var phone = new byte[16];
        for (var i = 0; i < 16; i++) phone[i] = (byte)i;
        var s1 = XiaomiProto.BuildStep1(phone);
        Check(s1 is [0x08, 0x01, 0x10, 0x1A, ..], "step1-prefix", BitConverter.ToString(s1, 0, Math.Min(4, s1.Length)));
        var (t, st) = XiaomiProto.ParseCommandType(s1);
        Check(t == 1 && st == 26, "step1-type", $"type={t} sub={st}");
        var cmd = XiaomiProto.Parse(s1);
        var auth = XiaomiProto.Find(cmd, 3)!.Raw;
        var pn = XiaomiProto.Find(XiaomiProto.Parse(auth), 30)!.Raw;
        var nonce = XiaomiProto.Find(XiaomiProto.Parse(pn), 1)!.Raw;
        Check(nonce.SequenceEqual(phone), "step1-nonce-roundtrip");

        // 2. Notif shape parses back.
        var notif = XiaomiProto.BuildNotif("Hi", "Body here", "com.google.chrome", "Chrome", 2001);
        var (nt, nst) = XiaomiProto.ParseCommandType(notif);
        Check(nt == 7 && nst == 0, "notif-type", $"type={nt} sub={nst}");

        // 3. Title truncation: <=120B and still valid UTF-8 (cut on a char boundary).
        var built = XiaomiProto.BuildNotif(new string('ü', 200), "b", "p", "a", 1);
        var n3raw = XiaomiProto.Find(XiaomiProto.Parse(XiaomiProto.Find(XiaomiProto.Parse(XiaomiProto.Find(XiaomiProto.Parse(built), 9)!.Raw), 3)!.Raw), 1)!.Raw;
        var titleBytes = XiaomiProto.Find(XiaomiProto.Parse(n3raw), 3)!.Raw;
        Check(titleBytes.Length <= 120, "truncate-limit", $"len={titleBytes.Length}");
        var validUtf8 = true;
        try { new UTF8Encoding(false, true).GetString(titleBytes); }
        catch { validUtf8 = false; }
        Check(validUtf8, "truncate-boundary");

        // 4. Crypto roundtrip (BCL AES-CCM, tag 4) + wrong-key rejection.
        var key = XiaomiCrypto.RandomNonce();
        var n4 = new byte[] { 1, 2, 3, 4 };
        var pt = "hello band"u8.ToArray();
        var ct = XiaomiCrypto.Encrypt(key, n4, 1, pt);
        Check(ct.Length == pt.Length + 4, "ccm-tag-len", $"len={ct.Length}");
        Check(XiaomiCrypto.Decrypt(key, n4, 1, ct).SequenceEqual(pt), "ccm-roundtrip");
        var bad = false;
        try { XiaomiCrypto.Decrypt(new byte[16], n4, 1, ct); }
        catch { bad = true; }
        Check(bad, "ccm-wrong-key-fails");

        // 5. Step2 keys: 64B, deterministic, nonce-sensitive (mirrors pc_test step2_keys).
        var secret = Convert.FromHexString("00112233445566778899aabbccddeeff");
        var w = new byte[16]; w[0] = 9;
        var k1 = XiaomiCrypto.ComputeStep2Keys(secret, phone, w);
        var k2 = XiaomiCrypto.ComputeStep2Keys(secret, phone, w);
        Check(k1.Length == 64 && k1.SequenceEqual(k2), "hkdf-deterministic");
        var k3 = XiaomiCrypto.ComputeStep2Keys(secret, phone, new byte[16]);
        Check(!k1.SequenceEqual(k3), "hkdf-nonce-sensitive");

        // 6. Nonce12 layout: n4 + 00000000 + idx LE.
        var n12 = XiaomiCrypto.Nonce12([0xAA, 0xBB, 0xCC, 0xDD], 1);
        Check(n12.AsSpan().SequenceEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0, 0, 0, 0, 1, 0, 0, 0 }), "nonce12-layout");

        // 7. Framing: single roundtrip + ack decode; chunked split/reassemble.
        var single = XiaomiFrame.EncodeSingle([1, 2, 3], true, 7);
        Check(single.Length == 6 + 3 && single[2] == 2 && single[3] == 1, "frame-single-header");
        var dec = XiaomiFrame.Decode(single);
        Check(dec is XiaomiFrame.Incoming.Single s && s.Encrypted && s.Payload.SequenceEqual(new byte[] { 7, 0, 1, 2, 3 }),
            "frame-single-roundtrip");
        Check(XiaomiFrame.Decode([0, 0, 3, 0]) is XiaomiFrame.Incoming.Ack a && a.Result == 0, "frame-ack");
        var bigPayload = Enumerable.Range(0, 600).Select(i => (byte)(i & 0xFF)).ToArray();
        var count = XiaomiFrame.ChunkCount(bigPayload.Length, 244);
        Check(count == 3, "frame-chunk-count", $"count={count}");
        Check(XiaomiFrame.ShouldWriteChunked(bigPayload.Length, true, 244), "frame-chunk-decision");
        Check(!XiaomiFrame.ShouldWriteChunked(100, true, 244), "frame-single-decision");
        var start = XiaomiFrame.EncodeChunkedStart(count, true);
        var cs = (XiaomiFrame.Incoming.ChunkedStart)XiaomiFrame.Decode(start);
        Check(cs.Count == count && cs.Encrypted, "frame-chunkstart-roundtrip");
        var rx = new XiaomiFrame.Reassembler();
        rx.FeedStart(cs);
        byte[]? full = null;
        var stride = 244 - 2;
        for (var i = 1; i <= count; i++)
        {
            var from = (i - 1) * stride;
            var slice = bigPayload[from..Math.Min(i * stride, bigPayload.Length)];
            var pkt = XiaomiFrame.EncodeChunk(i, slice);
            full = rx.Feed((XiaomiFrame.Incoming.Chunk)XiaomiFrame.Decode(pkt));
        }
        Check(full is not null && full.SequenceEqual(bigPayload), "frame-reassemble");

        // 8. App slot mapping.
        Check(Program.SlotFor("Google Chrome").Nid == 2001, "slot-chrome");
        Check(Program.SlotFor("Microsoft Teams").Nid == 2002, "slot-teams");

        // 9. Cross-check vs pc_test vectors (nonce 00..0F; see README). step1 and
        // devinfo are key-independent; hkdf uses the dummy secret above.
        Check(Convert.ToHexString(s1).ToLowerInvariant()
            == "0801101a1a15f201120a10000102030405060708090a0b0c0d0e0f", "vector-step1");
        Check(Convert.ToHexString(XiaomiProto.BuildDeviceInfo()).ToLowerInvariant()
            == "080015000008421a0757696e646f777320e0012a02454e", "vector-devinfo");
        Check(Convert.ToHexString(XiaomiCrypto.ComputeStep2Keys(secret, phone, w)).ToLowerInvariant()
            == "14cce878b31ffea758056768c44ab3dfe8ce5200e9463b42e6d230515a28412977cace427bd09404c415f41fcf29b1ea03697a7557077b60bb3b6fa0af2dbda0",
            "vector-hkdf");

        // 10. FE95 family: same service/char UUIDs for all Xiaomi encrypted bands
        // (Gadgetbridge XiaomiUuids.BLE_UUIDS["0000fe95-…"] – Band 7 Pro, Band 8 Active, etc).
        Check(XiaomiProtocol.ServiceFe95 == Guid.Parse("0000fe95-0000-1000-8000-00805f9b34fb"), "fe95-uuid");
        Check(XiaomiProtocol.CharCommandRead == Guid.Parse("00000051-0000-1000-8000-00805f9b34fb"), "char-51-uuid");
        Check(XiaomiProtocol.CharCommandWrite == Guid.Parse("00000052-0000-1000-8000-00805f9b34fb"), "char-52-uuid");
        Check(XiaomiProtocol.SupportedDeviceExamples.Length >= 6, "supported-list");
        Check(XiaomiProtocol.SupportedDeviceExamples.Any(s => s.Contains("7 Pro")), "supported-7pro");
        Check(XiaomiProtocol.UnsupportedHuamiBand7Note.Contains("huami"), "unsupported-note");

        // 11. Huami/Zepp OS family: FEE1/3512 + endpoint 0x001e (Mi Band 7 plain etc).
        Check(HuamiProtocol.ServiceFee1 == Guid.Parse("0000fee1-0000-1000-8000-00805f9b34fb"), "huami-fee1-uuid");
        Check(HuamiProtocol.CharChunkedWrite2021 == Guid.Parse("00000016-0000-3512-2118-0009af100700"), "huami-0016-uuid");
        Check(HuamiProtocol.CharChunkedRead2021 == Guid.Parse("00000017-0000-3512-2118-0009af100700"), "huami-0017-uuid");
        Check(HuamiProtocol.EndpointNotification == 0x001e, "huami-endpoint-notif");
        Check(HuamiProtocol.EndpointAuth == 0x0082, "huami-endpoint-auth");
        Check(HuamiProtocol.SupportedHuamiExamples.Any(s => s.Contains("Band 7")), "huami-supported-band7");
        Check(HuamiProtocol.MiBand7Name == "Xiaomi Smart Band 7", "huami-miband7-name");
        // Huami frame + Zepp OS proto shape
        {
            var zeppNotif = ZeppOsProto.BuildNotification("Hi", "Body", "com.test", "Test", 123);
            Check(zeppNotif[0] == 0x03 && BitConverter.ToInt32(zeppNotif, 1) == 123 && zeppNotif[5] == 0xFA, "zepp-notif-shape");
            var caps = ZeppOsProto.BuildCapabilitiesRequest();
            Check(caps.Length == 1 && caps[0] == 0x01, "zepp-caps-shape");
        }
        // Huami crypto: AES-ECB roundtrip + CRC32 + ECDH B-163 pub generation
        {
            var huamiKey = new byte[16]; for (var i = 0; i < 16; i++) huamiKey[i] = (byte)i;
            var huamiPlain = new byte[16]; for (var i = 0; i < 16; i++) huamiPlain[i] = (byte)(i * 2);
            var huamiEnc = HuamiFrame.AesEcbEncrypt(huamiPlain, huamiKey);
            var huamiDec = HuamiFrame.AesEcbDecrypt(huamiEnc, huamiKey);
            Check(huamiDec.SequenceEqual(huamiPlain), "huami-aes-ecb-roundtrip");
            var huamiPayload = "hello huami"u8.ToArray();
            var huamiEnc2 = new HuamiFrame.Encoder { Mtu = 247 };
            huamiEnc2.SetEncryptionParameters(12345, huamiKey);
            var huamiChunks = huamiEnc2.Encode(0x001e, huamiPayload, true);
            Check(huamiChunks.Count >= 1 && huamiChunks[0][0] == 0x03, "huami-chunked-encode");
            var huamiDec2 = new HuamiFrame.Decoder();
            huamiDec2.SetEncryptionParameters(huamiKey);
            byte[]? huamiReassembled = null;
            foreach (var c in huamiChunks) { var r = huamiDec2.Decode(c); if (r != null) huamiReassembled = r.Value.payload; }
            Check(huamiReassembled != null && huamiReassembled.SequenceEqual(huamiPayload), "huami-chunked-roundtrip");
            var (huamiPrv, huamiPub) = HuamiCrypto.GenerateKeyPair();
            Check(huamiPrv.Length == 24 && huamiPub.Length == 48, "huami-ecdh-keypair-size");
            var huamiRemotePrv = HuamiCrypto.RandomPrivateEC();
            var huamiRemotePub = EcdhB163.GeneratePublic(huamiRemotePrv)!;
            var huamiShared1 = EcdhB163.GenerateShared(huamiPrv, huamiRemotePub)!;
            var huamiShared2 = EcdhB163.GenerateShared(huamiRemotePrv, huamiPub)!;
            Check(huamiShared1.AsSpan(0, 24).SequenceEqual(huamiShared2.AsSpan(0, 24)), "huami-ecdh-shared");
        }

        Console.WriteLine(failures == 0 ? "SELFTEST OK" : $"SELFTEST {failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }
}
