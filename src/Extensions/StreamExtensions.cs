namespace LiteCDF.Extensions
{
    using System.IO;


    /// <summary>
    /// A collection of convenience methods for working wtih streams.
    /// </summary>
    public static class StreamExtensions
    {
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
    }
}
