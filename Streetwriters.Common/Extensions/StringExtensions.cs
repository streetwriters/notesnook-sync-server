using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace System
{
    public static class StringExtensions
    {
        public static string ToSha256(this string rawData, int maxLength = 12)
        {
            // Create a SHA256   
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                return ToHex(bytes, 0, maxLength);
            }
        }

        public static byte[] CompressBrotli(this string input)
        {
            var raw = Encoding.Default.GetBytes(input);
            using (MemoryStream memory = new MemoryStream())
            {
                using (BrotliStream brotli = new BrotliStream(memory, CompressionLevel.Optimal))
                {
                    brotli.Write(raw, 0, raw.Length);
                }
                return memory.ToArray();
            }
        }

        public static string DecompressBrotli(this byte[] compressed)
        {
            using (BrotliStream stream = new BrotliStream(new MemoryStream(compressed), CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            memory.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return Encoding.Default.GetString(memory.ToArray());
                }
            }
        }

        private static string ToHex(byte[] bytes, int startIndex, int length)
        {
            char[] c = new char[length * 2];
            byte b;
            for (int bx = startIndex, cx = startIndex; bx < length; ++bx, ++cx)
            {
                b = ((byte)(bytes[bx] >> 4));
                c[cx] = (char)(b > 9 ? b + 0x37 + 0x20 : b + 0x30);

                b = ((byte)(bytes[bx] & 0x0F));
                c[++cx] = (char)(b > 9 ? b + 0x37 + 0x20 : b + 0x30);
            }
            return new string(c);
        }
    }
}