using SingamDB.Core;
using SingamDB.Query;
using SingamDB.Indexing;

Console.WriteLine("=== SingamDB Advanced Query & Indexing Example ===");

var engine = new DatabaseEngine("advanced_query_data");
var db = engine.GetOrCreateDatabase("analytics_db");
var events = db.GetOrCreateCollection("telemetry");

// Create Composite Index on Category and Severity
events.CreateCompositeIndex(new List<string> { "category", "severity" });

// Populate sample data
for (int i = 1; i <= 20; i++)
{
    events.Insert(new Document(new Dictionary<string, object>
    {
        { "category", i % 2 == 0 ? "NETWORK" : "SYSTEM" },
        { "severity", i % 3 == 0 ? "HIGH" : "INFO" },
        { "latencyMs", i * 15 },
        { "timestamp", DateTime.UtcNow.AddMinutes(-i) }
    }));
}

// Complex query filter: category == NETWORK AND severity == INFO
var filter = new Dictionary<string, object>
{
    { "category", "NETWORK" },
    { "severity", "INFO" }
};

var results = events.Query(filter);
Console.WriteLine($"Found {results.Count} matching events for [category: NETWORK, severity: INFO]:");
foreach (var doc in results)
{
    Console.WriteLine($" - ID: {doc.Id} | Latency: {doc.GetValue("latencyMs")}ms");
}

// Generate Explain Plan
var plan = QueryPlanner.Explain(events, filter);
Console.WriteLine($"\n[Explain Plan] Strategy: {plan.ExecutionStrategy}, Estimated Cost: {plan.EstimatedCost}");
