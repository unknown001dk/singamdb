using SingamDB.Core;
using Xunit;

namespace SingamDB.Core.Tests;

public class DocumentAndTransactionTests
{
    [Fact]
    public void Document_Initialization_SetsDefaultValues()
    {
        var doc = new Document();

        Assert.False(string.IsNullOrWhiteSpace(doc.Id));
        Assert.NotNull(doc.Data);
        Assert.True(doc.CreatedAt <= DateTime.UtcNow);
        Assert.True(doc.UpdatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Document_GetValue_ReturnsCorrectProperties()
    {
        var data = new Dictionary<string, object>
        {
            { "name", "SingamDB" },
            { "version", 3.0 },
            { "isActive", true }
        };

        var doc = new Document(data, "custom-id-123");

        Assert.Equal("custom-id-123", doc.GetValue("_id"));
        Assert.Equal("SingamDB", doc.GetValue("name"));
        Assert.Equal(3.0, (double)doc.GetValue("version")!);
        Assert.Equal(true, doc.GetValue("isActive"));
    }

    [Fact]
    public void TransactionManager_BeginAndRollback_WorksCorrectly()
    {
        var txManager = new TransactionManager();
        var tx = txManager.BeginTransaction();

        Assert.NotNull(tx);
        Assert.True(tx.TxId > 0);
        Assert.Equal(TransactionStatus.Active, tx.Status);

        txManager.Rollback(tx);
        Assert.Equal(TransactionStatus.Aborted, tx.Status);
    }

    [Fact]
    public async Task LockManager_AcquireAndReleaseAsync_Success()
    {
        var lockManager = new LockManager();
        bool acquired = await lockManager.AcquireLockAsync(101, "resource_1", LockMode.Exclusive, TimeSpan.FromSeconds(1));
        Assert.True(acquired);

        lockManager.ReleaseLocks(101);
    }
}
