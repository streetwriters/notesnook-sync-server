using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace Notesnook.API.Models
{
    // must read and store exactly what SyncItem does (see PushedSyncItemTests)
    public sealed class PushedSyncItem : IDisposable
    {
        [JsonPropertyName("iv")]
        public string? IV { get; set; } = string.Empty;

        [JsonPropertyName("cipher")]
        public PooledUtf8String? Cipher { get; set; }

        [JsonPropertyName("id")]
        public string? ItemId { get; set; }

        [JsonPropertyName("length")]
        public long Length { get; set; }

        [JsonPropertyName("v")]
        public double Version { get; set; }

        [JsonPropertyName("keyVersion")]
        public int? KeyVersion { get; set; }

        [JsonPropertyName("alg")]
        public string? Algorithm { get; set; } = string.Empty;

        public void Dispose() => Cipher?.Dispose();

        // the driver writes strings strictly, throwing on invalid UTF-16 instead of replacing it
        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        private const byte Double = 0x01, String = 0x02, Null = 0x0A, Int32 = 0x10, Int64 = 0x12;

        // the buffer goes into rented, for the caller to return once MongoDB has the document
        public RawBsonDocument ToRawBson(string userId, long generation, long dateSynced, ICollection<byte[]> rented)
        {
            var buffer = LargeBuffers.Rent(BsonSize(userId, generation));
            rented.Add(buffer);
            var length = WriteBson(buffer, userId, generation, dateSynced);
            return new RawBsonDocument(new ByteArrayBuffer(buffer, length, isReadOnly: true));
        }

        public int BsonSize(string userId, long generation)
        {
            var size = 4 + 1;
            size += Element("DateSynced") + 8;
            size += Element("UserId") + StringValue(userId);
            if (generation != 0) size += Element("Generation") + 8;
            size += Element("IV") + (IV == null ? 0 : StringValue(IV));
            size += Element("Cipher") + (Cipher == null ? 0 : 4 + Cipher.Length + 1);
            size += Element("ItemId") + (ItemId == null ? 0 : StringValue(ItemId));
            size += Element("Length") + 8;
            size += Element("Version") + 8;
            size += Element("KeyVersion") + (KeyVersion == null ? 0 : 4);
            size += Element("Algorithm") + (Algorithm == null ? 0 : StringValue(Algorithm));
            return size;

            static int Element(string name) => 1 + name.Length + 1;
            static int StringValue(string value) => 4 + StrictUtf8.GetByteCount(value) + 1;
        }

        // SyncItem's class map order; no _id, so the replacement keeps the stored one
        public int WriteBson(Span<byte> destination, string userId, long generation, long dateSynced)
        {
            var writer = new BsonWriter(destination);
            writer.WriteInt64("DateSynced", dateSynced);
            writer.WriteString("UserId", userId);
            if (generation != 0) writer.WriteInt64("Generation", generation);
            writer.WriteString("IV", IV);
            if (Cipher == null) writer.WriteNull("Cipher");
            else writer.WriteUtf8("Cipher", Cipher.Span);
            writer.WriteString("ItemId", ItemId);
            writer.WriteInt64("Length", Length);
            writer.WriteDouble("Version", Version);
            if (KeyVersion is int keyVersion) writer.WriteInt32("KeyVersion", keyVersion);
            else writer.WriteNull("KeyVersion");
            writer.WriteString("Algorithm", Algorithm);
            return writer.End();
        }

        private ref struct BsonWriter
        {
            private readonly Span<byte> destination;
            // after the document's length, which End writes
            private int position;

            public BsonWriter(Span<byte> destination)
            {
                this.destination = destination;
                position = 4;
            }

            public int End()
            {
                destination[position++] = 0;
                BinaryPrimitives.WriteInt32LittleEndian(destination, position);
                return position;
            }

            public void WriteInt64(string name, long value)
            {
                WriteName(Int64, name);
                BinaryPrimitives.WriteInt64LittleEndian(destination[position..], value);
                position += 8;
            }

            public void WriteInt32(string name, int value)
            {
                WriteName(Int32, name);
                BinaryPrimitives.WriteInt32LittleEndian(destination[position..], value);
                position += 4;
            }

            public void WriteDouble(string name, double value)
            {
                WriteName(Double, name);
                BinaryPrimitives.WriteDoubleLittleEndian(destination[position..], value);
                position += 8;
            }

            public void WriteNull(string name) => WriteName(Null, name);

            public void WriteString(string name, string? value)
            {
                if (value == null)
                {
                    WriteNull(name);
                    return;
                }
                WriteName(String, name);
                FinishString(StrictUtf8.GetBytes(value, destination[(position + 4)..]));
            }

            public void WriteUtf8(string name, ReadOnlySpan<byte> value)
            {
                WriteName(String, name);
                value.CopyTo(destination[(position + 4)..]);
                FinishString(value.Length);
            }

            private void WriteName(byte type, string name)
            {
                destination[position++] = type;
                position += Encoding.ASCII.GetBytes(name, destination[position..]);
                destination[position++] = 0;
            }

            private void FinishString(int length)
            {
                BinaryPrimitives.WriteInt32LittleEndian(destination[position..], length + 1);
                position += 4 + length;
                destination[position++] = 0;
            }
        }
    }
}
