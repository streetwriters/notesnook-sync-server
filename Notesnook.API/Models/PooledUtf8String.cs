using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notesnook.API.Models
{
    // allocating a large object heap array per pushed item was the main trigger of gen2 collections, so those are pooled
    public static class LargeBuffers
    {
        // smaller arrays are cheap gen0 garbage, so pooling them would only hold memory
        private const int PoolThreshold = 64 * 1024;
        // a few per size up to 4MB, so the pool holds at most about 32MB; rarer, bigger ciphers are allocated directly
        private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Create(maxArrayLength: 4 * 1024 * 1024, maxArraysPerBucket: 4);

        public static byte[] Rent(int minimumLength) =>
            minimumLength < PoolThreshold ? new byte[minimumLength] : Pool.Rent(minimumLength);

        public static void Return(byte[] array)
        {
            if (array.Length >= PoolThreshold) Pool.Return(array);
        }
    }

    // the owner must dispose it once its bytes have been written, to return the buffer
    [JsonConverter(typeof(PooledUtf8StringJsonConverter))]
    public sealed class PooledUtf8String : IDisposable
    {
        private byte[]? buffer;

        public int Length { get; private set; }

        private PooledUtf8String(byte[] buffer, int length)
        {
            this.buffer = buffer;
            Length = length;
        }

        public static PooledUtf8String FromString(string value)
        {
            var bytes = LargeBuffers.Rent(Encoding.UTF8.GetByteCount(value));
            return new(bytes, Encoding.UTF8.GetBytes(value, bytes));
        }

        internal static PooledUtf8String Read(ref Utf8JsonReader reader)
        {
            // the unescaped value is never longer than the raw one
            var maxLength = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
            var bytes = LargeBuffers.Rent(maxLength);
            try
            {
                return new(bytes, reader.CopyString(bytes));
            }
            catch
            {
                LargeBuffers.Return(bytes);
                throw;
            }
        }

        public ReadOnlySpan<byte> Span => (buffer ?? throw new ObjectDisposedException(nameof(PooledUtf8String))).AsSpan(0, Length);

        public override string ToString() => Encoding.UTF8.GetString(Span);

        public void Dispose()
        {
            var bytes = buffer;
            buffer = null;
            if (bytes != null) LargeBuffers.Return(bytes);
        }
    }

    public sealed class PooledUtf8StringJsonConverter : JsonConverter<PooledUtf8String>
    {
        public override PooledUtf8String Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // nulls never reach a converter, so anything but a string is wrong, as for a string property
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected a string but found {reader.TokenType}.");
            return PooledUtf8String.Read(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, PooledUtf8String value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Span);
    }
}
