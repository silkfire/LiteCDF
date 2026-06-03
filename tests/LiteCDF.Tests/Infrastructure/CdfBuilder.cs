namespace LiteCDF.Tests.Infrastructure;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Synthesizes byte-for-byte valid compound documents (CFBF / OLE2) in memory so the reader can be exercised
/// against deterministic inputs with known contents and structure.
/// <para>
/// The layout mirrors what real Office documents use (verified against actual <c>.xls</c> files): 512-byte standard
/// sectors, 64-byte short sectors, a 4096-byte standard-stream size threshold, and a single SAT sector referenced
/// from the first-part MSAT in the header. The member directory tree is emitted as a left-leaning chain, which the
/// reader's traversal handles identically to a balanced tree.
/// </para>
/// </summary>
internal sealed class CdfBuilder
{
    public const int SectorSize = 512;
    public const int ShortSectorSize = 64;
    public const int StandardStreamThreshold = 4_096;
    public const int HeaderSize = 512;
    public const int DirectoryEntrySize = 128;
    public const int SecIdSize = 4;
    public const int SecIdsPerSector = SectorSize / SecIdSize;     // 128
    public const int EntriesPerSector = SectorSize / DirectoryEntrySize; // 4

    public const int SecIdFree = -1;
    public const int SecIdEndOfChain = -2;
    public const int SecIdSat = -3;
    public const int SecIdMsat = -4;

    private static readonly byte[] Signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private enum MemberKind { Stream, Storage }

    private readonly record struct Member(string Name, byte[] Content, MemberKind Kind);

    private readonly List<Member> _members = [];

    // Populated by Build() so corruption tests can patch precise byte offsets.
    public int SatSectorIndex { get; private set; }
    public int DirectoryFirstSecId { get; private set; }
    private List<int> _directorySecIds = [];
    private int _directoryEntryCount;

    /// <summary>Adds a stream. Content &gt;= 4096 bytes is stored as a standard stream; smaller (non-empty) content as a short stream.</summary>
    public CdfBuilder AddStream(string name, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _members.Add(new Member(name, content, MemberKind.Stream));

        return this;
    }

    /// <summary>Adds a stream from a UTF-8 string (convenience).</summary>
    public CdfBuilder AddStream(string name, string content) => AddStream(name, Encoding.UTF8.GetBytes(content));

    /// <summary>Adds an empty (zero-length) stream.</summary>
    public CdfBuilder AddEmptyStream(string name) => AddStream(name, []);

    /// <summary>Adds a storage (directory) member. It has no stream of its own.</summary>
    public CdfBuilder AddStorage(string name)
    {
        _members.Add(new Member(name, [], MemberKind.Storage));
        return this;
    }

