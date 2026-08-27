using System.Collections.Concurrent;
using System.Diagnostics;

namespace SingamDB.Core;

public class Frame
{
    public uint FrameId { get; }
    public uint PageId { get; set; }
    public BinaryPage? Page { get; set; }
    public int PinCount { get; set; }
    public bool IsDirty { get; set; }
    public long LastAccessTimestamp { get; set; }

    public Frame(uint frameId)
    {
        FrameId = frameId;
    }
}

public class BufferPoolManager : IDisposable
{
    private readonly SlottedPageManager diskManager;
    private readonly int poolSize;
    private readonly Frame[] frames;
    private readonly ConcurrentDictionary<uint, uint> pageTable = new(); // PageId -> FrameId
    private readonly object poolLock = new();

    public long CacheHits { get; private set; }
    public long CacheMisses { get; private set; }

    public BufferPoolManager(SlottedPageManager diskManager, int poolSize = 256)
    {
        this.diskManager = diskManager;
        this.poolSize = poolSize;
        frames = new Frame[poolSize];
        for (uint i = 0; i < poolSize; i++)
        {
            frames[i] = new Frame(i);
        }
    }

    public BinaryPage FetchPage(uint pageId)
    {
        lock (poolLock)
        {
            // 1. Check if Page is in Buffer Pool (Cache Hit)
            if (pageTable.TryGetValue(pageId, out uint frameId))
            {
                var frame = frames[frameId];
                frame.PinCount++;
                frame.LastAccessTimestamp = Stopwatch.GetTimestamp();
                CacheHits++;
                return frame.Page!;
            }

            // 2. Cache Miss: Find an unpinned frame using LRU eviction
            CacheMisses++;
            uint victimFrameId = SelectVictimFrame();
            var victimFrame = frames[victimFrameId];

            // If victim frame has a dirty page, write back to disk
            if (victimFrame.Page != null)
            {
                if (victimFrame.IsDirty || victimFrame.Page.IsDirty)
                {
                    diskManager.FlushPage(victimFrame.Page);
                }
                pageTable.TryRemove(victimFrame.PageId, out _);
            }

            // Read page from disk into selected frame
            var page = diskManager.GetPage(pageId);
            victimFrame.Page = page;
            victimFrame.PageId = pageId;
            victimFrame.PinCount = 1;
            victimFrame.IsDirty = false;
            victimFrame.LastAccessTimestamp = Stopwatch.GetTimestamp();

            pageTable[pageId] = victimFrameId;
            return page;
        }
    }

    public BinaryPage NewPage()
    {
        lock (poolLock)
        {
            var page = diskManager.AllocateNewPage();
            uint victimFrameId = SelectVictimFrame();
            var victimFrame = frames[victimFrameId];

            if (victimFrame.Page != null)
            {
                if (victimFrame.IsDirty || victimFrame.Page.IsDirty)
                {
                    diskManager.FlushPage(victimFrame.Page);
                }
                pageTable.TryRemove(victimFrame.PageId, out _);
            }

            victimFrame.Page = page;
            victimFrame.PageId = page.PageId;
            victimFrame.PinCount = 1;
            victimFrame.IsDirty = true;
            victimFrame.LastAccessTimestamp = Stopwatch.GetTimestamp();

            pageTable[page.PageId] = victimFrameId;
            return page;
        }
    }

    public void UnpinPage(uint pageId, bool isDirty)
    {
        lock (poolLock)
        {
            if (pageTable.TryGetValue(pageId, out uint frameId))
            {
                var frame = frames[frameId];
                if (frame.PinCount > 0)
                {
                    frame.PinCount--;
                }
                if (isDirty)
                {
                    frame.IsDirty = true;
                    if (frame.Page != null) frame.Page.IsDirty = true;
                }
            }
        }
    }

    public void FlushAllPages()
    {
        lock (poolLock)
        {
            foreach (var frame in frames)
            {
                if (frame.Page != null && (frame.IsDirty || frame.Page.IsDirty))
                {
                    diskManager.FlushPage(frame.Page);
                    frame.IsDirty = false;
                }
            }
            diskManager.FlushAll();
        }
    }

    private uint SelectVictimFrame()
    {
        // LRU replacement policy among frames with PinCount == 0
        long oldestTime = long.MaxValue;
        uint victim = uint.MaxValue;

        for (uint i = 0; i < poolSize; i++)
        {
            var frame = frames[i];
            if (frame.Page == null) // Free frame
            {
                return i;
            }

            if (frame.PinCount == 0 && frame.LastAccessTimestamp < oldestTime)
            {
                oldestTime = frame.LastAccessTimestamp;
                victim = i;
            }
        }

        if (victim == uint.MaxValue)
        {
            // All pages pinned; fallback to frame 0
            victim = 0;
        }

        return victim;
    }

    public void Dispose()
    {
        FlushAllPages();
        diskManager.Dispose();
    }
}
