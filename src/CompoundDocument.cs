namespace LiteCDF;

using BinaryBuffers;
using StreamExtensions;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private static readonly byte[] HEADER_SIGNATURE            = [ 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 ];     //  ��ࡱ�

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


    private string? _filepath;
    private byte[] _data = null!;
    private int[] _satSecIdChain = null!;
    private int[]? _ssatSecIdChain;
    private int _sectorSize;
    private int _shortSectorSize;
    private uint _standardStreamSizeThreshold;

    private List<DirectoryEntry> _directoryEntries = null!;

    // The root storage entry holds the short-stream container. It is kept separately because
    // VisitEntries() may later replace _directoryEntries with a filtered list that excludes the root,
    // and short-stream reads must still be able to locate the container.
    private DirectoryEntry _rootStorageEntry = null!;

    /// <summary>
    /// The directory entries contained in this compound document.
    /// </summary>
    public ReadOnlyCollection<DirectoryEntry> DirectoryEntries => _directoryEntries.AsReadOnly();

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

    internal ReadOnlyDictionary<string, byte[]>? Mount(string filepath, Predicate<string>? streamNameMatch, bool? returnOnFirstMatch, bool rootStorageDescendantsOnly)
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

    internal ReadOnlyDictionary<string, byte[]>? Mount(byte[] data, Predicate<string>? streamNameMatch, bool? returnOnFirstMatch, bool rootStorageDescendantsOnly)
    {
        _data = data;
        var mainReader = new BinaryBufferReader(data);

        try
        {
            GetHeaderValues(ref mainReader,
                            out _sectorSize,
                            out _shortSectorSize,
                            out var satSectorCount,
                            out var firstSecIdDirectoryStream,
                            out _standardStreamSizeThreshold,
                            out var firstSecIdSsat,
                            out var ssatSectorCount,
                            out var firstSecIdExtendedMsat,
                            out var msatExtraSectorCount);


            // Master Sector Allocation Table (MSAT) / Sector Allocation Table (SAT)

            var secIdsPerSector = _sectorSize / SECID_SIZE;

            _satSecIdChain = BuildSatSecIdChain(mainReader, msatExtraSectorCount, firstSecIdExtendedMsat, satSectorCount, _sectorSize, secIdsPerSector);


            // Short-Sector Allocation Table (SSAT)

            _ssatSecIdChain = BuildSsatSecIdChain(mainReader, ssatSectorCount, _satSecIdChain, firstSecIdSsat, _sectorSize, secIdsPerSector);


            // Directory

            var directorySecIdChain = GetDirectoryStreamSecIdChain(firstSecIdDirectoryStream, _satSecIdChain);

            _directoryEntries = new List<DirectoryEntry>(directorySecIdChain.Count * (_sectorSize / DIRECTORY_ENTRY_SIZE));

            if (rootStorageDescendantsOnly)
            {
                ReadDirectoryEntries(mainReader, null, null, directorySecIdChain);

                VisitEntries();

                if (streamNameMatch != null)
                {
                    if (returnOnFirstMatch == true)
                    {
                        var matchedDirectoryEntry = _directoryEntries.FirstOrDefault(de => streamNameMatch(de.Name!));

                        return matchedDirectoryEntry != null
                            ? new Dictionary<string, byte[]> { [matchedDirectoryEntry.Name!] = matchedDirectoryEntry.Stream! }.AsReadOnly()
                            : ReadOnlyDictionary<string, byte[]>.Empty;
                    }

                    return _directoryEntries.Where(de => streamNameMatch(de.Name!)).ToDictionary(de => de.Name!, de => de.Stream!).AsReadOnly();
                }

                return null;
            }

            return ReadDirectoryEntries(mainReader, streamNameMatch, returnOnFirstMatch, directorySecIdChain);
        }
        catch (Exception e) when (e is EndOfStreamException or ArgumentOutOfRangeException)
        {
            throw new CdfException(Errors.UnexpectedEndOfStream, e);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void VisitEntries()
    {
        if (_directoryEntries[0].RootNodeEntryDirId > 0)
        {
            var visitId = 0;

            VisitEntries(_directoryEntries[0].RootNodeEntryDirId, ref visitId);

            _directoryEntries = _directoryEntries.Where(de => de.IsRootStorageDescendant)
                                                 .OrderBy(de => de.VisitId)
                                                 .ToList();
        }
    }

    private void VisitEntries(int directoryEntryId, ref int visitId)
    {
        while (true)
        {
            if (directoryEntryId >= _directoryEntries.Count)
            {
                throw new CdfException(string.Format(Errors.ReferredChildDirectoryEntryMissing, directoryEntryId));
            }

            if (_directoryEntries[directoryEntryId].VisitId.HasValue)
            {
                throw new CdfException(Errors.CyclicChildDirectoryEntryReference);
            }

            _directoryEntries[directoryEntryId].IsRootStorageDescendant = true;
            _directoryEntries[directoryEntryId].VisitId = ++visitId;

            if (_directoryEntries[directoryEntryId].RightChildDirId > 0)
            {
                VisitEntries(_directoryEntries[directoryEntryId].RightChildDirId, ref visitId);
            }

            if (_directoryEntries[directoryEntryId].LeftChildDirId > 0)
            {
                directoryEntryId = _directoryEntries[directoryEntryId].LeftChildDirId;

                continue;
            }

            break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        if (!reader.ReadSpan(8).SequenceEqual(HEADER_SIGNATURE))
        {
            throw new CdfException(Errors.HeaderSignatureMissing);
        }

        reader.Position += 22;

        var sectorSizeExponent = reader.ReadUInt16();
        if (sectorSizeExponent < 7)
        {
            throw new CdfException(Errors.SectorSizeTooSmall);
        }

        sectorSize = 1 << sectorSizeExponent;

        var shortSectorSizeExponent = reader.ReadUInt16();
        if (shortSectorSizeExponent > sectorSizeExponent)
        {
            throw new CdfException(Errors.ShortSectorSizeGreaterThanStandardSectorSize);
        }

        shortSectorSize = 1 << shortSectorSizeExponent;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        reader.ReadInto(sat.AsSpan(0, firstPartMsatSatSectorCount));


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

                reader.ReadInto(sat.AsSpan(satSectorIndex, remainingSecIdsInCurrentSector));
                satSectorIndex += remainingSecIdsInCurrentSector;
                remainingMsatSatSectorCount -= remainingSecIdsInCurrentSector;

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

                reader.ReadInto(satSecIdChain.AsSpan(i * secIdsPerSector, secIdsPerSector));
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new CdfException(Errors.UnexpectedEndOfStream);
        }

        if (satSecIdChain.Length == 0)
        {
            throw new CdfException(Errors.EmptySatSecIdChain);
        }

        return satSecIdChain;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int[]? BuildSsatSecIdChain(BinaryBufferReader reader, uint ssatSectorCount, int[] satSecIdChain, int firstSecIdSsat, int sectorSize, int secIdsPerSector)
    {
        int[]? ssatSecIdChain = null;

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

                    reader.ReadInto(ssatSecIdChain.AsSpan(i * secIdsPerSector, secIdsPerSector));

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

        internal byte[] FetchStreamData(int startSector, int size, bool isShortStream)
        {
            if (isShortStream)
            {
                var rootStorageStream = _rootStorageEntry.Stream!;
                var reader = new BinaryBufferReader(rootStorageStream);

                return ReadEntryStream(reader, size, startSector, _shortSectorSize, 0, _ssatSecIdChain!);
            }
            else
            {
                var reader = new BinaryBufferReader(_data);
                return ReadEntryStream(reader, size, startSector, _sectorSize, HEADER_SIZE, _satSecIdChain);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlyDictionary<string, byte[]>? ReadDirectoryEntries(BinaryBufferReader reader, Predicate<string>? streamNameMatch, bool? returnOnFirstMatch, List<int> directorySecIdChain)
        {
            var matchedDirectoryEntries = new Dictionary<string, byte[]>(_directoryEntries.Capacity);

            var entriesPerSector = _sectorSize / DIRECTORY_ENTRY_SIZE;

            var currentSectorIndex = -1;
            var sectorBasePosition = 0;

            for (var i = 0; i < _directoryEntries.Capacity; i++)
            {
                var sectorIndex = i / entriesPerSector;
                if (sectorIndex != currentSectorIndex)
                {
                    currentSectorIndex = sectorIndex;
                    sectorBasePosition = HEADER_SIZE + directorySecIdChain[sectorIndex] * _sectorSize;
                }

                var entryOffsetInSector = i % entriesPerSector * DIRECTORY_ENTRY_SIZE;

                reader.Position = sectorBasePosition + entryOffsetInSector;

                var entryNameSequence = reader.ReadSpan(64);

                var entryNameSize = reader.ReadUInt16() - 2;

                if (entryNameSize > 62)
                {
                    throw new CdfException(Errors.DirectoryEntryNameTooLong);
                }

                var entryType = (DirectoryEntry.EntryType)reader.ReadByte();
                var entryName = entryNameSize < 2 ? null : Encoding.Unicode.GetString(entryNameSequence[..entryNameSize]);

                if (i > 0 && streamNameMatch != null && !streamNameMatch(entryName!))
                {
                    continue;
                }

                reader.Position += 1;

                var leftChildDirId = reader.ReadInt32();
                var rightChildDirId = reader.ReadInt32();
                var rootNodeEntryDirId = reader.ReadInt32();

                reader.Position += 36;

                var firstStreamSecId = reader.ReadInt32();
                var streamSize = (int)reader.ReadUInt32();

                if (i == 0)
                {
                    if (entryType != DirectoryEntry.EntryType.RootStorage)
                    {
                        throw new CdfException(Errors.FirstDirectoryEntryMustBeRootStorage);
                    }

                    if (_ssatSecIdChain != null && streamSize == 0)
                    {
                        throw new CdfException(Errors.ShortStreamContainerStreamSizeIsZero);
                    }
                }

                var isShortStream = false;
                if (i > 0 && streamSize > 0 && streamSize < _standardStreamSizeThreshold)
                {
                    if (_ssatSecIdChain == null)
                    {
                        throw new CdfException(Errors.NoShortStreamContainerStreamDefined);
                    }

                    isShortStream = true;
                }

                var entry = new DirectoryEntry(this, _directoryEntries.Count, entryName, entryType, firstStreamSecId, streamSize, isShortStream)
                {
                    LeftChildDirId = leftChildDirId,
                    RightChildDirId = rightChildDirId,
                    RootNodeEntryDirId = rootNodeEntryDirId
                };

                if (i == 0)
                {
                    _rootStorageEntry = entry;
                }

                if (entryName != null && streamNameMatch != null && streamNameMatch(entryName))
                {
                    matchedDirectoryEntries[entryName] = entry.Stream!;

                    if (returnOnFirstMatch == true)
                    {
                        break;
                    }
                }

                _directoryEntries.Add(entry);
            }

            return streamNameMatch == null ? null : matchedDirectoryEntries.AsReadOnly();
        }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static List<int> GetDirectoryStreamSecIdChain(int firstSecIdDirectoryStream, int[] satSecIdChain)
    {
        // The directory stream of typical documents spans only a handful of sectors; seeding a modest capacity covers that common case in a single allocation and avoids the list's early growth churn.
        var directorySecIdChain = new List<int>(16);

        var currentSecIdDirectoryStream = firstSecIdDirectoryStream;
        var fast = currentSecIdDirectoryStream;

        try
        {
            while (currentSecIdDirectoryStream != SECID_END_OF_CHAIN)
            {
                directorySecIdChain.Add(currentSecIdDirectoryStream);
                currentSecIdDirectoryStream = satSecIdChain[currentSecIdDirectoryStream];

                // Floyd's Cycle-Finding Algorithm
                // https://stackoverflow.com/a/2663147/633098

                // 'currentSecIdDirectoryStream' moves one step at a time
                // 'fast' moves two steps at a time
                // If they ever meet, there is a cycle in the chain

                if (fast != SECID_END_OF_CHAIN && satSecIdChain[fast] != SECID_END_OF_CHAIN)
                {
                    fast = satSecIdChain[satSecIdChain[fast]];
                    if (currentSecIdDirectoryStream == fast)
                    {
                        throw new CdfException(Errors.CyclicSecIdChain);
                    }
                }
            }
        }
        catch (IndexOutOfRangeException)
        {
            throw new CdfException(Errors.InvalidSecIdReference);
        }

        return directorySecIdChain;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] ReadEntryStream(BinaryBufferReader reader, int streamSize, int firstStreamSecId, int sectorSize, ushort initialOffset, int[] secIdChain)
    {
        var entryStream = new byte[streamSize];
        var destination = entryStream.AsSpan();

        var currentStreamSecId = firstStreamSecId;
        var streamSectorCount = (int)Math.Ceiling((double)streamSize / sectorSize);
        var remainingBytesToRead = streamSize;

        for (var j = 0; j < streamSectorCount; j++)
        {
            if (currentStreamSecId < 0)
            {
                throw new CdfException(Errors.UnexpectedEndOfStream);
            }

            reader.Position = initialOffset + currentStreamSecId * sectorSize;

            var bytesToRead = Math.Min(remainingBytesToRead, sectorSize);

            reader.ReadSpan(bytesToRead).CopyTo(destination);
            destination = destination[bytesToRead..];

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
        public string? Name { get; }

        /// <summary>
        /// Indicates the <strong>type</strong> of the directory entry.
        /// <para>This could be a <see langword="stream"/> (file), a <see langword="storage"/> (directory) or the <see langword="root storage"/> (internal).</para>
        /// </summary>
        public EntryType Type { get; }

        internal int LeftChildDirId { get; set; }
        internal int RightChildDirId { get; set; }
        internal int RootNodeEntryDirId { get; set; }
        internal int? VisitId { get; set; }

        /// <summary>
        /// Indicates whether this directory entry is a direct descendant of the root storage.
        /// </summary>
        public bool IsRootStorageDescendant { get; internal set; }

        /// <summary>
        /// If the directory entry represents a <see langword="stream"/>, this property contains its data as a raw byte array.
        /// </summary>
        public byte[]? Stream
        {
            get
            {
                if (field == null && _streamSize > 0)
                {
                    field = _document.FetchStreamData(_firstStreamSecId, _streamSize, _isShortStream);
                }

                return field;
            }
        }

        private readonly CompoundDocument _document;
        private readonly int _firstStreamSecId;
        private readonly int _streamSize;
        private readonly bool _isShortStream;

        internal DirectoryEntry(CompoundDocument document, int id, string? name, EntryType type, int firstStreamSecId, int streamSize, bool isShortStream)
        {
            _document = document;
            _firstStreamSecId = firstStreamSecId;
            _streamSize = streamSize;
            _isShortStream = isShortStream;

            Id = id;
            Name = name;
            Type = type;
        }

        /// <summary>
        /// Type of the directory entry.
        /// <para>This could be a <see langword="stream"/> (file), a <see langword="storage"/> (directory) or the <see langword="root storage"/> (internal).</para>
        /// </summary>
        public enum EntryType
        {
            /// <summary>
            /// Indicates an unknown or unassigned entry type.
            /// </summary>
            Empty = 0,

            /// <summary>
            /// Indicates a storage (directory).
            /// </summary>
            Storage = 1,

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
        public override string ToString() => $"{Name ?? "<empty>"} {(Type == EntryType.Storage ? "<STORAGE>" : $"| {(_streamSize > 0 ? $"{_streamSize} byte{(_streamSize != 1 ? "s" : "")}" : "<empty>")}")}";
    }
}