    /// <summary>
    /// Synthesizes the document with the current members and returns its bytes.
    /// </summary>
    public byte[] Build()
    {
        var sectors = new List<byte[]>();
        var satNext = new Dictionary<int, int>();

        // Resolve each member's first SecId and size. Short streams live inside the root short-stream container.
        var firstSecIds = new int[_members.Count];
        var streamSizes = new int[_members.Count];

        var ssatNext = new Dictionary<int, int>();
        var shortPlacements = new List<(int FirstShortSec, byte[] Content)>();
        var shortCursor = 0;
        var haveShortStreams = false;

        for (var m = 0; m < _members.Count; m++)
        {
            var member = _members[m];
            streamSizes[m] = member.Content.Length;

            if (member.Kind == MemberKind.Storage || member.Content.Length == 0)
            {
                firstSecIds[m] = SecIdEndOfChain;
                continue;
            }

            if (member.Content.Length >= StandardStreamThreshold)
            {
                firstSecIds[m] = WriteStandardChain(member.Content);
            }
            else
            {
                haveShortStreams = true;

                var shortSectorCount = (member.Content.Length + ShortSectorSize - 1) / ShortSectorSize;
                var first = shortCursor;
                firstSecIds[m] = first;

                for (var k = 0; k < shortSectorCount; k++)
                {
                    ssatNext[first + k] = k < shortSectorCount - 1 ? first + k + 1 : SecIdEndOfChain;
                }

                shortPlacements.Add((first, member.Content));
                shortCursor += shortSectorCount;
            }
        }

        // Root storage entry: its stream is the short-stream container (only present when short streams exist).
        int rootFirstSec;
        int rootSize;
        int firstSsatSecId;
        int ssatSectorCount;

        if (haveShortStreams)
        {
            var containerBytes = new byte[shortCursor * ShortSectorSize];
            foreach (var (firstShortSec, content) in shortPlacements)
            {
                content.CopyTo(containerBytes.AsSpan(firstShortSec * ShortSectorSize));
            }

            rootFirstSec = WriteStandardChain(containerBytes);
            rootSize = containerBytes.Length;

            var ssat = new int[SecIdsPerSector];
            Array.Fill(ssat, SecIdFree);
            foreach (var (shortSec, next) in ssatNext)
            {
                ssat[shortSec] = next;
            }

            var ssatSec = AllocSector();
            for (var i = 0; i < SecIdsPerSector; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(sectors[ssatSec].AsSpan(i * SecIdSize), ssat[i]);
            }
            satNext[ssatSec] = SecIdEndOfChain;

            firstSsatSecId = ssatSec;
            ssatSectorCount = 1;
        }
        else
        {
            rootFirstSec = SecIdEndOfChain;
            rootSize = 0;
            firstSsatSecId = SecIdEndOfChain;
            ssatSectorCount = 0;
        }

        // Directory: root entry (index 0) followed by members, padded to a full sector with empty entries.
        var realEntryCount = 1 + _members.Count;
        var paddedEntryCount = (realEntryCount + EntriesPerSector - 1) / EntriesPerSector * EntriesPerSector;
        _directoryEntryCount = paddedEntryCount;

        var directory = new byte[paddedEntryCount * DirectoryEntrySize];

        // Root storage. rootNode points at the first member (DID 1); members form a left-leaning chain.
        WriteDirectoryEntry(directory, 0, "Root Entry", entryType: 5,
                            leftChild: SecIdFree, rightChild: SecIdFree,
                            rootNode: _members.Count > 0 ? 1 : SecIdFree,
                            firstSec: rootFirstSec, size: rootSize);

        for (var m = 0; m < _members.Count; m++)
        {
            var did = 1 + m;
            var member = _members[m];
            var leftChild = m < _members.Count - 1 ? did + 1 : SecIdFree;

            WriteDirectoryEntry(directory, did, member.Name,
                                entryType: member.Kind == MemberKind.Storage ? (byte)1 : (byte)2,
                                leftChild: leftChild, rightChild: SecIdFree, rootNode: SecIdFree,
                                firstSec: firstSecIds[m], size: streamSizes[m]);
        }

        // Empty padding entries are already zeroed (type 0, name size 0).

        DirectoryFirstSecId = WriteStandardChain(directory);

        // Record the directory sector chain (for patch helpers).
        _directorySecIds = [];
        var cursor = DirectoryFirstSecId;
        for (var i = 0; i < paddedEntryCount / EntriesPerSector; i++)
        {
            _directorySecIds.Add(cursor);
            cursor = satNext[cursor];
        }

        // The SAT sector itself.
        var satSec = AllocSector();
        SatSectorIndex = satSec;

        var sat = new int[SecIdsPerSector];
        Array.Fill(sat, SecIdFree);
        foreach (var (sec, next) in satNext)
        {
            sat[sec] = next;
        }
        sat[satSec] = SecIdSat;

        for (var i = 0; i < SecIdsPerSector; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(sectors[satSec].AsSpan(i * SecIdSize), sat[i]);
        }

        // Assemble: header + all sectors in index order.
        var file = new byte[HeaderSize + sectors.Count * SectorSize];
        WriteHeader(file, satSectorCount: 1, firstDirSecId: DirectoryFirstSecId,
                    firstSsatSecId: firstSsatSecId, ssatSectorCount: ssatSectorCount, msatFirstEntry: satSec);

        for (var i = 0; i < sectors.Count; i++)
        {
            sectors[i].CopyTo(file.AsSpan(HeaderSize + i * SectorSize));
        }

        return file;

        int AllocSector()
        {
            sectors.Add(new byte[SectorSize]);

            return sectors.Count - 1;
        }

        int WriteStandardChain(byte[] content)
        {
            var sectorCount = Math.Max(1, (content.Length + SectorSize - 1) / SectorSize);
            var ids = new int[sectorCount];

            for (var i = 0; i < sectorCount; i++)
            {
                ids[i] = AllocSector();
            }

            for (var i = 0; i < sectorCount; i++)
            {
                var offset = i * SectorSize;
                var length = Math.Min(SectorSize, content.Length - offset);
                if (length > 0)
                {
                    content.AsSpan(offset, length).CopyTo(sectors[ids[i]]);
                }

                satNext[ids[i]] = i < sectorCount - 1 ? ids[i + 1] : SecIdEndOfChain;
            }

            return ids[0];
        }
    }

