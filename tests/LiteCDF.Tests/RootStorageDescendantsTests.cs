namespace LiteCDF.Tests;

using Infrastructure;

using System;
using System.Buffers.Binary;
using System.Linq;

using Xunit;

public class RootStorageDescendantsTests
{
    private static void PatchInt32(byte[] data, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);

    [Fact]
    public void RootStorageDescendantsOnly_excludes_the_root_and_empty_padding_entries()
    {
        var bytes = new CdfBuilder()
                    .AddStream("First", "1")
                    .AddStream("Second", "2")
                    .Build();

        var document = Cdf.Open(bytes, rootStorageDescendantsOnly: true);

        Assert.Equal(["First", "Second"], document.DirectoryEntries.Select(e => e.Name));
        Assert.All(document.DirectoryEntries, e => Assert.True(e.IsRootStorageDescendant));
        Assert.DoesNotContain(document.DirectoryEntries, e => e.Name == "Root Entry");
    }

    [Fact]
    public void RootStorageDescendantsOnly_preserves_member_traversal_order()
    {
        var bytes = new CdfBuilder()
                    .AddStream("M1", "a")
                    .AddStream("M2", "b")
                    .AddStream("M3", "c")
                    .AddStream("M4", "d")
                    .Build();

        var document = Cdf.Open(bytes, rootStorageDescendantsOnly: true);

        Assert.Equal(["M1", "M2", "M3", "M4"], document.DirectoryEntries.Select(e => e.Name));
    }

    [Fact]
    public void RootStorageDescendantsOnly_still_allows_reading_stream_content()
    {
        var bytes = new CdfBuilder()
                    .AddStream("Big", new string('x', 5000))
                    .AddStream("Tiny", "tiny")
                    .Build();

        var document = Cdf.Open(bytes, rootStorageDescendantsOnly: true);

        Assert.Equal(5000, document.DirectoryEntries.Single(e => e.Name == "Big").Stream.Length);
        Assert.Equal(4, document.DirectoryEntries.Single(e => e.Name == "Tiny").Stream.Length);
    }

    [Fact]
    public void RootStorageDescendantsOnly_includes_storage_members()
    {
        var bytes = new CdfBuilder()
                    .AddStream("Stream", "s")
                    .AddStorage("Storage")
                    .Build();

        var document = Cdf.Open(bytes, rootStorageDescendantsOnly: true);

        Assert.Contains(document.DirectoryEntries, e => e.Name == "Storage" && e.Type == CompoundDocument.DirectoryEntry.EntryType.Storage);
    }

    [Fact]
    public void RootStorageDescendantsOnly_visits_entries_reached_via_a_right_child_link()
    {
        // The builder emits a left-leaning chain (A.left -> B). Rewire it so B is reached via A's right child
        // instead, exercising the right-child traversal branch in VisitEntries.
        var builder = new CdfBuilder().AddStream("A", "a").AddStream("B", "b");
        var bytes = builder.Build();

        PatchInt32(bytes, builder.DirectoryEntryOffset(1) + 68, CdfBuilder.SecIdFree); // A's left child -> none
        PatchInt32(bytes, builder.DirectoryEntryOffset(1) + 72, 2);                    // A's right child -> B (DID 2)

        var document = Cdf.Open(bytes, rootStorageDescendantsOnly: true);

        Assert.Equal(["A", "B"], document.DirectoryEntries.Select(e => e.Name));
        Assert.All(document.DirectoryEntries, e => Assert.True(e.IsRootStorageDescendant));
    }

    [Fact]
    public void OpenAndReadStream_with_rootStorageDescendantsOnly_returns_the_matching_stream()
    {
        var bytes = new CdfBuilder()
                    .AddStream("Other", "x")
                    .AddStream("Target", "payload")
                    .Build();

        var result = Cdf.OpenAndReadStream(bytes, n => n == "Target", rootStorageDescendantOnly: true);

        Assert.Equal("payload", System.Text.Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void OpenAndReadStream_with_rootStorageDescendantsOnly_returns_null_when_nothing_matches()
    {
        var bytes = new CdfBuilder().AddStream("A", "a").AddStream("B", "b").Build();

        var result = Cdf.OpenAndReadStream(bytes, n => n == "Missing", rootStorageDescendantOnly: true);

        Assert.Null(result);
    }

    [Fact]
    public void OpenAndReadMultipleStreams_with_rootStorageDescendantsOnly_returns_all_matches()
    {
        var bytes = new CdfBuilder()
                    .AddStream("Doc_1", "one")
                    .AddStream("Skip", "no")
                    .AddStream("Doc_2", "two")
                    .Build();

        var result = Cdf.OpenAndReadMultipleStreams(bytes, n => n != null && n.StartsWith("Doc_"), rootStorageDescendantsOnly: true);

        Assert.Equal(2, result.Count);
        Assert.Equal("one", System.Text.Encoding.UTF8.GetString(result["Doc_1"]));
        Assert.Equal("two", System.Text.Encoding.UTF8.GetString(result["Doc_2"]));
        Assert.False(result.ContainsKey("Skip"));
    }
}
