using System.Buffers;

namespace OpenMcdf.Ole;

internal static class StreamExtensions
{
#if !NET
    private static int Read(this Stream target, Span<byte> buffer)
    {
        var sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            int numRead = target.Read(sharedBuffer, 0, buffer.Length);
            new ReadOnlySpan<byte>(sharedBuffer, 0, numRead).CopyTo(buffer);
            return numRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sharedBuffer);
        }
    }

    public static void ReadExactly(this Stream target, Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = target.Read(buffer.Slice(totalRead));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            totalRead += read;
        }
    }

    public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
    {
        stream.ReadExactly(buffer.AsSpan(offset, count));
    }

#endif
}