    private static void WriteHeader(byte[] file, int satSectorCount, int firstDirSecId, int firstSsatSecId, int ssatSectorCount, int msatFirstEntry)
    {
        Signature.CopyTo(file.AsSpan(0));

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x1E), 9); // sector size exponent -> 512
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x20), 6); // short sector size exponent -> 64

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x2C), satSectorCount);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x30), firstDirSecId);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x38), StandardStreamThreshold);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x3C), firstSsatSecId);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x40), ssatSectorCount);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x44), SecIdEndOfChain); // first extended-MSAT SecId
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x48), 0);               // extended-MSAT sector count

        // First-part MSAT: 109 entries starting at 0x4C. Entry 0 -> the SAT sector; rest free.
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x4C), msatFirstEntry);
        for (var i = 1; i < 109; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x4C + i * SecIdSize), SecIdFree);
        }
    }

    private static void WriteDirectoryEntry(byte[] directory, int index, string name, byte entryType, int leftChild, int rightChild, int rootNode, int firstSec, int size)
    {
        var baseOffset = index * DirectoryEntrySize;

        if (name is { Length: > 0 })
        {
            var nameBytes = Encoding.Unicode.GetBytes(name);
            nameBytes.CopyTo(directory.AsSpan(baseOffset));
            BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(baseOffset + 64), (ushort)(nameBytes.Length + 2)); // include null terminator
        }

        directory[baseOffset + 66] = entryType;
        directory[baseOffset + 67] = 1; // node color (black) — unused by reader
        BinaryPrimitives.WriteInt32LittleEndian(directory.AsSpan(baseOffset + 68), leftChild);
        BinaryPrimitives.WriteInt32LittleEndian(directory.AsSpan(baseOffset + 72), rightChild);
        BinaryPrimitives.WriteInt32LittleEndian(directory.AsSpan(baseOffset + 76), rootNode);
        BinaryPrimitives.WriteInt32LittleEndian(directory.AsSpan(baseOffset + 116), firstSec);
        BinaryPrimitives.WriteInt32LittleEndian(directory.AsSpan(baseOffset + 120), size);
    }

    /// <summary>Returns the file byte offset of the SAT entry for the specified sector ID (its "next sector" pointer).</summary>
    public int GetSatEntryOffset(int secId) => HeaderSize + SatSectorIndex * SectorSize + secId * SecIdSize;

    /// <summary>Returns the file byte offset for the specified directory entry index (0 = root, 1.. = members).</summary>
    public int GetDirectoryEntryOffset(int directoryIndex)
    {
        var sectorIndex = directoryIndex / EntriesPerSector;
        var offsetInSector = directoryIndex % EntriesPerSector * DirectoryEntrySize;

        return HeaderSize + _directorySecIds[sectorIndex] * SectorSize + offsetInSector;
    }

    public const int HeaderMsatSatSecIdCount = 109; // SAT-sector SecIds held directly in the header's first-part MSAT

    /// <summary>
    /// Synthesizes a document whose SAT spans more sectors than the header's first-part MSAT can hold (110 &gt; 109),
    /// forcing the reader down the extended-MSAT branch in <c>BuildSatSecIdChain</c>. The 110th SAT-sector SecId
    /// lives in a single extended-MSAT sector. Only one real stream is stored; the surplus SAT sectors exist
    /// physically (so the reader can read them) but describe only free sectors, keeping the file small.
    /// <para>Returns the file bytes and the name of the single stream member it contains.</para>
    /// </summary>
    public static (byte[] File, string StreamName, byte[] StreamContent) BuildWithExtendedMsat()
    {
        const int satSectorCount = HeaderMsatSatSecIdCount + 1; // 110 -> one entry spills into the extended MSAT
        const string streamName = "Big";

        // A standard stream (>= threshold) so it occupies its own SAT-tracked sectors rather than the SSAT.
        var content = new byte[StandardStreamThreshold + 137];
        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte)(i % 251);
        }

        var sectors = new List<byte[]>();
        var satNext = new Dictionary<int, int>();

        var streamFirstSec = WriteStandardChain(content);

        // Directory: root + the single stream member, padded to a full sector.
        const int paddedEntryCount = (1 + 1 + EntriesPerSector - 1) / EntriesPerSector * EntriesPerSector;
        var directory = new byte[paddedEntryCount * DirectoryEntrySize];

        WriteDirectoryEntry(directory, 0, "Root Entry", entryType: 5,
                            leftChild: SecIdFree, rightChild: SecIdFree, rootNode: 1,
                            firstSec: SecIdEndOfChain, size: 0);
        WriteDirectoryEntry(directory, 1, streamName, entryType: 2,
                            leftChild: SecIdFree, rightChild: SecIdFree, rootNode: SecIdFree,
                            firstSec: streamFirstSec, size: content.Length);

        var directoryFirstSec = WriteStandardChain(directory);

        // Allocate the 110 SAT sectors and the single extended-MSAT sector physically.
        var satSecs = new int[satSectorCount];
        for (var i = 0; i < satSectorCount; i++)
        {
            satSecs[i] = AllocSector();
        }

        var extendedMsatSec = AllocSector();

        // Mark the SAT and MSAT sectors in the allocation table so the chain stays self-consistent.
        foreach (var s in satSecs)
        {
            satNext[s] = SecIdSat;
        }
        satNext[extendedMsatSec] = SecIdMsat;

        // Build the global SAT: satSectorCount * 128 entries, defaulting to free, then apply satNext.
        const int totalSatEntries = satSectorCount * SecIdsPerSector;
        var sat = new int[totalSatEntries];
        Array.Fill(sat, SecIdFree);

        foreach (var (sec, next) in satNext)
        {
            sat[sec] = next;
        }

        // Serialize the SAT across the 110 SAT sectors.
        for (var s = 0; s < satSectorCount; s++)
        {
            for (var e = 0; e < SecIdsPerSector; e++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(sectors[satSecs[s]].AsSpan(e * SecIdSize), sat[s * SecIdsPerSector + e]);
            }
        }

        // The single extended-MSAT sector carries the 110th SAT-sector SecId in slot 0; the rest stay free.
        Array.Fill(sectors[extendedMsatSec], (byte)0xFF); // 0xFFFFFFFF == SecIdFree for every slot by default
        BinaryPrimitives.WriteInt32LittleEndian(sectors[extendedMsatSec].AsSpan(0), satSecs[HeaderMsatSatSecIdCount]);

        var file = new byte[HeaderSize + sectors.Count * SectorSize];

        Signature.CopyTo(file.AsSpan(0));
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x1E), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x20), 6);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x2C), satSectorCount);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x30), directoryFirstSec);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x38), StandardStreamThreshold);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x3C), SecIdEndOfChain); // no SSAT
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x40), 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x44), extendedMsatSec); // first extended-MSAT SecId
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x48), 1);               // extended-MSAT sector count

        // First-part MSAT: the first 109 SAT-sector SecIds.
        for (var i = 0; i < HeaderMsatSatSecIdCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x4C + i * SecIdSize), satSecs[i]);
        }

        for (var i = 0; i < sectors.Count; i++)
        {
            sectors[i].CopyTo(file.AsSpan(HeaderSize + i * SectorSize));
        }

        return (file, streamName, content);

        int AllocSector()
        {
            sectors.Add(new byte[SectorSize]);

            return sectors.Count - 1;
        }

        int WriteStandardChain(byte[] data)
        {
            var count = Math.Max(1, (data.Length + SectorSize - 1) / SectorSize);
            var ids = new int[count];
            for (var i = 0; i < count; i++)
            {
                ids[i] = AllocSector();
            }
            for (var i = 0; i < count; i++)
            {
                var offset = i * SectorSize;
                var length = Math.Min(SectorSize, data.Length - offset);
                if (length > 0)
                {
                    data.AsSpan(offset, length).CopyTo(sectors[ids[i]]);
                }
                satNext[ids[i]] = i < count - 1 ? ids[i + 1] : SecIdEndOfChain;
            }

            return ids[0];
        }
    }

    public const int MsatSatSecIdsPerSector = SecIdsPerSector - 1; // 127; the 128th slot chains to the next MSAT sector

    /// <summary>
    /// Synthesizes a ~15 MB document whose SAT is large enough (237 sectors) to span <b>multiple</b> extended-MSAT
    /// sectors, forcing the reader to follow the next-MSAT-sector pointer in <c>BuildSatSecIdChain</c>. The 237 SAT-sector
    /// SecIds are distributed as 109 (header first-part MSAT) + 127 (first extended-MSAT sector) + 1 (second extended-MSAT
    /// sector); slot 127 of the first MSAT sector holds the SecId of the second. As with <see cref="BuildWithExtendedMsat"/>,
    /// the surplus SAT entries describe free sectors, but the file is physically padded to ~15 MB so it is a genuine
    /// large-document input.
    /// <para>Returns the file bytes and the name/content of the single stream member it contains.</para>
    /// </summary>
    public static (byte[] File, string StreamName, byte[] StreamContent) BuildWithMultiSectorExtendedMsat()
    {
        // 240 SAT sectors map 240 * 128 = 30,720 physical sectors (~15 MB). The SecIds spill as
        // 109 (header) + 127 (first extended-MSAT sector) + 4 (second extended-MSAT sector), so the reader
        // must follow the next-MSAT-sector pointer to reach the second sector.
        const int satSectorCount = 240;
        const int msatExtraSectorCount = 2;
        const string streamName = "Big";

        const int secIdsInSecondMsat = satSectorCount - HeaderMsatSatSecIdCount - MsatSatSecIdsPerSector; // 4

        var content = new byte[StandardStreamThreshold + 137];
        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte)(i % 251);
        }

        var sectors = new List<byte[]>();
        var satNext = new Dictionary<int, int>();

        var streamFirstSec = WriteStandardChain(content);

        const int paddedEntryCount = (1 + 1 + EntriesPerSector - 1) / EntriesPerSector * EntriesPerSector;
        var directory = new byte[paddedEntryCount * DirectoryEntrySize];

        WriteDirectoryEntry(directory, 0, "Root Entry", entryType: 5,
                            leftChild: SecIdFree, rightChild: SecIdFree, rootNode: 1,
                            firstSec: SecIdEndOfChain, size: 0);
        WriteDirectoryEntry(directory, 1, streamName, entryType: 2,
                            leftChild: SecIdFree, rightChild: SecIdFree, rootNode: SecIdFree,
                            firstSec: streamFirstSec, size: content.Length);

        var directoryFirstSec = WriteStandardChain(directory);

        // Pad with free sectors so the file reaches ~15 MB. These are never referenced (all-free in the SAT),
        // they only make the document physically large. The total physical sector count must not exceed what the
        // SAT can map (satSectorCount * 128 = 30,720), so every sector — including the SAT/MSAT sectors themselves —
        // has a SAT entry. 30,720 sectors + header is ~15 MB.
        const int totalPhysicalSectors = satSectorCount * SecIdsPerSector;
        while (sectors.Count < totalPhysicalSectors - satSectorCount - msatExtraSectorCount)
        {
            AllocSector();
        }

        // Allocate the 240 SAT sectors and the two extended-MSAT sectors physically (the last sectors in the file).
        var satSecs = new int[satSectorCount];
        for (var i = 0; i < satSectorCount; i++)
        {
            satSecs[i] = AllocSector();
        }
        var firstMsatSec = AllocSector();
        var secondMsatSec = AllocSector();

        foreach (var s in satSecs)
        {
            satNext[s] = SecIdSat;
        }
        satNext[firstMsatSec] = SecIdMsat;
        satNext[secondMsatSec] = SecIdMsat;

        const int totalSatEntries = satSectorCount * SecIdsPerSector;
        var sat = new int[totalSatEntries];
        Array.Fill(sat, SecIdFree);
        foreach (var (sec, next) in satNext)
        {
            sat[sec] = next;
        }

        for (var s = 0; s < satSectorCount; s++)
        {
            for (var e = 0; e < SecIdsPerSector; e++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(sectors[satSecs[s]].AsSpan(e * SecIdSize), sat[s * SecIdsPerSector + e]);
            }
        }

        // First extended-MSAT sector: SAT-sector SecIds 109..235 in slots 0..126, then the next-MSAT pointer in slot 127.
        Array.Fill(sectors[firstMsatSec], (byte)0xFF);
        for (var i = 0; i < MsatSatSecIdsPerSector; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(sectors[firstMsatSec].AsSpan(i * SecIdSize), satSecs[HeaderMsatSatSecIdCount + i]);
        }
        BinaryPrimitives.WriteInt32LittleEndian(sectors[firstMsatSec].AsSpan(MsatSatSecIdsPerSector * SecIdSize), secondMsatSec);

        // Second extended-MSAT sector: the remaining SAT-sector SecIds in slots 0..(secIdsInSecondMsat-1); rest free.
        Array.Fill(sectors[secondMsatSec], (byte)0xFF);
        for (var i = 0; i < secIdsInSecondMsat; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(sectors[secondMsatSec].AsSpan(i * SecIdSize), satSecs[HeaderMsatSatSecIdCount + MsatSatSecIdsPerSector + i]);
        }

        var file = new byte[HeaderSize + sectors.Count * SectorSize];

        Signature.CopyTo(file.AsSpan(0));
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x1E), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x20), 6);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x2C), satSectorCount);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x30), directoryFirstSec);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x38), StandardStreamThreshold);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x3C), SecIdEndOfChain); // no SSAT
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x40), 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x44), firstMsatSec);     // first extended-MSAT SecId
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x48), msatExtraSectorCount);

        for (var i = 0; i < HeaderMsatSatSecIdCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x4C + i * SecIdSize), satSecs[i]);
        }

        for (var i = 0; i < sectors.Count; i++)
        {
            sectors[i].CopyTo(file.AsSpan(HeaderSize + i * SectorSize));
        }

        return (file, streamName, content);

        int AllocSector()
        {
            sectors.Add(new byte[SectorSize]);

            return sectors.Count - 1;
        }

        int WriteStandardChain(byte[] data)
        {
            var count = Math.Max(1, (data.Length + SectorSize - 1) / SectorSize);
            var ids = new int[count];

            for (var i = 0; i < count; i++)
            {
                ids[i] = AllocSector();
            }

            for (var i = 0; i < count; i++)
            {
                var offset = i * SectorSize;
                var length = Math.Min(SectorSize, data.Length - offset);
                if (length > 0)
                {
                    data.AsSpan(offset, length).CopyTo(sectors[ids[i]]);
                }
                satNext[ids[i]] = i < count - 1 ? ids[i + 1] : SecIdEndOfChain;
            }

            return ids[0];
        }
    }

    /// <summary>
    /// Synthesizes a document with a single SAT sector (so the SAT chain maps only 128 sectors) but enough physical
    /// sectors that the SSAT's first SecId can point at a sector that is physically readable yet beyond the SAT
    /// chain's length. Reading the SSAT sector succeeds, but the follow-up <c>satSecIdChain[currentSecIdSsat]</c>
    /// lookup in <c>BuildSsatSecIdChain</c> is out of range, surfacing as <c>InvalidSecIdReference</c>.
    /// </summary>
    public static byte[] BuildWithSsatPointerBeyondSatChain()
    {
        const int satSectorCount = 1;                       // satSecIdChain length = 128
        const int physicalSectorCount = SecIdsPerSector + 8; // 136 -> a valid SecId can exceed the chain length
        const int ssatSecId = SecIdsPerSector + 2;          // 130: in-file (readable) but >= 128 (out of chain range)

        var sectors = new byte[physicalSectorCount][];
        for (var i = 0; i < physicalSectorCount; i++)
        {
            sectors[i] = new byte[SectorSize];
        }

        // Directory: root + one (empty) member, written into sector 0.
        var directory = new byte[EntriesPerSector * DirectoryEntrySize];
        WriteDirectoryEntry(directory, 0, "Root Entry", entryType: 5,
                            leftChild: SecIdFree, rightChild: SecIdFree, rootNode: 1,
                            firstSec: SecIdEndOfChain, size: 0);
        WriteDirectoryEntry(directory, 1, "Member", entryType: 2,
                            leftChild: SecIdFree, rightChild: SecIdFree, rootNode: SecIdFree,
                            firstSec: SecIdEndOfChain, size: 0);
        const int directorySec = 0;
        directory.CopyTo(sectors[directorySec].AsSpan());

        // Single SAT sector (sector 1) describing the 128 sectors it can map. Everything is free except the
        // structural sectors, which keeps the chain self-consistent for the parts the reader actually walks.
        const int satSec = 1;
        var sat = new int[SecIdsPerSector];

        Array.Fill(sat, SecIdFree);
        sat[directorySec] = SecIdEndOfChain;
        sat[satSec] = SecIdSat;

        for (var e = 0; e < SecIdsPerSector; e++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(sectors[satSec].AsSpan(e * SecIdSize), sat[e]);
        }

        var file = new byte[HeaderSize + physicalSectorCount * SectorSize];

        Signature.CopyTo(file.AsSpan(0));
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x1E), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x20), 6);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x2C), satSectorCount);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x30), directorySec);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x38), StandardStreamThreshold);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x3C), ssatSecId); // SSAT points beyond the SAT chain
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x40), 1);         // ssatSectorCount = 1
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x44), SecIdEndOfChain);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x48), 0);

        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x4C), satSec);
        for (var i = 1; i < HeaderMsatSatSecIdCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x4C + i * SecIdSize), SecIdFree);
        }

        for (var i = 0; i < physicalSectorCount; i++)
        {
            sectors[i].CopyTo(file.AsSpan(HeaderSize + i * SectorSize));
        }

        return file;
    }
}
