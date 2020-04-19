namespace LiteCDF
{
    using Extensions;

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;


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
            return new CompoundDocument().Mount(filepath);
        }

        /// <summary>
        /// Opens a compound document for reading from a byte array.
        /// </summary>
        /// <param name="data">A byte array to read data from.</param>
        public static CompoundDocument Open(byte[] data)
        {
            if (data == null || data.Length == 0) throw new CdfException(Errors.EmptyDataStream);

            var document = new CompoundDocument();

            document.Mount(data, null, null);

            return document;
        }

        /// <summary>
        /// Opens a compound document for reading from a stream.
        /// </summary>
        /// <param name="stream">A stream to read data from.</param>
        public static CompoundDocument Open(Stream stream)
        {
            if (stream == null || stream.Length == 0) throw new CdfException(Errors.EmptyDataStream);

            var document = new CompoundDocument();

            document.Mount(stream.ToByteArray(), null, null);

            return document;
        }

        /// <summary>
        /// Opens a compound document for reading from a file source and extracts the first stream whose name matches the given predicate.
        /// </summary>
        /// <param name="filepath">Path to the document.</param>
        /// <param name="streamNameMatch">A predicate applied to the name of the stream that must be satisfied to determine which stream to read.</param>
        public static byte[] OpenAndReadStream(string filepath, Predicate<string> streamNameMatch)
        {
            if (streamNameMatch == null) throw new CdfException(Errors.StreamNamePredicateNull);

            return new CompoundDocument().Mount(filepath, streamNameMatch, true).FirstOrDefault().Value;
        }

        /// <summary>
        /// Opens a compound document for reading from a byte array and extracts the first stream whose name matches the given predicate.
        /// </summary>
        /// <param name="data">A byte array to read data from.</param>
        /// <param name="streamNameMatch">A predicate applied to the name of the stream that must be satisfied to determine which stream to read.</param>
        public static byte[] OpenAndReadStream(byte[] data, Predicate<string> streamNameMatch)
        {
            if (data == null || data.Length == 0) throw new CdfException(Errors.EmptyDataStream);
            if (streamNameMatch == null) throw new CdfException(Errors.StreamNamePredicateNull);

            return new CompoundDocument().Mount(data, streamNameMatch, true).FirstOrDefault().Value;
        }

        /// <summary>
        /// Opens a compound document for reading from a stream and extracts the first stream whose name matches the given predicate.
        /// </summary>
        /// <param name="stream">A stream to read data from.</param>
        /// <param name="streamNameMatch">A predicate applied to the name of the stream that must be satisfied to determine which stream to read.</param>
        public static byte[] OpenAndReadStream(Stream stream, Predicate<string> streamNameMatch)
        {
            if (stream == null || stream.Length == 0) throw new CdfException(Errors.EmptyDataStream);
            if (streamNameMatch == null) throw new CdfException(Errors.StreamNamePredicateNull);

            return new CompoundDocument().Mount(stream.ToByteArray(), streamNameMatch, true).FirstOrDefault().Value;
        }

        /// <summary>
        /// Opens a compound document for reading from a file source and extracts all streams whose name matches the given predicate.
        /// <para>Returns a dictionary of key-value pairs representing the name and the stream itself, respectively.</para>
        /// </summary>
        /// <param name="filepath">Path to the document.</param>
        /// <param name="streamNameMatch">A predicate applied to the name of the stream that must be satisfied to determine which streams to read.</param>
        public static Dictionary<string, byte[]> OpenAndReadMultipleStreams(string filepath, Predicate<string> streamNameMatch)
        {
            if (streamNameMatch == null) throw new CdfException(Errors.StreamNamePredicateNull);

            return new CompoundDocument().Mount(filepath, streamNameMatch, false);
        }

        /// <summary>
        /// Opens a compound document for reading from a file source and extracts all streams whose name matches the given predicate.
        /// <para>Returns a dictionary of key-value pairs representing the name and the stream itself, respectively.</para>
        /// </summary>
        /// <param name="data">A byte array to read data from.</param>
        /// <param name="streamNameMatch">A predicate applied to the name of the stream that must be satisfied to determine which streams to read.</param>
        public static Dictionary<string, byte[]> OpenAndReadMultipleStreams(byte[] data, Predicate<string> streamNameMatch)
        {
            if (data == null || data.Length == 0) throw new CdfException(Errors.EmptyDataStream);
            if (streamNameMatch == null) throw new CdfException(Errors.StreamNamePredicateNull);

            return new CompoundDocument().Mount(data, streamNameMatch, false);
        }

        /// <summary>
        /// Opens a compound document for reading from a file source and extracts all streams whose name matches the given predicate.
        /// <para>Returns a dictionary of key-value pairs representing the name and the stream itself, respectively.</para>
        /// </summary>
        /// <param name="stream">A stream to read data from.</param>
        /// <param name="streamNameMatch">A predicate applied to the name of the stream that must be satisfied to determine which streams to read.</param>
        public static Dictionary<string, byte[]> OpenAndReadMultipleStreams(Stream stream, Predicate<string> streamNameMatch)
        {
            if (stream == null || stream.Length == 0) throw new CdfException(Errors.EmptyDataStream);
            if (streamNameMatch == null) throw new CdfException(Errors.StreamNamePredicateNull);

            return new CompoundDocument().Mount(stream.ToByteArray(), streamNameMatch, false);
        }
    }
}
