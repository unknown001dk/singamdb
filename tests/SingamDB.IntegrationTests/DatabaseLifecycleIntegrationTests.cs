using SingamDB.Core;
using SingamDB.Indexing;
using SingamDB.Network;
using SingamDB.Storage;
using Xunit;

namespace SingamDB.IntegrationTests;

public class DatabaseLifecycleIntegrationTests : IDisposable
{
    private readonly string _testDataDir;

    public DatabaseLifecycleIntegrationTests()
    {
        _testDataDir = Path.Combine(Path.GetTempPath(), "SingamDB_Test_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDataDir))
            {
                Directory.Delete(_testDataDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void FullDatabaseLifecycle_Create_Insert_Query_Flush_Reload()
    {
        var engine = new DatabaseEngine(_testDataDir);
        var db = engine.GetOrCreateDatabase("production_db");
        var users = db.GetOrCreateCollection("users");

        users.CreateIndex("email", isBTree: true);

        var doc1 = new Document(new Dictionary<string, object>
        {
            { "email", "leo@singamdb.io" },
            { "tier", "enterprise" },
            { "score", 98 }
        });

        var doc2 = new Document(new Dictionary<string, object>
        {
            { "email", "dev@singamdb.io" },
            { "tier", "standard" },
            { "score", 75 }
        });

        users.Insert(doc1);
        users.Insert(doc2);

        Assert.Equal(2, users.Count());

        // Flush to slotted pages and WAL
        db.Flush();

        // Reload fresh engine from disk
        var reloadedEngine = new DatabaseEngine(_testDataDir);
        var reloadedDb = reloadedEngine.GetDatabase("production_db");
        Assert.NotNull(reloadedDb);

        var reloadedUsers = reloadedDb.GetCollection("users");
        Assert.NotNull(reloadedUsers);
        Assert.Equal(2, reloadedUsers.Count());

        var foundDoc = reloadedUsers.FindById(doc1.Id);
        Assert.NotNull(foundDoc);
        Assert.Equal("leo@singamdb.io", foundDoc.GetValue("email"));
    }

    [Fact]
    public void StorageSegmentManager_GeneratesAccurateDiagnostics()
    {
        var storage = new StorageEngine(_testDataDir);
        var coll = new Collection("diagnostics_coll");
        coll.Insert(new Document(new Dictionary<string, object> { { "item", "A" } }));
        storage.SaveCollection("testdb", coll);

        var segMgr = new StorageSegmentManager(storage, _testDataDir);
        var diag = segMgr.GetDiagnostics("testdb", "diagnostics_coll");

        Assert.Equal("testdb", diag.DatabaseName);
        Assert.Equal("diagnostics_coll", diag.CollectionName);
        Assert.True(diag.PageCount >= 0);
    }
}
