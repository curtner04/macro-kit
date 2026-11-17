using System;
using System.Numerics;
using System.Security.Cryptography;

namespace MacroKit.Core
{
    /// <summary>
    /// Ed25519 signatures (RFC 8032), written on BigInteger because .NET Framework 4.8 has none built in.
    /// Macro Kit only verifies (a license key, once, when it loads). Sign and PublicKey exist for the publisher's private
    /// key generator, which compiles this same file. Clarity beats speed here, and it is not constant-time, which does not
    /// matter for verifying a public signature.
    /// </summary>
    public static class Ed25519
    {
        public const int PublicKeySize = 32, SecretKeySize = 32, SignatureSize = 64;

        static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
        static readonly BigInteger L = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");
        static readonly BigInteger D = Mod(-121665 * Inv(121666));
        static readonly BigInteger SqrtM1 = BigInteger.ModPow(2, (P - 1) / 4, P);
        static readonly BigInteger[] B = BasePoint();

        /// <summary>True when signature is a valid Ed25519 signature of message by publicKey. Never throws.</summary>
        public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
        {
            try
            {
                if (publicKey == null || publicKey.Length != PublicKeySize || signature == null || signature.Length != SignatureSize || message == null) return false;
                BigInteger[] a = Decompress(publicKey);
                if (a == null) return false;
                byte[] r = Slice(signature, 0, 32);
                BigInteger[] rPoint = Decompress(r);
                if (rPoint == null) return false;
                BigInteger s = FromBytes(Slice(signature, 32, 32));
                if (s >= L) return false;
                BigInteger h = HashModL(r, publicKey, message);
                return Equal(Multiply(s, B), Add(rPoint, Multiply(h, a)));
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The 32-byte public key of a 32-byte secret key (seed).</summary>
        public static byte[] PublicKey(byte[] secretKey)
        {
            BigInteger a;
            byte[] prefix;
            Expand(secretKey, out a, out prefix);
            return Compress(Multiply(a, B));
        }

        /// <summary>Signs message with a 32-byte secret key (seed). Deterministic, as RFC 8032 specifies.</summary>
        public static byte[] Sign(byte[] secretKey, byte[] message)
        {
            if (message == null) throw new ArgumentNullException("message");
            BigInteger a;
            byte[] prefix;
            Expand(secretKey, out a, out prefix);
            byte[] publicKey = Compress(Multiply(a, B));
            BigInteger r = HashModL(prefix, message);
            byte[] rBytes = Compress(Multiply(r, B));
            BigInteger h = HashModL(rBytes, publicKey, message);
            BigInteger s = (r + h * a) % L;
            var signature = new byte[SignatureSize];
            Buffer.BlockCopy(rBytes, 0, signature, 0, 32);
            Buffer.BlockCopy(ToBytes32(s), 0, signature, 32, 32);
            return signature;
        }

        // ---- field and curve arithmetic (extended coordinates X, Y, Z, T with x = X/Z, y = Y/Z, xy = T/Z) ----

        static BigInteger Mod(BigInteger x)
        {
            BigInteger r = x % P;
            return r.Sign < 0 ? r + P : r;
        }

        static BigInteger Inv(BigInteger x) { return BigInteger.ModPow(Mod(x), P - 2, P); }

        static BigInteger[] BasePoint()
        {
            BigInteger y = Mod(4 * Inv(5));
            BigInteger x = RecoverX(y, 0);
            return new[] { x, y, BigInteger.One, Mod(x * y) };
        }

        static BigInteger[] Add(BigInteger[] p, BigInteger[] q)
        {
            BigInteger a = Mod((p[1] - p[0]) * (q[1] - q[0]));
            BigInteger b = Mod((p[1] + p[0]) * (q[1] + q[0]));
            BigInteger c = Mod(2 * p[3] * q[3] * D);
            BigInteger d = Mod(2 * p[2] * q[2]);
            BigInteger e = b - a, f = d - c, g = d + c, h = b + a;
            return new[] { Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h) };
        }

        static BigInteger[] Multiply(BigInteger scalar, BigInteger[] point)
        {
            BigInteger[] q = { BigInteger.Zero, BigInteger.One, BigInteger.One, BigInteger.Zero };
            BigInteger[] p = point;
            while (scalar.Sign > 0)
            {
                if (!scalar.IsEven) q = Add(q, p);
                p = Add(p, p);
                scalar >>= 1;
            }
            return q;
        }

        static bool Equal(BigInteger[] p, BigInteger[] q)
        {
            return Mod(p[0] * q[2] - q[0] * p[2]).IsZero && Mod(p[1] * q[2] - q[1] * p[2]).IsZero;
        }

        // The x for a y and the sign (low) bit of x, or -1 when no point has that y.
        static BigInteger RecoverX(BigInteger y, int sign)
        {
            if (y >= P) return BigInteger.MinusOne;
            BigInteger x2 = Mod((y * y - 1) * Inv(D * y * y + 1));
            if (x2.IsZero) return sign == 0 ? BigInteger.Zero : BigInteger.MinusOne;
            BigInteger x = BigInteger.ModPow(x2, (P + 3) / 8, P);
            if (!Mod(x * x - x2).IsZero) x = Mod(x * SqrtM1);
            if (!Mod(x * x - x2).IsZero) return BigInteger.MinusOne;
            if ((x.IsEven ? 0 : 1) != sign) x = P - x;
            return x;
        }

        static byte[] Compress(BigInteger[] point)
        {
            BigInteger zInverse = Inv(point[2]);
            BigInteger x = Mod(point[0] * zInverse), y = Mod(point[1] * zInverse);
            byte[] bytes = ToBytes32(y);
            if (!x.IsEven) bytes[31] |= 0x80;
            return bytes;
        }

        static BigInteger[] Decompress(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 32) return null;
            var copy = (byte[])bytes.Clone();
            int sign = copy[31] >> 7;
            copy[31] &= 0x7F;
            BigInteger y = FromBytes(copy);
            BigInteger x = RecoverX(y, sign);
            if (x.Sign < 0) return null;
            return new[] { x, y, BigInteger.One, Mod(x * y) };
        }

