namespace LiteCDF
{
    using Extensions;

    using System;
    using System.IO;


    /// <summary>
    /// A static class for reading compound documents.
    /// </summary>
    public static class Cdf
    {
        /// <summary>
        /// Opens a compound document for reading from a file source.
        /// </summary>
        /// <param name="filepath">Path to the document.</param>
        public static CompoundDocument Open(string filepath)
        {
            if (!File.Exists(filepath)) throw new CdfException(string.Format(Errors.FileDoesNotExist, filepath));

            return new CompoundDocument().Mount(filepath);
        }

        /// <summary>
        /// Opens a compound document for reading from a stream.
        /// </summary>
        /// <param name="stream">A stream to read data from.</param>
        public static CompoundDocument Open(Stream stream)
        {
            if (stream == null || stream.Length == 0) throw new CdfException(Errors.EmptyStream);

            var document = new CompoundDocument();

            document.Mount(stream.ToByteArray(), null);

            return document;
        }

        /// <summary>
        /// Opens a compound document for reading from a file source and extracts the first stream whose name matches the given predicate.
        /// </summary>
        /// <param name="filepath">Path to the document.</param>
        /// <param name="streamNameMatch">A predicate applied to the name of the stream that must be satisfied to determine which stream to read.</param>
        public static byte[] OpenAndReadStream(string filepath, Predicate<string> streamNameMatch)
        {
            if (!File.Exists(filepath)) throw new CdfException(string.Format(Errors.FileDoesNotExist, filepath));
            if (streamNameMatch == null) throw new CdfException(Errors.StreamNamePredicateNull);

            return new CompoundDocument().Mount(filepath, streamNameMatch);
        }

        /// <summary>
        /// Opens a compound document for reading from a stream and extracts the first stream whose name matches the given predicate.
        /// </summary>
        /// <param name="stream">A stream to read data from.</param>
        /// <param name="streamNameMatch">A predicate applied to the name of the stream that must be satisfied to determine which stream to read.</param>
        public static byte[] OpenAndReadStream(Stream stream, Predicate<string> streamNameMatch)
        {
            if (stream == null || stream.Length == 0) throw new CdfException(Errors.EmptyStream);
            if (streamNameMatch == null) throw new CdfException(Errors.StreamNamePredicateNull);

            return new CompoundDocument().Mount(stream.ToByteArray(), streamNameMatch);
        }
    }
}
