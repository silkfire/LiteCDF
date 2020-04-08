namespace LiteCDF.Extensions
{
    using System;
    using System.Buffers;
    using System.Diagnostics;
    using System.IO;


    /// <summary>
    /// A collection of convenience methods for working wtih streams.
    /// </summary>
    public static class StreamExtensions
    {
        private const int MaxByteArrayLength = 0x7FFFFFC7;

        /// <summary>
        /// Reads the entire contents of a stream block-by-block, returning them as a byte array.
        /// </summary>
        /// <param name="stream">Stream to read from.</param>
        public static byte[] ToByteArray(this Stream stream)
        {
            {
                if (stream is MemoryStream ms)
                {
                    return ms.ToArray();
                }
            }

            if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);


            var buffer = new byte[16 * 1024];

            {
                using var ms = new MemoryStream();

                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }

                return ms.ToArray();
            }
        }


        /// <summary>
        /// Opens a binary file, reads the contents of the file into a byte array, and then closes the file.
        /// <para>Reading should be possible even if the file is currently in use.</para>
        /// </summary>
        /// <param name="path">The file to open for reading.</param>
        public static byte[] ReadAllBytes(string path)
        {
            // bufferSize == 1 used to avoid unnecessary buffer in FileStream
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1))
            {
                long fileLength = fs.Length;
                if (fileLength > int.MaxValue)
                {
                    throw new IOException("The file is too long. This operation is currently limited to supporting files less than 2 gigabytes in size.");
                }

                if (fileLength == 0)
                {
#if !MS_IO_REDIST
                    // Some file systems (e.g. procfs on Linux) return 0 for length even when there's content.
                    // Thus we need to assume 0 doesn't mean empty.
                    return ReadAllBytesUnknownLength(fs);
#endif
                }

                int index = 0;
                int count = (int)fileLength;
                byte[] bytes = new byte[count];
                while (count > 0)
                {
                    int n = fs.Read(bytes, index, count);
                    if (n == 0)
                        throw new EndOfStreamException("Unable to read beyond the end of the stream.");
                    index += n;
                    count -= n;
                }
                return bytes;
            }
        }


#if !MS_IO_REDIST
        private static byte[] ReadAllBytesUnknownLength(FileStream fs)
        {
            byte[] rentedArray = null;
            Span<byte> buffer = stackalloc byte[512];
            try
            {
                int bytesRead = 0;
                while (true)
                {
                    if (bytesRead == buffer.Length)
                    {
                        uint newLength = (uint)buffer.Length * 2;
                        if (newLength > MaxByteArrayLength)
                        {
                            newLength = (uint)Math.Max(MaxByteArrayLength, buffer.Length + 1);
                        }

                        byte[] tmp = ArrayPool<byte>.Shared.Rent((int)newLength);
                        buffer.CopyTo(tmp);
                        if (rentedArray != null)
                        {
                            ArrayPool<byte>.Shared.Return(rentedArray);
                        }
                        buffer = rentedArray = tmp;
                    }

                    Debug.Assert(bytesRead < buffer.Length);
                    int n = fs.Read(buffer.Slice(bytesRead));
                    if (n == 0)
                    {
                        return buffer.Slice(0, bytesRead).ToArray();
                    }
                    bytesRead += n;
                }
            }
            finally
            {
                if (rentedArray != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedArray);
                }
            }
        }
#endif
    }
}
