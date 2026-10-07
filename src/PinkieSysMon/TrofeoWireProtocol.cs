using System.Buffers.Binary;

namespace PinkieSysMon;

internal readonly record struct TrofeoFrameLayout(
    int ChunkCount,
    int PaddedChunkCount,
    int FrameLength,
    int LastDataLength);

internal static class TrofeoWireProtocol
{
    internal const int ChunkSize = 512;
    internal const int HeaderSize = 16;
    internal const int DataSize = 496;
    internal const int TransferBlockSize = 4096;

    internal static TrofeoFrameLayout GetFrameLayout(int payloadLength)
    {
        if (payloadLength < 0)
            throw new ArgumentOutOfRangeException(nameof(payloadLength));

        var chunkCount = payloadLength / DataSize + 1;
        var lastDataLength = payloadLength % DataSize;

        var paddedChunkCount = chunkCount;
        var remainder = paddedChunkCount % 4;
        if (remainder != 0)
            paddedChunkCount += 4 - remainder;

        return new TrofeoFrameLayout(
            chunkCount,
            paddedChunkCount,
            checked(paddedChunkCount * ChunkSize),
            lastDataLength);
    }

    internal static int WriteFrame(ReadOnlySpan<byte> payload, Span<byte> output)
    {
        var layout = GetFrameLayout(payload.Length);
        if (output.Length < layout.FrameLength)
        {
            throw new ArgumentException(
                $"Trofeo frame buffer is too small: {output.Length}/{layout.FrameLength} bytes.",
                nameof(output));
        }

        var frame = output.Slice(0, layout.FrameLength);
        for (var i = 0; i < layout.ChunkCount; i++)
        {
            var offset = i * ChunkSize;
            var isLast = i == layout.ChunkCount - 1;
            var dataLength = isLast ? layout.LastDataLength : DataSize;
            var chunk = frame.Slice(offset, ChunkSize);

            // Header has three reserved bytes that must not retain data from the previous frame.
            chunk.Slice(0, HeaderSize).Clear();
            chunk[0] = 0x01;
            chunk[1] = 0xFF;
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.Slice(2, 4), checked((uint)payload.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.Slice(6, 2), checked((ushort)dataLength));
            chunk[8] = 0x01;
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.Slice(9, 2), checked((ushort)layout.ChunkCount));
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.Slice(11, 2), checked((ushort)i));

            if (dataLength > 0)
                payload.Slice(i * DataSize, dataLength).CopyTo(chunk.Slice(HeaderSize, dataLength));

            if (dataLength < DataSize)
                chunk.Slice(HeaderSize + dataLength, DataSize - dataLength).Clear();
        }

        if (layout.PaddedChunkCount > layout.ChunkCount)
        {
            frame.Slice(
                layout.ChunkCount * ChunkSize,
                (layout.PaddedChunkCount - layout.ChunkCount) * ChunkSize).Clear();
        }

        return layout.FrameLength;
    }

    internal static int GetTransferLength(int remainingBytes)
    {
        if (remainingBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(remainingBytes));

        return Math.Min(TransferBlockSize, remainingBytes);
    }
}
