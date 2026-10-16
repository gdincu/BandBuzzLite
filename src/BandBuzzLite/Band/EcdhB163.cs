// Port of Gadgetbridge util/ECDH_B163.java (tiny-ECDH B-163, NIST B-163)
// Original: https://github.com/kokke/tiny-ECDH-c/, ported to Java by Gadgetbridge.
// Line-for-line C# port; uses int[] with unchecked casts to preserve Java/C parity.
// uint[] would avoid unchecked (e.g. 0xb8c953ca) but is left as int[] for fidelity.

namespace BandBuzzLite.Band;

internal static class EcdhB163
{
    const int CURVE_DEGREE = 163;
    const int ECC_PRV_KEY_SIZE = 24;
    const int ECC_PUB_KEY_SIZE = 2 * ECC_PRV_KEY_SIZE;
    const int BITVEC_MARGIN = 3;
    const int BITVEC_NBITS = (CURVE_DEGREE + BITVEC_MARGIN);
    const int BITVEC_NWORDS = ((BITVEC_NBITS + 31) / 32);
    const int BITVEC_NBYTES = (4 * BITVEC_NWORDS);

    static readonly int[] polynomial = [0x000000c9, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000008];
    static readonly int[] coeff_b = [unchecked((int)0x4a3205fd), unchecked((int)0x512f7874), unchecked((int)0x1481eb10), unchecked((int)0xb8c953ca), unchecked((int)0x0a601907), unchecked((int)0x00000002)];
    static readonly int[] base_x = [unchecked((int)0xe8343e36), unchecked((int)0xd4994637), unchecked((int)0xa0991168), unchecked((int)0x86a2d57e), unchecked((int)0xf0eba162), unchecked((int)0x00000003)];
    static readonly int[] base_y = [unchecked((int)0x797324f1), unchecked((int)0xb11c5c0c), unchecked((int)0xa2cdd545), unchecked((int)0x71a0094f), unchecked((int)0xd51fbc6c), unchecked((int)0x00000000)];
    static readonly int[] base_order = [unchecked((int)0xa4234c33), unchecked((int)0x77e70c12), unchecked((int)0x000292fe), unchecked((int)0x00000000), unchecked((int)0x00000000), unchecked((int)0x00000004)];

    static int bitvec_get_bit(int[] x, int idx) => (int)(((x[idx / 32] >> (idx & 31)) & 1) & 1);
    static void bitvec_clr_bit(int[] x, int idx) => x[idx / 32] &= ~(1 << (idx & 31));
    static void bitvec_copy(int[] x, int[] y) { for (int i = 0; i < BITVEC_NWORDS; i++) x[i] = y[i]; }
    static void bitvec_swap(int[] x, int[] y) { var tmp = new int[BITVEC_NWORDS]; bitvec_copy(tmp, x); bitvec_copy(x, y); bitvec_copy(y, tmp); }
    static bool bitvec_equal(int[] x, int[] y) { for (int i = 0; i < BITVEC_NWORDS; i++) if (x[i] != y[i]) return false; return true; }
    static void bitvec_set_zero(int[] x) { for (int i = 0; i < BITVEC_NWORDS; i++) x[i] = 0; }
    static bool bitvec_is_zero(int[] x) { for (int i = 0; i < BITVEC_NWORDS; i++) if (x[i] != 0) return false; return true; }
    static int bitvec_degree(int[] x)
    {
        int i = BITVEC_NWORDS * 32; int y = BITVEC_NWORDS;
        while (i > 0 && x[--y] == 0) i -= 32;
        if (i != 0) { int m = 1 << 31; while ((x[y] & m) == 0) { m = (int)((uint)m >> 1); i--; } }
        return i;
    }
    static void bitvec_lshift(int[] x, int[] y, int nbits)
    {
        int nwords = nbits / 32; int i, j;
        for (i = 0; i < nwords; i++) x[i] = 0;
        j = 0; while (i < BITVEC_NWORDS) { x[i] = y[j]; i++; j++; }
        nbits &= 31;
        if (nbits != 0) { for (i = BITVEC_NWORDS - 1; i > 0; --i) x[i] = (int)(((uint)x[i] << nbits) | ((uint)x[i - 1] >> (32 - nbits))); x[0] = (int)((uint)x[0] << nbits); }
    }

