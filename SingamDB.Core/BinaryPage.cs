using System.Text;
using System.Text.Json;

namespace SingamDB.Core;

public enum PageType : ushort
{
    Data = 1,
    Index = 2,
    Free = 3
}

public class BinaryPage
{
    public const int PageSize = 4096; // 4 KB Block Size
    public const int HeaderSize = 18; // 4 + 2 + 2 + 2 + 4 + 4

    public uint PageId { get; set; }
    public PageType Type { get; set; } = PageType.Data;
    public ushort SlotCount { get; private set; }
    public ushort FreeSpaceOffset { get; private set; } = (ushort)PageSize;
    public uint NextPageId { get; set; } = 0;
    public byte[] RawBuffer { get; } = new byte[PageSize];
    public bool IsDirty { get; set; }

    public BinaryPage(uint pageId)
    {
        PageId = pageId;
        WriteHeader();
    }

    public BinaryPage(byte[] sourceBytes)
    {
        Array.Copy(sourceBytes, RawBuffer, Math.Min(sourceBytes.Length, PageSize));
        ReadHeader();
    }

    private void WriteHeader()
    {
        BinaryPrimitivesWriter.WriteUInt32(RawBuffer, 0, PageId);
        BinaryPrimitivesWriter.WriteUInt16(RawBuffer, 4, (ushort)Type);
        BinaryPrimitivesWriter.WriteUInt16(RawBuffer, 6, SlotCount);
        BinaryPrimitivesWriter.WriteUInt16(RawBuffer, 8, FreeSpaceOffset);
        BinaryPrimitivesWriter.WriteUInt32(RawBuffer, 10, NextPageId);

        uint crc = FastCrc32.Compute(RawBuffer.AsSpan(0, 14).ToArray());
        BinaryPrimitivesWriter.WriteUInt32(RawBuffer, 14, crc);
    }

    private void ReadHeader()
    {
        PageId = BinaryPrimitivesWriter.ReadUInt32(RawBuffer, 0);
        Type = (PageType)BinaryPrimitivesWriter.ReadUInt16(RawBuffer, 4);
        SlotCount = BinaryPrimitivesWriter.ReadUInt16(RawBuffer, 6);
        FreeSpaceOffset = BinaryPrimitivesWriter.ReadUInt16(RawBuffer, 8);
        NextPageId = BinaryPrimitivesWriter.ReadUInt32(RawBuffer, 10);
    }

    public int GetAvailableFreeSpace()
    {
        int slotDirectoryEnd = HeaderSize + (SlotCount * 4);
        return FreeSpaceOffset - slotDirectoryEnd;
    }

    public bool TryInsertRecord(byte[] recordBytes, out ushort slotIndex)
    {
        slotIndex = 0;
        int neededSpace = recordBytes.Length + 4; // Data + 4-byte slot (Offset + Length)

        if (GetAvailableFreeSpace() < neededSpace)
        {
            return false;
        }

        // Allocate from bottom
        FreeSpaceOffset = (ushort)(FreeSpaceOffset - recordBytes.Length);
        Array.Copy(recordBytes, 0, RawBuffer, FreeSpaceOffset, recordBytes.Length);

        // Record slot in directory (Offset: 2 bytes, Length: 2 bytes)
        int slotPos = HeaderSize + (SlotCount * 4);
        BinaryPrimitivesWriter.WriteUInt16(RawBuffer, slotPos, FreeSpaceOffset);
        BinaryPrimitivesWriter.WriteUInt16(RawBuffer, slotPos + 2, (ushort)recordBytes.Length);

        slotIndex = SlotCount;
        SlotCount++;
        IsDirty = true;
        WriteHeader();
        return true;
    }

    public bool TryUpdateRecord(ushort slotIndex, byte[] newRecordBytes)
    {
        if (slotIndex >= SlotCount) return false;

        int slotPos = HeaderSize + (slotIndex * 4);
        ushort oldOffset = BinaryPrimitivesWriter.ReadUInt16(RawBuffer, slotPos);
        ushort oldLength = BinaryPrimitivesWriter.ReadUInt16(RawBuffer, slotPos + 2);

        // Case 1: In-place overwrite if same or smaller length
        if (newRecordBytes.Length <= oldLength)
        {
            Array.Copy(newRecordBytes, 0, RawBuffer, oldOffset, newRecordBytes.Length);
            BinaryPrimitivesWriter.WriteUInt16(RawBuffer, slotPos + 2, (ushort)newRecordBytes.Length);
            IsDirty = true;
            WriteHeader();
            return true;
        }

        // Case 2: Allocate new offset from free space
        if (GetAvailableFreeSpace() < newRecordBytes.Length)
        {
            // Try compacting first
            Compact();
            if (GetAvailableFreeSpace() < newRecordBytes.Length)
            {
                return false; // Page genuinely full
            }
        }

        FreeSpaceOffset = (ushort)(FreeSpaceOffset - newRecordBytes.Length);
        Array.Copy(newRecordBytes, 0, RawBuffer, FreeSpaceOffset, newRecordBytes.Length);
        BinaryPrimitivesWriter.WriteUInt16(RawBuffer, slotPos, FreeSpaceOffset);
        BinaryPrimitivesWriter.WriteUInt16(RawBuffer, slotPos + 2, (ushort)newRecordBytes.Length);

        IsDirty = true;
        WriteHeader();
        return true;
    }

