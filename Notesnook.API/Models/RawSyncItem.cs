using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Serializers;

namespace Notesnook.API.Models
{
    /// <summary>
    /// A <see cref="SyncItem"/> kept as the BSON bytes stored in MongoDB and written to clients straight from them,
    /// so its strings (mostly the cipher) are never decoded to UTF-16. It is read-only: pushes still use <see cref="SyncItem"/>.
    /// The JSON output must be byte-for-byte what <see cref="SyncItem"/> produces (see RawSyncItemTests).
    /// </summary>
    [BsonSerializer(typeof(RawSyncItemBsonSerializer))]
    [JsonConverter(typeof(RawSyncItemJsonConverter))]
    public sealed class RawSyncItem
    {
        internal enum Field { DateSynced, UserId, IV, Cipher, ItemId, Length, Version, KeyVersion, Algorithm }

        private static readonly byte[][] ElementNames = [.. Enum.GetNames<Field>().Select(Encoding.UTF8.GetBytes)];

        private readonly byte[] bson;
        // (BSON type, offset of the value, length of the value) per field; type 0 = missing
        private readonly (byte Type, int Offset, int Length)[] slots = new (byte, int, int)[ElementNames.Length];

        public string? ItemId { get; }

        public RawSyncItem(byte[] bson)
        {
            this.bson = bson;
            var position = 4;
            while (true)
            {
                var type = bson[position++];
                if (type == 0) break;
                var nameLength = bson.AsSpan(position).IndexOf((byte)0);
                var name = bson.AsSpan(position, nameLength);
                position += nameLength + 1;
                var length = ValueLength(type, position);
                for (var field = 0; field < ElementNames.Length; field++)
                    if (name.SequenceEqual(ElementNames[field])) slots[field] = (type, position, length);
                position += length;
            }

            // validate every field up front, so a malformed document fails while it is read, like SyncItem does
            GetInt64(Field.DateSynced);
            GetInt64(Field.Length);
            GetDouble(Field.Version);
            GetInt32OrNull(Field.KeyVersion);
            foreach (var field in new[] { Field.UserId, Field.IV, Field.Cipher, Field.ItemId, Field.Algorithm }) GetString(field, out _);
            ItemId = GetString(Field.ItemId, out var itemId) ? Encoding.UTF8.GetString(itemId) : null;
        }

        private int ValueLength(byte type, int position) => type switch
        {
            0x01 or 0x09 or 0x11 or 0x12 => 8,
            0x02 or 0x0D or 0x0E => 4 + ReadInt32(position),
            0x03 or 0x04 or 0x0F => ReadInt32(position),
            0x05 => 5 + ReadInt32(position),
            0x06 or 0x0A or 0x7F or 0xFF => 0,
            0x07 => 12,
            0x08 => 1,
            0x0B => CStringLength(position) + CStringLength(position + CStringLength(position)),
            0x0C => 4 + ReadInt32(position) + 12,
            0x10 => 4,
            0x13 => 16,
            _ => throw new FormatException($"Unknown BSON type 0x{type:X2}.")
        };

        private int ReadInt32(int position) => BinaryPrimitives.ReadInt32LittleEndian(bson.AsSpan(position));
        private int CStringLength(int position) => bson.AsSpan(position).IndexOf((byte)0) + 1;

        /// <summary>
        /// Returns false for null. A missing field reads as SyncItem's default: null for UserId and ItemId, "" otherwise.
        /// </summary>
        internal bool GetString(Field field, out ReadOnlySpan<byte> utf8)
        {
            var (type, offset, length) = slots[(int)field];
            switch (type)
            {
                // string and symbol, as StringSerializer reads them
                case 0x02 or 0x0E:
                    utf8 = bson.AsSpan(offset + 4, length - 5);
                    return true;
                case 0x0A:
                    utf8 = default;
                    return false;
                case 0:
                    utf8 = default;
                    return field is not (Field.UserId or Field.ItemId);
                default:
                    throw Unreadable(field, type);
            }
        }

        // numbers are converted exactly as Int64Serializer, DoubleSerializer and Int32Serializer do
        private static readonly RepresentationConverter Converter = new(allowOverflow: false, allowTruncation: false);

