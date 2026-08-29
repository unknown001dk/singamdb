using SingamDB.Core;
using SingamDB.Query;
using Xunit;

namespace SingamDB.Query.Tests;

public class VolcanoQueryEngineTests
{
    [Fact]
    public void Collection_FilterPredicate_MatchesCorrectly()
    {
        var coll = new Collection("test_query_coll");

        coll.Insert(new Document(new Dictionary<string, object>
        {
            { "name", "Alice" },
            { "age", 25 },
            { "role", "Engineer" }
        }));

        coll.Insert(new Document(new Dictionary<string, object>
        {
            { "name", "Bob" },
            { "age", 35 },
            { "role", "Manager" }
        }));

        coll.Insert(new Document(new Dictionary<string, object>
        {
            { "name", "Charlie" },
            { "age", 30 },
            { "role", "Engineer" }
        }));

        // Filter: age > 28
        var filter = new Dictionary<string, object>
        {
            { "age", new Dictionary<string, object> { { "$gt", 28 } } }
        };

        var results = coll.Query(filter);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Collection_InFilter_ReturnsExpectedDocuments()
    {
        var coll = new Collection("test_in_coll");

        coll.Insert(new Document(new Dictionary<string, object> { { "city", "Tokyo" } }));
        coll.Insert(new Document(new Dictionary<string, object> { { "city", "Chennai" } }));
        coll.Insert(new Document(new Dictionary<string, object> { { "city", "London" } }));

        var filter = new Dictionary<string, object>
        {
            { "city", new Dictionary<string, object> { { "$in", new List<object> { "Tokyo", "London" } } } }
        };

        var results = coll.Query(filter);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void QueryPlanner_Explain_ReturnsExecutionPlan()
    {
        var coll = new Collection("explain_coll");
        var filter = new Dictionary<string, object> { { "_id", "123" } };

        var plan = QueryPlanner.Explain(coll, filter);

        Assert.Equal("explain_coll", plan.CollectionName);
        Assert.Equal("PrimaryHashIndexScan", plan.ExecutionStrategy);
        Assert.Equal("_id", plan.IndexName);
    }
}