    public void Compact()
    {
        var activeRecords = new List<(ushort slot, byte[] data)>();
        for (ushort s = 0; s < SlotCount; s++)
        {
            var r = GetRecord(s);
            if (r != null && r.Length > 0)
            {
                activeRecords.Add((s, r));
            }
        }

        // Reset free space to bottom
        FreeSpaceOffset = (ushort)PageSize;

        foreach (var (slot, data) in activeRecords)
        {
            FreeSpaceOffset = (ushort)(FreeSpaceOffset - data.Length);
            Array.Copy(data, 0, RawBuffer, FreeSpaceOffset, data.Length);

            int slotPos = HeaderSize + (slot * 4);
            BinaryPrimitivesWriter.WriteUInt16(RawBuffer, slotPos, FreeSpaceOffset);
            BinaryPrimitivesWriter.WriteUInt16(RawBuffer, slotPos + 2, (ushort)data.Length);
        }

        IsDirty = true;
        WriteHeader();
    }

    public byte[]? GetRecord(ushort slotIndex)
    {
        if (slotIndex >= SlotCount) return null;

        int slotPos = HeaderSize + (slotIndex * 4);
        ushort offset = BinaryPrimitivesWriter.ReadUInt16(RawBuffer, slotPos);
        ushort length = BinaryPrimitivesWriter.ReadUInt16(RawBuffer, slotPos + 2);

        if (offset == 0 || length == 0 || offset + length > PageSize) return null;

        var result = new byte[length];
        Array.Copy(RawBuffer, offset, result, 0, length);
        return result;
    }

    public List<byte[]> GetAllRecords()
    {
        var records = new List<byte[]>();
        for (ushort s = 0; s < SlotCount; s++)
        {
            var r = GetRecord(s);
            if (r != null && r.Length > 0)
            {
                records.Add(r);
            }
        }
        return records;
    }
}

public class SlottedPageManager : IDisposable
{
    private readonly string filePath;
    private readonly FileStream fileStream;
    private readonly Dictionary<uint, BinaryPage> pageCache = new();
    private readonly object lockObj = new();
    private uint totalPages = 0;

    public SlottedPageManager(string filePath)
    {
        this.filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        totalPages = (uint)(fileStream.Length / BinaryPage.PageSize);
    }

    public BinaryPage AllocateNewPage()
    {
        lock (lockObj)
        {
            uint newPageId = totalPages++;
            var page = new BinaryPage(newPageId) { IsDirty = true };
            pageCache[newPageId] = page;
            FlushPage(page);
            return page;
        }
    }

    public BinaryPage GetPage(uint pageId)
    {
        lock (lockObj)
        {
            if (pageCache.TryGetValue(pageId, out var cached))
            {
                return cached;
            }

            fileStream.Seek((long)pageId * BinaryPage.PageSize, SeekOrigin.Begin);
            var buffer = new byte[BinaryPage.PageSize];
            fileStream.ReadExactly(buffer, 0, BinaryPage.PageSize);

            var page = new BinaryPage(buffer);
            pageCache[pageId] = page;
            return page;
        }
    }

    public void FlushPage(BinaryPage page)
    {
        lock (lockObj)
        {
            fileStream.Seek((long)page.PageId * BinaryPage.PageSize, SeekOrigin.Begin);
            fileStream.Write(page.RawBuffer, 0, BinaryPage.PageSize);
            fileStream.Flush();
            page.IsDirty = false;
        }
    }

    public void FlushAll()
    {
        lock (lockObj)
        {
            foreach (var page in pageCache.Values.Where(p => p.IsDirty))
            {
                FlushPage(page);
            }
        }
    }

    public uint GetTotalPages() => totalPages;

    public void Dispose()
    {
        FlushAll();
        fileStream.Dispose();
    }
}

public static class BinaryPrimitivesWriter
{
    public static void WriteUInt32(byte[] buf, int offset, uint val)
    {
        buf[offset] = (byte)(val & 0xFF);
        buf[offset + 1] = (byte)((val >> 8) & 0xFF);
        buf[offset + 2] = (byte)((val >> 16) & 0xFF);
        buf[offset + 3] = (byte)((val >> 24) & 0xFF);
    }

    public static uint ReadUInt32(byte[] buf, int offset)
    {
        return (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));
    }

    public static void WriteUInt16(byte[] buf, int offset, ushort val)
    {
        buf[offset] = (byte)(val & 0xFF);
        buf[offset + 1] = (byte)((val >> 8) & 0xFF);
    }

    public static ushort ReadUInt16(byte[] buf, int offset)
    {
        return (ushort)(buf[offset] | (buf[offset + 1] << 8));
    }
}