        static void Expand(byte[] secretKey, out BigInteger a, out byte[] prefix)
        {
            if (secretKey == null || secretKey.Length != SecretKeySize) throw new ArgumentException("An Ed25519 secret key is 32 bytes.", "secretKey");
            byte[] h;
            using (SHA512 sha = SHA512.Create()) h = sha.ComputeHash(secretKey);
            byte[] low = Slice(h, 0, 32);
            low[0] &= 248;
            low[31] &= 127;
            low[31] |= 64;
            a = FromBytes(low);
            prefix = Slice(h, 32, 32);
        }

        static BigInteger HashModL(params byte[][] parts)
        {
            using (SHA512 sha = SHA512.Create())
            {
                foreach (byte[] part in parts) sha.TransformBlock(part, 0, part.Length, null, 0);
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return FromBytes(sha.Hash) % L;
            }
        }

        // Little-endian unsigned bytes to a BigInteger (a zero byte on top keeps it positive).
        static BigInteger FromBytes(byte[] little)
        {
            var bytes = new byte[little.Length + 1];
            Buffer.BlockCopy(little, 0, bytes, 0, little.Length);
            return new BigInteger(bytes);
        }

        static byte[] ToBytes32(BigInteger value)
        {
            byte[] raw = value.ToByteArray();
            var bytes = new byte[32];
            Buffer.BlockCopy(raw, 0, bytes, 0, Math.Min(32, raw.Length));
            return bytes;
        }

        static byte[] Slice(byte[] source, int offset, int count)
        {
            var part = new byte[count];
            Buffer.BlockCopy(source, offset, part, 0, count);
            return part;
        }
    }
}
