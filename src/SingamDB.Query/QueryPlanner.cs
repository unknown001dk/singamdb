using SingamDB.Core;

namespace SingamDB.Query;

/// <summary>
/// Volcano query planner and cost estimator for SingamDB.
/// </summary>
public class QueryPlanner
{
    public static QueryExecutionPlan Explain(Collection collection, Dictionary<string, object>? filter, int skip = 0, int limit = 0)
    {
        var plan = new QueryExecutionPlan
        {
            CollectionName = collection.Name,
            TotalDocuments = collection.Count(),
            EstimatedCost = 1.0,
            ExecutionStrategy = "CollectionScan"
        };

        if (filter != null && filter.Count > 0)
        {
            if (filter.ContainsKey("_id"))
            {
                plan.ExecutionStrategy = "PrimaryHashIndexScan";
                plan.IndexName = "_id";
                plan.EstimatedCost = 0.05;
            }
            else
            {
                plan.ExecutionStrategy = "FilterEvaluatorScan";
                plan.FilterPredicateCount = filter.Count;
                plan.EstimatedCost = plan.TotalDocuments * 0.1;
            }
        }

        plan.Skip = skip;
        plan.Limit = limit;
        return plan;
    }
}

public class QueryExecutionPlan
{
    public string CollectionName { get; set; } = string.Empty;
    public int TotalDocuments { get; set; }
    public string ExecutionStrategy { get; set; } = string.Empty;
    public string? IndexName { get; set; }
    public int FilterPredicateCount { get; set; }
    public int Skip { get; set; }
    public int Limit { get; set; }
    public double EstimatedCost { get; set; }
}