        internal long GetInt64(Field field)
        {
            var (type, offset, _) = slots[(int)field];
            return type switch
            {
                0 => 0,
                0x01 => Converter.ToInt64(ReadDouble(offset)),
                0x02 => JsonConvert.ToInt64(ReadString(offset)),
                0x10 => Converter.ToInt64(ReadInt32(offset)),
                0x12 => ReadInt64(offset),
                0x13 => Converter.ToInt64(ReadDecimal128(offset)),
                _ => throw Unreadable(field, type)
            };
        }

        internal double GetDouble(Field field)
        {
            var (type, offset, _) = slots[(int)field];
            return type switch
            {
                0 => 0,
                0x01 => ReadDouble(offset),
                0x02 => JsonConvert.ToDouble(ReadString(offset)),
                0x10 => Converter.ToDouble(ReadInt32(offset)),
                0x12 => Converter.ToDouble(ReadInt64(offset)),
                0x13 => Converter.ToDouble(ReadDecimal128(offset)),
                _ => throw Unreadable(field, type)
            };
        }

        internal int? GetInt32OrNull(Field field)
        {
            var (type, offset, _) = slots[(int)field];
            return type switch
            {
                0 or 0x0A => null,
                0x01 => Converter.ToInt32(ReadDouble(offset)),
                0x02 => JsonConvert.ToInt32(ReadString(offset)),
                0x10 => ReadInt32(offset),
                0x12 => Converter.ToInt32(ReadInt64(offset)),
                0x13 => Converter.ToInt32(ReadDecimal128(offset)),
                _ => throw Unreadable(field, type)
            };
        }

        private long ReadInt64(int position) => BinaryPrimitives.ReadInt64LittleEndian(bson.AsSpan(position));
        private double ReadDouble(int position) => BinaryPrimitives.ReadDoubleLittleEndian(bson.AsSpan(position));
        private string ReadString(int position) => Encoding.UTF8.GetString(bson.AsSpan(position + 4, ReadInt32(position) - 1));
        private Decimal128 ReadDecimal128(int position) => Decimal128.FromIEEEBits((ulong)ReadInt64(position + 8), (ulong)ReadInt64(position));
        private static FormatException Unreadable(Field field, byte type) => new($"Cannot read {field} from BSON type 0x{type:X2}.");
    }

    public sealed class RawSyncItemBsonSerializer : SerializerBase<RawSyncItem>
    {
        public override RawSyncItem Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            // copied out of the driver's pooled buffer, which must not stay pinned while a client applies the chunk
            using var slice = context.Reader.ReadRawBsonDocument();
            var bytes = new byte[slice.Length];
            slice.GetBytes(0, bytes, 0, slice.Length);
            return new RawSyncItem(bytes);
        }
    }

    public sealed class RawSyncItemJsonConverter : JsonConverter<RawSyncItem>
    {
        public override RawSyncItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException($"{nameof(RawSyncItem)} is only sent to clients.");

        public override void Write(Utf8JsonWriter writer, RawSyncItem value, JsonSerializerOptions options)
        {
            // the order and names of SyncItem's properties
            writer.WriteStartObject();
            writer.WriteNumber("dateSynced"u8, value.GetInt64(RawSyncItem.Field.DateSynced));
            WriteString(writer, "userId"u8, value, RawSyncItem.Field.UserId);
            WriteString(writer, "iv"u8, value, RawSyncItem.Field.IV);
            WriteString(writer, "cipher"u8, value, RawSyncItem.Field.Cipher);
            WriteString(writer, "id"u8, value, RawSyncItem.Field.ItemId);
            writer.WriteNumber("length"u8, value.GetInt64(RawSyncItem.Field.Length));
            writer.WriteNumber("v"u8, value.GetDouble(RawSyncItem.Field.Version));
            if (value.GetInt32OrNull(RawSyncItem.Field.KeyVersion) is int keyVersion) writer.WriteNumber("keyVersion"u8, keyVersion);
            else writer.WriteNull("keyVersion"u8);
            WriteString(writer, "alg"u8, value, RawSyncItem.Field.Algorithm);
            writer.WriteEndObject();
        }

        private static void WriteString(Utf8JsonWriter writer, ReadOnlySpan<byte> name, RawSyncItem value, RawSyncItem.Field field)
        {
            if (value.GetString(field, out var utf8)) writer.WriteString(name, utf8);
            else writer.WriteNull(name);
        }
    }
}