    static void gf2field_set_one(int[] x) { x[0] = 1; for (int i = 1; i < BITVEC_NWORDS; i++) x[i] = 0; }
    static bool gf2field_is_one(int[] x) { if (x[0] != 1) return false; for (int i = 1; i < BITVEC_NWORDS; i++) if (x[i] != 0) return false; return true; }
    static void gf2field_add(int[] z, int[] x, int[] y) { for (int i = 0; i < BITVEC_NWORDS; i++) z[i] = x[i] ^ y[i]; }
    static void gf2field_inc(int[] x) => x[0] ^= 1;
    static void gf2field_mul(int[] z, int[] x, int[] y)
    {
        int[] tmp = new int[BITVEC_NWORDS]; bitvec_copy(tmp, x);
        if (bitvec_get_bit(y, 0) != 0) bitvec_copy(z, x); else bitvec_set_zero(z);
        for (int i = 1; i < CURVE_DEGREE; i++)
        {
            bitvec_lshift(tmp, tmp, 1);
            if (bitvec_get_bit(tmp, CURVE_DEGREE) != 0) gf2field_add(tmp, tmp, polynomial);
            if (bitvec_get_bit(y, i) != 0) gf2field_add(z, z, tmp);
        }
    }
    static void gf2field_inv(int[] z, int[] x)
    {
        int[] u = new int[BITVEC_NWORDS], v = new int[BITVEC_NWORDS], g = new int[BITVEC_NWORDS], h = new int[BITVEC_NWORDS];
        bitvec_copy(u, x); bitvec_copy(v, polynomial); bitvec_set_zero(g); gf2field_set_one(z);
        while (!gf2field_is_one(u))
        {
            int i = bitvec_degree(u) - bitvec_degree(v);
            if (i < 0) { bitvec_swap(u, v); bitvec_swap(g, z); i = -i; }
            bitvec_lshift(h, v, i); gf2field_add(u, u, h); bitvec_lshift(h, g, i); gf2field_add(z, z, h);
        }
    }

    static void gf2point_copy(int[] x1, int[] y1, int[] x2, int[] y2) { bitvec_copy(x1, x2); bitvec_copy(y1, y2); }
    static void gf2point_set_zero(int[] x, int[] y) { bitvec_set_zero(x); bitvec_set_zero(y); }
    static bool gf2point_is_zero(int[] x, int[] y) => bitvec_is_zero(x) && bitvec_is_zero(y);
    static void gf2point_double(int[] x, int[] y)
    {
        if (bitvec_is_zero(x)) bitvec_set_zero(y);
        else
        {
            int[] l = new int[BITVEC_NWORDS];
            gf2field_inv(l, x); gf2field_mul(l, l, y); gf2field_add(l, l, x); gf2field_mul(y, x, x); gf2field_mul(x, l, l); gf2field_inc(l); gf2field_add(x, x, l); gf2field_mul(l, l, x); gf2field_add(y, y, l);
        }
    }
    static void gf2point_add(int[] x1, int[] y1, int[] x2, int[] y2)
    {
        if (!gf2point_is_zero(x2, y2))
        {
            if (gf2point_is_zero(x1, y1)) gf2point_copy(x1, y1, x2, y2);
            else if (bitvec_equal(x1, x2))
            {
                if (bitvec_equal(y1, y2)) gf2point_double(x1, y1); else gf2point_set_zero(x1, y1);
            }
            else
            {
                int[] a = new int[BITVEC_NWORDS], b = new int[BITVEC_NWORDS], c = new int[BITVEC_NWORDS], d = new int[BITVEC_NWORDS];
                gf2field_add(a, y1, y2); gf2field_add(b, x1, x2); gf2field_inv(c, b); gf2field_mul(c, c, a); gf2field_mul(d, c, c); gf2field_add(d, d, c); gf2field_add(d, d, b); gf2field_inc(d); gf2field_add(x1, x1, d); gf2field_mul(a, x1, c); gf2field_add(a, a, d); gf2field_add(y1, y1, a); bitvec_copy(x1, d);
            }
        }
    }
    static void gf2point_mul(int[] x, int[] y, int[] exp)
    {
        int[] tmpx = new int[BITVEC_NWORDS], tmpy = new int[BITVEC_NWORDS];
        int nbits = bitvec_degree(exp); gf2point_set_zero(tmpx, tmpy);
        for (int i = nbits - 1; i >= 0; --i) { gf2point_double(tmpx, tmpy); if (bitvec_get_bit(exp, i) != 0) gf2point_add(tmpx, tmpy, x, y); }
        gf2point_copy(x, y, tmpx, tmpy);
    }
    static bool gf2point_on_curve(int[] x, int[] y)
    {
        if (gf2point_is_zero(x, y)) return false;
        int[] a = new int[BITVEC_NWORDS], b = new int[BITVEC_NWORDS];
        gf2field_mul(a, x, x); gf2field_mul(b, a, x); gf2field_add(a, a, b); gf2field_add(a, a, coeff_b); gf2field_mul(b, y, y); gf2field_add(a, a, b); gf2field_mul(b, x, y); return bitvec_equal(a, b);
    }
    static int[] bytes_to_int(byte[] bytes, int offset)
    {
        var v = new int[BITVEC_NWORDS];
        int p = offset; for (int i = 0; i < BITVEC_NWORDS; i++) { v[i] = (bytes[p++] & 0xff) | ((bytes[p++] & 0xff) << 8) | ((bytes[p++] & 0xff) << 16) | ((bytes[p++] & 0xff) << 24); }
        return v;
    }
    static void ints_to_bytes(byte[] bytes, int[] ints, int offset)
    {
        int p = offset; for (int i = 0; i < BITVEC_NWORDS; i++) { bytes[p++] = (byte)(ints[i] & 0xff); bytes[p++] = (byte)((ints[i] >> 8) & 0xff); bytes[p++] = (byte)((ints[i] >> 16) & 0xff); bytes[p++] = (byte)((ints[i] >> 24) & 0xff); }
    }

