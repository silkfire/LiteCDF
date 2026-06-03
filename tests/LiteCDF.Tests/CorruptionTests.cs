namespace LiteCDF.Tests;

using Infrastructure;

using System;
using System.Buffers.Binary;
using System.Linq;

using Xunit;

/// <summary>
/// Each test builds a valid document and then patches a single field to provoke one specific corruption path,
/// asserting the exact error so the diagnostics stay meaningful.
/// </summary>
public class CorruptionTests
{
    private static void PatchInt32(byte[] data, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
    private static void PatchUInt16(byte[] data, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);

    [Fact]
    public void Missing_header_signature_throws()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();
        bytes.AsSpan(0, 8).Clear();

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.HeaderSignatureMissing, ex.Message);
    }

    [Fact]
    public void Sector_size_exponent_below_seven_throws()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();
        PatchUInt16(bytes, 0x1E, 6); // 2^6 = 64 < minimum

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.SectorSizeTooSmall, ex.Message);
    }

    [Fact]
    public void Short_sector_size_greater_than_standard_throws()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();
        PatchUInt16(bytes, 0x20, 10); // short exponent 10 > standard exponent 9

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.ShortSectorSizeGreaterThanStandardSectorSize, ex.Message);
    }

    [Fact]
    public void Empty_sat_sec_id_chain_throws()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();
        PatchInt32(bytes, 0x2C, 0); // satSectorCount = 0

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.EmptySatSecIdChain, ex.Message);
    }

    [Fact]
    public void First_directory_entry_not_root_storage_throws()
    {
        var builder = new CdfBuilder().AddStream("A", "a");
        var bytes = builder.Build();
        bytes[builder.GetDirectoryEntryOffset(0) + 66] = 2; // change root entry type to Stream

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.FirstDirectoryEntryMustBeRootStorage, ex.Message);
    }

    [Fact]
    public void Directory_entry_name_too_long_throws()
    {
        var builder = new CdfBuilder().AddStream("A", "a");
        var bytes = builder.Build();
        PatchUInt16(bytes, builder.GetDirectoryEntryOffset(1) + 64, 66); // -> name length 64 > 62

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.DirectoryEntryNameTooLong, ex.Message);
    }

    [Fact]
    public void Invalid_sec_id_reference_in_directory_chain_throws()
    {
        var builder = new CdfBuilder().AddStream("A", "a");
        var bytes = builder.Build();
        PatchInt32(bytes, builder.GetSatEntryOffset(builder.DirectoryFirstSecId), 500); // next-sector points out of range

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.InvalidSecIdReference, ex.Message);
    }

    [Fact]
    public void Cyclic_sec_id_chain_in_directory_throws()
    {
        var builder = new CdfBuilder().AddStream("A", "a");
        var bytes = builder.Build();
        PatchInt32(bytes, builder.GetSatEntryOffset(builder.DirectoryFirstSecId), builder.DirectoryFirstSecId); // self-loop

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.CyclicSecIdChain, ex.Message);
    }

    [Fact]
    public void Cyclic_child_directory_entry_reference_throws()
    {
        var builder = new CdfBuilder().AddStream("A", "a").AddStream("B", "b");
        var bytes = builder.Build();
        PatchInt32(bytes, builder.GetDirectoryEntryOffset(2) + 68, 1); // B's left child -> A, forming a cycle

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes, rootStorageDescendantsOnly: true));
        Assert.Equal(Errors.CyclicChildDirectoryEntryReference, ex.Message);
    }

    [Fact]
    public void Referred_child_directory_entry_missing_throws()
    {
        var builder = new CdfBuilder().AddStream("A", "a");
        var bytes = builder.Build();
        PatchInt32(bytes, builder.GetDirectoryEntryOffset(1) + 68, 999); // A's left child -> nonexistent DID

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes, rootStorageDescendantsOnly: true));
        Assert.StartsWith("The referred child directory entry", ex.Message);
    }

    [Fact]
    public void Short_stream_without_container_throws()
    {
        var builder = new CdfBuilder().AddStream("Tiny", "tiny"); // short stream -> needs SSAT
        var bytes = builder.Build();

        // Remove the SSAT so the short stream has no container.
        PatchInt32(bytes, 0x3C, CdfBuilder.SecIdEndOfChain); // firstSsatSecId
        PatchInt32(bytes, 0x40, 0);                          // ssatSectorCount

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.NoShortStreamContainerStreamDefined, ex.Message);
    }

    [Fact]
    public void Zero_sized_short_stream_container_throws()
    {
        var builder = new CdfBuilder().AddStream("Tiny", "tiny"); // creates an SSAT + container
        var bytes = builder.Build();

        PatchInt32(bytes, builder.GetDirectoryEntryOffset(0) + 120, 0); // root storage stream size -> 0

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.ShortStreamContainerStreamSizeIsZero, ex.Message);
    }

    [Fact]
    public void Truncated_document_throws_unexpected_end_of_stream()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();

        var truncated = bytes[..CdfBuilder.HeaderSize]; // header only; SAT sector is gone

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(truncated));
        Assert.Equal(Errors.UnexpectedEndOfStream, ex.Message);
    }

    [Fact]
    public void Document_shorter_than_header_throws_unexpected_end_of_stream()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();

        // Keep the signature so it passes the first check, but cut off before the header is fully read.
        var truncated = bytes[..32];

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(truncated));
        Assert.Equal(Errors.UnexpectedEndOfStream, ex.Message);
    }

    [Fact]
    public void Sat_sector_address_out_of_range_throws_invalid_sec_id_reference()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").Build();

        // The first-part MSAT entry (0x4C) points at the SAT sector. Point it far out of range so that
        // reading the SAT sector indexes past the buffer, surfacing as an invalid SecId reference.
        PatchInt32(bytes, 0x4C, int.MaxValue);

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.InvalidSecIdReference, ex.Message);
    }

    [Fact]
    public void Ssat_sector_address_out_of_bounds_throws_unexpected_end_of_stream()
    {
        var builder = new CdfBuilder().AddStream("Tiny", "tiny"); // short stream -> a real SSAT exists
        var bytes = builder.Build();

        // Point the first SSAT SecId so far out that HEADER_SIZE + secId * sectorSize exceeds the buffer.
        PatchInt32(bytes, 0x3C, 100_000);

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.UnexpectedEndOfStream, ex.Message);
    }

    [Fact]
    public void Ssat_first_sec_id_beyond_sat_chain_throws_invalid_sec_id_reference()
    {
        // The SSAT's first SecId points at a sector that is physically readable but beyond the SAT chain's length,
        // so reading the SSAT sector succeeds yet the follow-up SAT-chain lookup is out of range.
        var bytes = CdfBuilder.BuildWithSsatPointerBeyondSatChain();

        var ex = Assert.Throws<CdfException>(() => Cdf.Open(bytes));
        Assert.Equal(Errors.InvalidSecIdReference, ex.Message);
    }

    [Fact]
    public void Stream_chain_terminates_early_throws_unexpected_end_of_stream()
    {
        // A multi-sector standard stream so ReadEntryStream loops more than once.
        var builder = new CdfBuilder().AddStream("Multi", new string('z', 5_000));
        var bytes = builder.Build();

        // Find the stream's first sector, then break the chain so the second iteration hits a free SecId.
        var firstStreamSecId = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(builder.GetDirectoryEntryOffset(1) + 116));
        PatchInt32(bytes, builder.GetSatEntryOffset(firstStreamSecId), CdfBuilder.SecIdFree);

        var entry = Cdf.Open(bytes).DirectoryEntries.Single(e => e.Name == "Multi");

        var ex = Assert.Throws<CdfException>(() => { var _ = entry.Stream; }); // stream content is read lazily
        Assert.Equal(Errors.UnexpectedEndOfStream, ex.Message);
    }
}
