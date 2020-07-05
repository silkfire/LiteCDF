namespace LiteCDF
{
    using Salar.BinaryBuffers;
    using StreamExtensions;

    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;


    /// <summary>
    /// Represents a compound document.
    /// </summary>
    public class CompoundDocument
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private const ushort           HEADER_SIZE                 = 0x200;             //  0x200 = 512

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private static readonly byte[] HEADER_SIGNATURE            = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };     //  ��ࡱ�

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private const byte             SECID_SIZE                  = sizeof(int);       //  0x04 =   4

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private const byte             HEADER_MSAT_SAT_SECID_COUNT = 0x6D;              //  0x6D = 109


        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private const byte             DIRECTORY_ENTRY_SIZE        = 0x80;              //  0x80 = 128

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private const int              SECID_FREE                  = -1;                //  0xFFFFFFFF = -1 (two's complement)
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private const int              SECID_END_OF_CHAIN          = -2;                //  0xFFFFFFFE = -2 (two's complement)
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private const int              SECID_SAT                   = -3;                //  0xFFFFFFFD = -3 (two's complement)
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private const int              SECID_MSAT                  = -4;                //  0xFFFFFFFC = -4 (two's complement)


        private string _filepath;

        /// <summary>
        /// The directory entries contained in this compound document.
        /// </summary>
        public List<DirectoryEntry> DirectoryEntries { get; private set; }


        internal CompoundDocument() { }



        internal CompoundDocument Mount(string filepath, bool rootStorageDescendantsOnly)
        {
            _filepath = filepath;

            try
            {
                Mount(StreamExtensions.ReadAllBytes(filepath), null, null, rootStorageDescendantsOnly);
            }
            catch (FileNotFoundException e)
            {
                throw new CdfException(e.Message);
            }

            return this;
        }

        internal Dictionary<string, byte[]> Mount(string filepath, Predicate<string> streamNameMatch, bool? returnOnFirstMatch, bool rootStorageDescendantsOnly)
        {
            _filepath = filepath;

            try
            {
                return Mount(StreamExtensions.ReadAllBytes(filepath), streamNameMatch, returnOnFirstMatch, rootStorageDescendantsOnly);
            }
            catch (FileNotFoundException e)
            {
                throw new CdfException(e.Message);
            }
        }

        internal Dictionary<string, byte[]> Mount(byte[] data, Predicate<string> streamNameMatch, bool? returnOnFirstMatch, bool rootStorageDescendantsOnly)
        {
            var mainReader = new BinaryBufferReader(data);

            try
            {
                GetHeaderValues(ref mainReader,
                                out var sectorSize,
                                out var shortSectorSize,
                                out var satSectorCount,
                                out var firstSecIdDirectoryStream,
                                out var standardStreamSizeThreshold,
                                out var firstSecIdSsat,
                                out var ssatSectorCount,
                                out var firstSecIdExtendedMsat,
                                out var msatExtraSectorCount);


                // Master Sector Allocation Table (MSAT) / Sector Allocation Table (SAT)

                var secIdsPerSector = sectorSize / SECID_SIZE;

                var satSecIdChain = BuildSatSecIdChain(mainReader, msatExtraSectorCount, firstSecIdExtendedMsat, satSectorCount, sectorSize, secIdsPerSector);


                // Short-Sector Allocation Table (SSAT)

                var ssatSecIdChain = BuildSsatSecIdChain(mainReader, ssatSectorCount, satSecIdChain, firstSecIdSsat, sectorSize, secIdsPerSector);


                // Directory

                var directoryStream = ReadDirectoryStream(mainReader, firstSecIdDirectoryStream, satSecIdChain, sectorSize);

                DirectoryEntries = new List<DirectoryEntry>(directoryStream.Length / DIRECTORY_ENTRY_SIZE);

                if (rootStorageDescendantsOnly)
                {
                    ReadDirectoryEntries(mainReader, null, null, directoryStream, satSecIdChain, sectorSize, standardStreamSizeThreshold, shortSectorSize, ssatSecIdChain);

                    VisitEntries();

                    if (streamNameMatch != null)
                    {
                        if (returnOnFirstMatch.HasValue && returnOnFirstMatch.Value)
                        {
                            var matchedDirectoryEntry = DirectoryEntries.FirstOrDefault(de => streamNameMatch(de.Name));

                            return matchedDirectoryEntry != null ? new Dictionary<string, byte[]> { [matchedDirectoryEntry.Name] = matchedDirectoryEntry.Stream } : new Dictionary<string, byte[]>();
                        }

                        return DirectoryEntries.Where(de => streamNameMatch(de.Name)).ToDictionary(de => de.Name, de => de.Stream);
                    }

                    return null;
                }

                return ReadDirectoryEntries(mainReader, streamNameMatch, returnOnFirstMatch, directoryStream, satSecIdChain, sectorSize, standardStreamSizeThreshold, shortSectorSize, ssatSecIdChain);
            }
            catch (EndOfStreamException e)
            {
                throw new CdfException(Errors.UnexpectedEndOfStream, e);
            }
        }

        private void VisitEntries()
        {
            if (DirectoryEntries[0].RootNodeEntryDirId > 0)
            {
                var visitId = 0;

                VisitEntries(DirectoryEntries[0].RootNodeEntryDirId, ref visitId);

                DirectoryEntries = DirectoryEntries.Where(de => de.IsRootStorageDescendant)
                                                   .OrderBy(de => de.VisitId)
                                                   .ToList();
            }
        }

        private void VisitEntries(int directoryEntryId, ref int visitId)
        {
            if (directoryEntryId >= DirectoryEntries.Count) throw new CdfException(string.Format(Errors.ReferredChildDirectoryEntryMissing, directoryEntryId));
            if (DirectoryEntries[directoryEntryId].VisitId.HasValue) throw new CdfException(Errors.CyclicChildDirectoryEntryReference);


            DirectoryEntries[directoryEntryId].IsRootStorageDescendant = true;
            DirectoryEntries[directoryEntryId].VisitId = ++visitId;

            if (DirectoryEntries[directoryEntryId].RightChildDirId > 0)
            {
                VisitEntries(DirectoryEntries[directoryEntryId].RightChildDirId, ref visitId);
            }

            if (DirectoryEntries[directoryEntryId].LeftChildDirId > 0)
            {
                VisitEntries(DirectoryEntries[directoryEntryId].LeftChildDirId, ref visitId);
            }
        }

        private static void GetHeaderValues(ref BinaryBufferReader                      reader,
                                            out                int                  sectorSize,
                                            out                int             shortSectorSize,
                                            out                int              satSectorCount,
                                            out                int   firstSecIdDirectoryStream,
                                            out               uint standardStreamSizeThreshold,
                                            out                int              firstSecIdSsat,
                                            out               uint             ssatSectorCount,
                                            out                int      firstSecIdExtendedMsat,
                                            out                int        msatExtraSectorCount)
        {
            if (!reader.ReadSpan(8).SequenceEqual(HEADER_SIGNATURE)) throw new CdfException(Errors.HeaderSignatureMissing);

            reader.Position += 22;

            var sectorSizeExponent = reader.ReadUInt16();
            if (sectorSizeExponent < 7) throw new CdfException(Errors.SectorSizeTooSmall);
            sectorSize = (int)Math.Pow(2, sectorSizeExponent);

            var shortSectorSizeExponent = reader.ReadUInt16();
            if (shortSectorSizeExponent > sectorSizeExponent) throw new CdfException(Errors.ShortSectorSizeGreaterThanStandardSectorSize);
            shortSectorSize = (int)Math.Pow(2, shortSectorSizeExponent);

            reader.Position += 10;

            satSectorCount = (int)reader.ReadUInt32();
            firstSecIdDirectoryStream = reader.ReadInt32();

            reader.Position += 4;

            standardStreamSizeThreshold = reader.ReadUInt32();
            firstSecIdSsat = reader.ReadInt32();
            ssatSectorCount = reader.ReadUInt32();
            firstSecIdExtendedMsat = reader.ReadInt32();
            msatExtraSectorCount = reader.ReadInt32();
        }

        private static int[] BuildSatSecIdChain(BinaryBufferReader reader, int msatExtraSectorCount, int firstSecIdExtendedMsat, int satSectorCount, int sectorSize, int secIdsPerSector)
        {
#if DEBUG
            var msat = new int[1 + msatExtraSectorCount + 1];
            var sat = new int[satSectorCount + 1];
            msat[0] = SECID_MSAT;
#else
            var sat = new int[satSectorCount];
#endif
            var firstPartMsatSatSectorCount = Math.Min(satSectorCount, HEADER_MSAT_SAT_SECID_COUNT);


            var remainder = firstPartMsatSatSectorCount % 4;

            for (var i = 0; i < remainder; i++)
            {
                sat[i] = reader.ReadInt32();
            }

            if (firstPartMsatSatSectorCount >= 4)
            {
                var remainingSecIdCount = firstPartMsatSatSectorCount - remainder;

                for (var i = 0; i < remainingSecIdCount; i += 4)
                {
                    sat[remainder + i]     = reader.ReadInt32();
                    sat[remainder + i + 1] = reader.ReadInt32();
                    sat[remainder + i + 2] = reader.ReadInt32();
                    sat[remainder + i + 3] = reader.ReadInt32();
                }
            }

            
            if (firstPartMsatSatSectorCount < satSectorCount)
            {
                var satSectorIndex = (int)HEADER_MSAT_SAT_SECID_COUNT;
                var remainingMsatSatSectorCount = satSectorCount - HEADER_MSAT_SAT_SECID_COUNT;
                var currentSecIdMsat = firstSecIdExtendedMsat;
                var currentSectorPosMsat = HEADER_SIZE + currentSecIdMsat * sectorSize;

                for (var i = 0; i < msatExtraSectorCount; i++)
                {
#if DEBUG
                    msat[i + 1] = currentSecIdMsat;
#endif
                    reader.Position = currentSectorPosMsat;

                    var remainingSecIdsInCurrentSector = Math.Min(remainingMsatSatSectorCount, secIdsPerSector - 1);


                    remainder = remainingSecIdsInCurrentSector % 4;

                    for (var j = 0; j < remainder; j++)
                    {
                        sat[satSectorIndex++] = reader.ReadInt32();
                        remainingMsatSatSectorCount--;
                    }

                    if (remainingSecIdsInCurrentSector >= 4)
                    {
                        var remainingSecIdCount = remainingSecIdsInCurrentSector - remainder;

                        for (var j = 0; j < remainingSecIdCount; j += 4)
                        {
                            sat[satSectorIndex]     = reader.ReadInt32();
                            sat[satSectorIndex + 1] = reader.ReadInt32();
                            sat[satSectorIndex + 2] = reader.ReadInt32();
                            sat[satSectorIndex + 3] = reader.ReadInt32();

                            remainingMsatSatSectorCount -= 4;
                            satSectorIndex += 4;
                        }
                    }

                    if (remainingMsatSatSectorCount > 0)
                    {
                        currentSecIdMsat = reader.ReadInt32();
                        currentSectorPosMsat = HEADER_SIZE + currentSecIdMsat * sectorSize;
                    }
#if DEBUG
                    else
                    {
                        msat[1 + i + 1] = SECID_END_OF_CHAIN;
                        sat[satSectorIndex] = SECID_END_OF_CHAIN;
                    }
#endif
                }
            }
#if DEBUG
            else
            {
                msat[^1] = SECID_END_OF_CHAIN;
                sat[^1] = SECID_END_OF_CHAIN;
            }
#endif

            var satSecIdChain = new int[satSectorCount * secIdsPerSector];

            try
            {
                for (var i = 0; i < satSectorCount; i++)
                {
                    reader.Position = HEADER_SIZE + sat[i] * sectorSize;

                    for (var j = 0; j < secIdsPerSector; j++)
                    {
                        satSecIdChain[i * secIdsPerSector + j] = reader.ReadInt32();
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new CdfException(Errors.UnexpectedEndOfStream);
            }
            catch (IndexOutOfRangeException)
            {
                throw new CdfException(Errors.InvalidSecIdReference);
            }


            if (satSecIdChain.Length == 0) throw new CdfException(Errors.EmptySatSecIdChain);

            return satSecIdChain;
        }

        private static int[] BuildSsatSecIdChain(BinaryBufferReader reader, uint ssatSectorCount, int[] satSecIdChain, int firstSecIdSsat, int sectorSize, int secIdsPerSector)
        {
            int[] ssatSecIdChain = null;

#if DEBUG
            int[] ssat;
#endif

            if (ssatSectorCount > 0)
            {
#if DEBUG
                ssat = new int[ssatSectorCount + 1];
#endif
                ssatSecIdChain = new int[ssatSectorCount * secIdsPerSector];

                var currentSecIdSsat = firstSecIdSsat;

                try
                {
                    for (var i = 0; i < ssatSectorCount; i++)
                    {
#if DEBUG
                        ssat[i] = currentSecIdSsat;
#endif
                        reader.Position = HEADER_SIZE + currentSecIdSsat * sectorSize;

                        for (var j = 0; j < secIdsPerSector; j++)
                        {
                            ssatSecIdChain[i * secIdsPerSector + j] = reader.ReadInt32();
                        }

                        currentSecIdSsat = satSecIdChain[currentSecIdSsat];
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new CdfException(Errors.UnexpectedEndOfStream);
                }
                catch (IndexOutOfRangeException)
                {
                    throw new CdfException(Errors.InvalidSecIdReference);
                }
#if DEBUG
                ssat[^1] = SECID_END_OF_CHAIN;
#endif
            }


            return ssatSecIdChain;
        }

        private Dictionary<string, byte[]> ReadDirectoryEntries(BinaryBufferReader reader, Predicate<string> streamNameMatch, bool? returnOnFirstMatch, byte[] directoryStream, int[] satSecIdChain, int sectorSize, uint standardStreamSizeThreshold, int shortSectorSize, int[] ssatSecIdChain)
        {
            var matchedDirectoryEntries = new Dictionary<string, byte[]>(DirectoryEntries.Capacity);
            BinaryBufferReader shortStreamContainerStreamReader = null;

            var directoryStreamReader = new BinaryBufferReader(directoryStream);

            for (var i = 0; i < DirectoryEntries.Capacity; i++)
            {
                directoryStreamReader.Position = i * DIRECTORY_ENTRY_SIZE;

                var entryNameSequence = directoryStreamReader.ReadSpan(64);

                var entryNameSize = directoryStreamReader.ReadUInt16() - 2;

                //if (entryNameSize < 2) continue;
                if (entryNameSize > 62) throw new CdfException(Errors.DirectoryEntryNameTooLong);


                var entryType = (DirectoryEntry.EntryType)directoryStreamReader.ReadByte();

                //if (!Enum.IsDefined(typeof(DirectoryEntry.ObjectType), entryType) || entryType == DirectoryEntry.ObjectType.Empty) continue;


                var entryName = entryNameSize < 2 ? null : Encoding.Unicode.GetString(entryNameSequence.Slice(0, entryNameSize));

                if (i > 0 && streamNameMatch != null && !streamNameMatch(entryName)) continue;


                directoryStreamReader.Position += 1;

                var leftChildDirId = directoryStreamReader.ReadInt32();
                var rightChildDirId = directoryStreamReader.ReadInt32();
                var rootNodeEntryDirId = directoryStreamReader.ReadInt32();

                directoryStreamReader.Position += 36;

                var firstStreamSecId = directoryStreamReader.ReadInt32();
                var streamSize = (int)directoryStreamReader.ReadUInt32();


                byte[] entryStream = null;

                if (i == 0)
                {
                    if (entryType != DirectoryEntry.EntryType.RootStorage) throw new CdfException(Errors.FirstDirectoryEntryMustBeRootStorage);

                    if (ssatSecIdChain != null)
                    {
                        // Short-Stream Container Stream

                        if (streamSize == 0) throw new CdfException(Errors.ShortStreamContainerStreamSizeIsZero);

                        entryStream = ReadEntryStream(reader, streamSize, firstStreamSecId, sectorSize, HEADER_SIZE, satSecIdChain);
                        shortStreamContainerStreamReader = new BinaryBufferReader(entryStream);
                    }
                }
                else
                {
                    if (streamSize > 0)
                    {
                        if (streamSize < standardStreamSizeThreshold)
                        {
                            if (shortStreamContainerStreamReader == null) throw new CdfException(Errors.NoShortStreamContainerStreamDefined);

                            entryStream = ReadEntryStream(shortStreamContainerStreamReader, streamSize, firstStreamSecId, shortSectorSize, 0, ssatSecIdChain);
                        }
                        else
                        {
                            entryStream = ReadEntryStream(reader, streamSize, firstStreamSecId, sectorSize, HEADER_SIZE, satSecIdChain);
                        }
                    }
                }


                if (entryName != null && streamNameMatch != null && streamNameMatch(entryName))
                {
                    matchedDirectoryEntries[entryName] = entryStream;

                    if (returnOnFirstMatch.HasValue && returnOnFirstMatch.Value) break;
                }

                DirectoryEntries.Add(new DirectoryEntry(DirectoryEntries.Count, entryName, entryType, entryStream) { LeftChildDirId = leftChildDirId, RightChildDirId = rightChildDirId, RootNodeEntryDirId = rootNodeEntryDirId });
            }


            return streamNameMatch == null ? null : matchedDirectoryEntries;
        }

        private static byte[] ReadDirectoryStream(BinaryBufferReader reader, int firstSecIdDirectoryStream, int[] satSecIdChain, int sectorSize)
        {
            var directorySectorCount = 0;

            var currentSecIdDirectoryStream = firstSecIdDirectoryStream;
            var fast = currentSecIdDirectoryStream;

            try
            {
                while (currentSecIdDirectoryStream != SECID_END_OF_CHAIN)
                {
                    currentSecIdDirectoryStream = satSecIdChain[currentSecIdDirectoryStream];
                    directorySectorCount++;


                    // https://stackoverflow.com/a/2663147/633098

                    if (fast != SECID_END_OF_CHAIN && satSecIdChain[fast] != SECID_END_OF_CHAIN)
                    {
                        fast = satSecIdChain[satSecIdChain[fast]];
                        if (currentSecIdDirectoryStream == fast) throw new CdfException(Errors.CyclicSecIdChain);
                    }
                }
            }
            catch (IndexOutOfRangeException)
            {
                throw new CdfException(Errors.InvalidSecIdReference);
            }

#if DEBUG
            var directorySecIdChain = new int[directorySectorCount + 1];
#endif

            var directoryStreamLength = directorySectorCount * sectorSize;

            var directoryStream = new byte[directoryStreamLength];
            var directoryStreamWriter = new BinaryBufferWriter(directoryStream);

            currentSecIdDirectoryStream = firstSecIdDirectoryStream;
            for (var i = 0; i < directorySectorCount; i++)
            {
                reader.Position = HEADER_SIZE + currentSecIdDirectoryStream * sectorSize;

                directoryStreamWriter.Write(reader.ReadSpan(sectorSize));
#if DEBUG
                directorySecIdChain[i] = currentSecIdDirectoryStream;
#endif
                currentSecIdDirectoryStream = satSecIdChain[currentSecIdDirectoryStream];
            }
#if DEBUG
            directorySecIdChain[^1] = SECID_END_OF_CHAIN;
#endif

            return directoryStream;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] ReadEntryStream(BinaryBufferReader reader, int streamSize, int firstStreamSecId, int sectorSize, ushort initialOffset, int[] secIdChain)
        {
            var entryStream = new byte[streamSize];
            var entryStreamWriter = new BinaryBufferWriter(entryStream);

            var currentStreamSecId = firstStreamSecId;
            var streamSectorCount = (int)Math.Ceiling((double)streamSize / sectorSize);
            var remainingBytesToRead = streamSize;

            for (var j = 0; j < streamSectorCount; j++)
            {
                if (currentStreamSecId < 0) throw new CdfException(Errors.UnexpectedEndOfStream);

                reader.Position = initialOffset + currentStreamSecId * sectorSize;

                var bytesToRead = Math.Min(remainingBytesToRead, sectorSize);

                entryStreamWriter.Write(reader.ReadSpan(bytesToRead));

                remainingBytesToRead -= bytesToRead;

                currentStreamSecId = secIdChain[currentStreamSecId];
            }


            return entryStream;
        }



        /// <summary>
        /// If applicable, returns the file name of this compound document.
        /// </summary>
        public override string ToString() => _filepath != null ? Path.GetFileName(_filepath) : "Stream";

        /// <summary>
        /// Represents a directory entry in a compound document.
        /// </summary>
        public class DirectoryEntry
        {
            /// <summary>
            /// Original ID of the directory entry, based on its position within the directory stream.
            /// </summary>
            public int Id { get; }

            /// <summary>
            /// Name of the directory entry.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Type of the directory entry.
            /// <para>This could be a <see langword="stream"/> (file), a <see langword="storage"/> (directory) or the <see langword="root storage"/> (internal).</para>
            /// </summary>
            public EntryType Type { get; }

            internal int LeftChildDirId { get; set; }
            internal int RightChildDirId { get; set; }
            internal int RootNodeEntryDirId { get; set; }
            internal int? VisitId { get; set; }
            public bool IsRootStorageDescendant { get; internal set; }



            /// <summary>
            /// If the directory entry represents a <see langword="stream"/>, this property contains its data as a raw byte array.
            /// </summary>
            public byte[] Stream { get; }


            internal DirectoryEntry(int id, string name, EntryType type, byte[] stream)
            {
                Id = id;
                Name = name;
                Type = type;
                Stream = stream;
            }


            /// <summary>
            /// Type of the directory entry.
            /// <para>This could be a <see langword="stream"/> (file), a <see langword="storage"/> (directory) or the <see langword="root storage"/> (internal).</para>
            /// </summary>
            public enum EntryType : byte
            {
                /// <summary>
                /// Indicates an unknown or unassigned entry type.
                /// </summary>
                Empty       = 0,

                /// <summary>
                /// Indicates a storage (directory).
                /// </summary>
                Storage     = 1,

                /// <summary>
                /// Indicates a stream (file).
                /// </summary>
                Stream = 2,

                /// <summary>
                /// Indicates the root storage (internal).
                /// </summary>
                RootStorage = 5
            }

            /// <summary>
            /// Returns the name of the directory entry.
            /// </summary>
            /// <returns></returns>
            public override string ToString() => $"{Name ?? "<empty>"} {(Type == EntryType.Storage ? "<STORAGE>" : $"| {(Stream != null ? $"{Stream.Length} byte{(Stream.Length != 0 ? "s" : "")}" : "<empty>")}")}";
        }
    }
}