    static bool ecdh_generate_keys(byte[] pub, byte[] prv)
    {
        var prv32 = bytes_to_int(prv, 0); var pub1 = bytes_to_int(pub, 0); var pub2 = bytes_to_int(pub, BITVEC_NBYTES);
        gf2point_copy(pub1, pub2, base_x, base_y);
        if (bitvec_degree(prv32) < CURVE_DEGREE / 2) return false;
        int nbits = bitvec_degree(base_order); for (int i = nbits - 1; i < BITVEC_NWORDS * 32; i++) bitvec_clr_bit(prv32, i);
        gf2point_mul(pub1, pub2, prv32); ints_to_bytes(pub, pub1, 0); ints_to_bytes(pub, pub2, BITVEC_NBYTES); return true;
    }
    static bool ecdh_shared_secret(byte[] prv, byte[] otherPub, byte[] output)
    {
        var prv32 = bytes_to_int(prv, 0); var o1 = bytes_to_int(otherPub, 0); var o2 = bytes_to_int(otherPub, BITVEC_NBYTES);
        if (gf2point_is_zero(o1, o2) || !gf2point_on_curve(o1, o2)) return false;
        for (int i = 0; i < BITVEC_NBYTES * 2; i++) output[i] = otherPub[i];
        int nbits = bitvec_degree(base_order); for (int i = nbits - 1; i < BITVEC_NWORDS * 32; i++) bitvec_clr_bit(prv32, i);
        var out1 = bytes_to_int(output, 0); var out2 = bytes_to_int(output, BITVEC_NBYTES); gf2point_mul(out1, out2, prv32); ints_to_bytes(output, out1, 0); ints_to_bytes(output, out2, BITVEC_NBYTES); return true;
    }

    public static byte[]? GeneratePublic(byte[] privateEC)
    {
        var pub = new byte[ECC_PUB_KEY_SIZE];
        return ecdh_generate_keys(pub, privateEC) ? pub : null;
    }
    public static byte[]? GenerateShared(byte[] privateEC, byte[] remotePub)
    {
        var shared = new byte[ECC_PUB_KEY_SIZE];
        return ecdh_shared_secret(privateEC, remotePub, shared) ? shared : null;
    }
}
