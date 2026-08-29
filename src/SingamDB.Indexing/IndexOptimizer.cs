using SingamDB.Core;

namespace SingamDB.Indexing;

/// <summary>
/// Provides index optimization, coverage analysis, and index suggestion utilities.
/// </summary>
public class IndexOptimizer
{
    public static IndexRecommendation AnalyzeQueryPattern(string collectionName, IEnumerable<string> queriedFields, IEnumerable<string> existingIndexes)
    {
        var fieldList = queriedFields.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var indexSet = new HashSet<string>(existingIndexes, StringComparer.OrdinalIgnoreCase);

        var missingFields = fieldList.Where(f => !indexSet.Contains(f) && !f.Equals("_id", StringComparison.OrdinalIgnoreCase)).ToList();

        return new IndexRecommendation
        {
            CollectionName = collectionName,
            RecommendedFields = missingFields,
            IsCompositeRecommended = missingFields.Count > 1,
            RecommendedCompositeName = missingFields.Count > 1 ? string.Join("_", missingFields) : null,
            RecommendationReason = missingFields.Count > 0 
                ? $"Adding index on [{string.Join(", ", missingFields)}] will accelerate query scans."
                : "All queried fields are covered by existing indexes."
        };
    }
}

public class IndexRecommendation
{
    public string CollectionName { get; set; } = string.Empty;
    public List<string> RecommendedFields { get; set; } = new();
    public bool IsCompositeRecommended { get; set; }
    public string? RecommendedCompositeName { get; set; }
    public string RecommendationReason { get; set; } = string.Empty;
}
