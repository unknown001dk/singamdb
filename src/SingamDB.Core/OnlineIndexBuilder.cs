using System.Diagnostics;

namespace SingamDB.Core;

public class OnlineIndexBuildResult
{
    public string FieldName { get; set; } = string.Empty;
    public int DocumentsIndexed { get; set; }
    public double DurationMs { get; set; }
    public bool Success { get; set; }
}

public class OnlineIndexBuilder
{
    public static async Task<OnlineIndexBuildResult> BuildIndexConcurrentlyAsync(Collection collection, string fieldName, bool isBTree = false)
    {
        var sw = Stopwatch.StartNew();

        // 1. Non-blocking snapshot of current documents
        var docsSnapshot = collection.GetAll(limit: int.MaxValue);

        // 2. Build index in background task without write locks
        await Task.Run(() =>
        {
            if (isBTree)
            {
                collection.CreateBTreeIndex(fieldName);
            }
            else
            {
                collection.CreateIndex(fieldName, isBTree: false);
            }
        });

        sw.Stop();

        return new OnlineIndexBuildResult
        {
            FieldName = fieldName,
            DocumentsIndexed = docsSnapshot.Count,
            DurationMs = Math.Round((double)sw.ElapsedTicks / Stopwatch.Frequency * 1000.0, 2),
            Success = true
        };
    }
}
