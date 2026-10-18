using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PinkieSysMon;

internal enum TurzxImageFormat : byte { Jpeg = 101, Png = 102 }
internal enum TurzxReplyKind { StatusMarker, Empty }
internal readonly record struct TurzxCommandCompletion(byte Command, int ResponseBytes, TurzxReplyKind Reply);

// Fresh implementation of public interoperability facts, not upstream code.
// Research provenance and physical evidence limits: docs/development/devices/turzx-12.3.md.
internal static class TurzxWireProtocol
{
    public const int HeaderLength = 512;
    public const int MaximumImageBytes = 1_048_576;

    public static byte[] Sync(uint milliseconds) => Header(10, milliseconds, 0);

    public static byte[] Brightness(int controllerValue, uint milliseconds)
    {
        if (controllerValue is < 0 or > 102)
            throw new ArgumentOutOfRangeException(nameof(controllerValue));
        return Header(14, milliseconds, (uint)controllerValue << 24);
    }

    public static byte[] Image(TurzxImageFormat format, ReadOnlySpan<byte> image, uint milliseconds)
    {
        if (format is not (TurzxImageFormat.Jpeg or TurzxImageFormat.Png))
            throw new ArgumentOutOfRangeException(nameof(format));
        if (image.Length is < 1 or > MaximumImageBytes)
            throw new ArgumentOutOfRangeException(nameof(image), "TURZX encoded image must be 1..1048576 bytes.");
        // A format tag must never silently relabel PNG as JPEG. Full decoding and
        // geometry validation belong to the encoder, not this framing layer.
        var matches = format == TurzxImageFormat.Png
            ? image.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            : image.Length >= 5 && image[0] == 255 && image[1] == 216 && image[2] == 255 &&
              image[^2] == 255 && image[^1] == 217;
        if (!matches) throw new InvalidDataException("TURZX encoded image signature does not match its command.");
        var packet = new byte[checked(HeaderLength + image.Length)];
        Header((byte)format, milliseconds, (uint)image.Length).CopyTo(packet, 0);
        image.CopyTo(packet.AsSpan(HeaderLength));
        return packet;
    }

    private static byte[] Header(byte command, uint milliseconds, uint value)
    {
        if (milliseconds >= 86_400_000) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        // 500-byte command body, padded with four zero bytes to a DES block boundary.
        var body = new byte[504];
        body[0] = command;
        body[2] = 0x1A;
        body[3] = 0x6D;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), milliseconds);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(8), value);
        using var des = DES.Create();
        // Fixed PUBLIC wire-format material, not an authentication credential.
        // Required for interoperability with the pinned public TURZX protocol.
        des.Key = "slv3tuzx"u8.ToArray();
        var packet = new byte[HeaderLength];
        des.EncryptCbc(body, des.Key, PaddingMode.None).CopyTo(packet, 0);
        packet[510] = 0xA1;
        packet[511] = 0x1A;
        return packet;
    }

    public static TurzxCommandCompletion Interpret(byte command, ReadOnlySpan<byte> response)
    {
        if (command is not (10 or 14 or 101 or 102)) throw new ArgumentOutOfRangeException(nameof(command));
        if (command == 14 && response.IsEmpty) return new(command, 0, TurzxReplyKind.Empty);
        // Conservative adapter policy: a short/empty image or sync response is
        // not the observed full response, even if an isolated marker is present.
        if (response.Length != HeaderLength || response[1] != 0xC8 && response[8] != 0xC8)
            throw new InvalidDataException($"TURZX command {command} returned an unrecognized response ({response.Length} bytes).");
        return new(command, response.Length, TurzxReplyKind.StatusMarker);
    }
}
