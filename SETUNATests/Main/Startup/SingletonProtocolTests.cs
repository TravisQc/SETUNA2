using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using com.clearunit;

namespace SETUNATests.Main.Startup
{
    [TestClass]
    public class SingletonProtocolTests
    {
        [TestMethod]
        public void RoundTripPreservesVersionAndArgumentOrder()
        {
            var args = new[] { "capture", "-x", "-120", "file name.png" };
            var payload = SingletonProtocol.Encode("3.1.0", args);
            var decoded = SingletonProtocol.Decode(payload);

            Assert.AreEqual(SingletonProtocol.ProtocolVersion, decoded.ProtocolVersion);
            Assert.AreEqual("3.1.0", decoded.ProductVersion);
            CollectionAssert.AreEqual(args, decoded.Args);
            Assert.IsFalse(object.ReferenceEquals(args, decoded.Args));
        }

        [TestMethod]
        public async Task LengthPrefixUsesLittleEndianAndReadsPartialStreams()
        {
            var expectedArgs = new[] { "one", "two" };
            using (var stream = new ChunkedStream())
            {
                await SingletonProtocol.WriteAsync(stream, "v", expectedArgs, CancellationToken.None);
                stream.Position = 0;

                var decoded = await SingletonProtocol.ReadAsync(stream, CancellationToken.None);
                CollectionAssert.AreEqual(expectedArgs, decoded.Args);

                var prefix = stream.ToArray();
                var payloadLength = BitConverter.ToInt32(prefix, 0);
                Assert.AreEqual(prefix.Length - sizeof(int), payloadLength);
            }
        }

        [TestMethod]
        public void RejectsMalformedAndWrongVersionMessages()
        {
            Assert.ThrowsException<InvalidDataException>(
                () => SingletonProtocol.Decode(Encoding.UTF8.GetBytes("{}")));

            var wrongVersion = Encoding.UTF8.GetBytes(
                "{\"protocolVersion\":99,\"productVersion\":\"v\",\"args\":[]}");
            Assert.ThrowsException<InvalidDataException>(() => SingletonProtocol.Decode(wrongVersion));
        }

        [TestMethod]
        public void RejectsOversizedPayloadBeforeWriting()
        {
            var huge = new string('x', SingletonProtocol.MaximumMessageBytes);
            Assert.ThrowsException<InvalidDataException>(
                () => SingletonProtocol.Encode("v", new[] { huge }));
        }

        sealed class ChunkedStream : MemoryStream
        {
            public override int Read(byte[] buffer, int offset, int count)
            {
                return base.Read(buffer, offset, Math.Min(1, count));
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return base.ReadAsync(buffer, offset, Math.Min(1, count), cancellationToken);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                while (count > 0)
                {
                    base.Write(buffer, offset, Math.Min(1, count));
                    offset++;
                    count--;
                }
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }
        }
    }
}
