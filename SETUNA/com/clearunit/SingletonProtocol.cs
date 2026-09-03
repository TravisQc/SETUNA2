using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace com.clearunit
{
    /// <summary>
    /// Wire contract for current-user startup forwarding. The framing is deliberately
    /// independent of object serialization so a malformed client cannot create a UI object.
    /// </summary>
    internal static class SingletonProtocol
    {
        public const int ProtocolVersion = 1;
        public const int MaximumMessageBytes = 64 * 1024;

        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = false
        };

        public static byte[] Encode(string productVersion, string[] args)
        {
            var message = new StartupMessage
            {
                ProtocolVersion = ProtocolVersion,
                ProductVersion = productVersion ?? string.Empty,
                Args = args == null ? Array.Empty<string>() : (string[])args.Clone()
            };

            var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
            ValidatePayloadLength(payload.Length);
            return payload;
        }

        public static StartupMessage Decode(ReadOnlySpan<byte> payload)
        {
            ValidatePayloadLength(payload.Length);

            StartupMessage message;
            try
            {
                message = JsonSerializer.Deserialize<StartupMessage>(payload, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Singleton message JSON is invalid.", ex);
            }

            if (message == null
                || message.ProtocolVersion != ProtocolVersion
                || message.ProductVersion == null
                || message.Args == null)
            {
                throw new InvalidDataException("Singleton message protocol version or fields are invalid.");
            }

            for (var i = 0; i < message.Args.Length; i++)
            {
                if (message.Args[i] == null)
                {
                    throw new InvalidDataException("Singleton message arguments cannot contain null values.");
                }
            }

            return message;
        }

        public static async Task WriteAsync(Stream stream, string productVersion, string[] args, CancellationToken cancellationToken)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var payload = Encode(productVersion, args);
            var length = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
            await stream.WriteAsync(length, 0, length.Length, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task<StartupMessage> ReadAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var lengthBytes = new byte[sizeof(int)];
            await ReadExactlyAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            ValidatePayloadLength(length);

            var payload = new byte[length];
            await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
            return Decode(payload);
        }

        static void ValidatePayloadLength(int length)
        {
            if (length <= 0 || length > MaximumMessageBytes)
            {
                throw new InvalidDataException("Singleton message length is outside the 64 KiB limit.");
            }
        }

        static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Singleton pipe closed before the message was complete.");
                }

                offset += read;
            }
        }

        internal sealed class StartupMessage
        {
            [JsonPropertyName("protocolVersion")]
            public int ProtocolVersion { get; set; }

            [JsonPropertyName("productVersion")]
            public string ProductVersion { get; set; }

            [JsonPropertyName("args")]
            public string[] Args { get; set; }
        }
    }
}
