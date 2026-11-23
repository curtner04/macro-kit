using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MacroKit.Core
{
    /// <summary>A valid Macro Kit Pro license, as read from its key.</summary>
    public sealed class LicenseInfo
    {
        public int Serial;
        public DateTime Issued;    // date only
        public string Name = "";   // who it was issued to; may be empty
        public string Key = "";    // the key text, tidied

        public string Describe()
        {
            string to = Name.Length > 0 ? "Licensed to " + Name : "Licensed";
            return to + " · key " + Serial.ToString(CultureInfo.InvariantCulture) + " · issued " + Issued.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Free or Pro for this run of Macro Kit. Everything outside the Pro features works the same in both.
    /// Set once the saved license is read, and again when one is entered or removed.
    /// </summary>
    public static class Edition
    {
        public const string ProPageUrl = "https://curtner04.github.io/macro-kit/#pro";

        /// <summary>
        /// False until Pro can be bought. Meanwhile no "Get Pro" button leads to the website's disabled checkout;
        /// Pro features say Pro goes on sale soon instead. Set it to true when checkout opens.
        /// </summary>
        public static readonly bool CheckoutOpen = true;   // 1.13.4: Pro is on sale through Stripe

        static volatile LicenseInfo license;

        public static LicenseInfo License { get { return license; } }
        public static bool IsPro { get { return license != null; } }
        public static string Name { get { return IsPro ? "Pro" : "Free"; } }

        /// <summary>Raised on the thread that called Set.</summary>
        public static event Action Changed;

        public static void Set(LicenseInfo info)
        {
            license = info;
            Action handler = Changed;
            if (handler != null) handler();
        }

        /// <summary>The sentence a free user sees on a Pro feature.</summary>
        public static string ProOnly(string feature)
        {
            return CheckoutOpen
                ? feature + " come with Macro Kit Pro. Enter your license key in Settings, or get Pro from the Macro Kit website."
                : feature + " come with Macro Kit Pro, which goes on sale soon. If you have a license key, enter it in Settings.";
        }
    }

    /// <summary>
    /// Offline license keys. A key is "MK1-" and Crockford base32 of a short payload plus its Ed25519 signature, so it is
    /// checked with the public key below and nothing is ever sent anywhere. The private signing key never leaves the
    /// publisher's own tools (it is not in this repository).
    /// Payload: "MK", format 1, edition 1 (Pro), serial (4 bytes), issue day (2 bytes, days since 2026-01-01),
    /// name length (1 byte) and the name in UTF-8. The signature covers <see cref="Domain"/> followed by the payload.
    /// </summary>
    public static class License
    {
        public const string Prefix = "MK1-";
        public const int MaxNameBytes = 60;
        const string Domain = "Macro Kit license\n";
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";   // Crockford: no I, L, O or U
        static readonly DateTime Epoch = new DateTime(2026, 1, 1);

        /// <summary>Public keys whose signatures are accepted (a new one can be added here before an old one is retired).</summary>
        static readonly byte[][] PublicKeys = { FromHex("9cb2523ce02ddb9bf7af5d8ec5f5a0340a5c0c8859c9b6437b5621bd9154e082") };

        /// <summary>Serials of keys that were refunded or shared publicly. A new version of Macro Kit stops accepting them.</summary>
        static readonly HashSet<int> Withdrawn = new HashSet<int>();

        /// <summary>Reads a key. Spaces, line breaks, dashes and lower case are all fine (keys get pasted from emails).</summary>
        public static bool TryRead(string text, out LicenseInfo info, out string problem)
        {
            return TryRead(text, PublicKeys, out info, out problem);
        }

        internal static bool TryRead(string text, IList<byte[]> publicKeys, out LicenseInfo info, out string problem)
        {
            info = null;
            problem = null;
            string compact = new string((text ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
            if (compact.Length == 0) { problem = "Paste your license key first."; return false; }
            compact = compact.Replace("-", "");
            string bare = Prefix.Replace("-", "");
            if (!compact.StartsWith(bare, StringComparison.Ordinal))
            {
                problem = "That isn't a Macro Kit license key. Keys start with " + Prefix;
                return false;
            }
            byte[] raw = Decode(compact.Substring(bare.Length));
            if (raw == null || raw.Length < 11 + Ed25519.SignatureSize)
            {
                problem = "This key is incomplete or has a typo. Copy the whole key from your email and paste it again.";
                return false;
            }
            int payloadLength = raw.Length - Ed25519.SignatureSize;
            byte[] payload = raw.Take(payloadLength).ToArray();
            byte[] signature = raw.Skip(payloadLength).ToArray();
            if (payload[0] != (byte)'M' || payload[1] != (byte)'K' || payload[2] != 1 || payload[3] != 1 || payload[10] != payloadLength - 11)
            {
                problem = "This key is incomplete or has a typo. Copy the whole key from your email and paste it again.";
                return false;
            }
            byte[] message = SignedMessage(payload);
            if (!publicKeys.Any(k => Ed25519.Verify(k, message, signature)))
            {
                problem = "This key doesn't check out. Copy the whole key from your email and paste it again.";
                return false;
            }
            int serial = BitConverter.ToInt32(payload, 4);
            if (Withdrawn.Contains(serial))
            {
                problem = "This key was withdrawn (refunded or shared publicly), so it no longer unlocks Pro.";
                return false;
            }
            info = new LicenseInfo
            {
                Serial = serial,
                Issued = Epoch.AddDays(BitConverter.ToUInt16(payload, 8)),
                Name = Encoding.UTF8.GetString(payload, 11, payload[10]),
                Key = Format(raw),
            };
            return true;
        }

        /// <summary>The payload for a new key (used by the publisher's key generator). The name is cut to fit.</summary>
        public static byte[] Payload(int serial, DateTime issued, string name)
        {
            if (serial < 1) throw new ArgumentOutOfRangeException("serial", "Serials start at 1.");
            int day = (int)(issued.Date - Epoch).TotalDays;
            if (day < 0 || day > ushort.MaxValue) throw new ArgumentOutOfRangeException("issued");
            byte[] nameBytes = FitName(name);
            var payload = new byte[11 + nameBytes.Length];
            payload[0] = (byte)'M';
            payload[1] = (byte)'K';
            payload[2] = 1;
            payload[3] = 1;
            BitConverter.GetBytes(serial).CopyTo(payload, 4);
            BitConverter.GetBytes((ushort)day).CopyTo(payload, 8);
            payload[10] = (byte)nameBytes.Length;
            nameBytes.CopyTo(payload, 11);
            return payload;
        }

        /// <summary>What the signature covers.</summary>
        public static byte[] SignedMessage(byte[] payload)
        {
            byte[] domain = Encoding.UTF8.GetBytes(Domain);
            var message = new byte[domain.Length + payload.Length];
            domain.CopyTo(message, 0);
            payload.CopyTo(message, domain.Length);
            return message;
        }

        /// <summary>The key text for payload + signature, in dash-separated groups of eight.</summary>
        public static string Format(byte[] payload, byte[] signature)
        {
            return Format(payload.Concat(signature).ToArray());
        }

        static string Format(byte[] raw)
        {
            string body = Encode(raw);
            var groups = new List<string>();
            for (int i = 0; i < body.Length; i += 8) groups.Add(body.Substring(i, Math.Min(8, body.Length - i)));
            return Prefix + string.Join("-", groups);
        }

        static byte[] FitName(string name)
        {
            string clean = new string((name ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
            byte[] bytes = Encoding.UTF8.GetBytes(clean);
            while (bytes.Length > MaxNameBytes && clean.Length > 0)
            {
                clean = clean.Substring(0, clean.Length - 1);
                if (clean.Length > 0 && char.IsHighSurrogate(clean[clean.Length - 1])) clean = clean.Substring(0, clean.Length - 1);
                bytes = Encoding.UTF8.GetBytes(clean);
            }
            return bytes;
        }

        static string Encode(byte[] data)
        {
            var text = new StringBuilder();
            int buffer = 0, bits = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    text.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
                buffer &= (1 << bits) - 1;
            }
            if (bits > 0) text.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            return text.ToString();
        }

        // Null for a character outside the alphabet. O reads as 0 and I or L as 1, as Crockford intends.
        static byte[] Decode(string text)
        {
            var bytes = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (char raw in text)
            {
                char ch = raw == 'O' ? '0' : raw == 'I' || raw == 'L' ? '1' : raw;
                int value = Alphabet.IndexOf(ch);
                if (value < 0) return null;
                buffer = (buffer << 5) | value;
                bits += 5;
                if (bits >= 8)
                {
                    bytes.Add((byte)(buffer >> (bits - 8)));
                    bits -= 8;
                }
                buffer &= (1 << bits) - 1;
            }
            // A key has exactly one spelling: the bits left over at the end must be zero padding.
            if (bits >= 5 || buffer != 0) return null;
            return bytes.ToArray();
        }

        internal static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }
    }
}
