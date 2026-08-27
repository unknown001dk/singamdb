using System.Diagnostics;
using SingamDB.Core;

Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine(@"
   ____  _                             ____  ____  
  / ___|(_)_ __   __ _  __ _ _ __ ___ |  _ \| __ ) 
  \___ \| | '_ \ / _` |/ _` | '_ ` _ \| | | |  _ \ 
   ___) | | | | | (_| | (_| | | | | | | |_| | |_) |
  |____/|_|_| |_|\__, |\__,_|_| |_| |_|____/|____/ 
                 |___/                             
  SingamDB Index Benchmark Suite (Full Scan vs Secondary Hash Index)
");
Console.ResetColor();

var ranks = new[] { "Constable", "HeadConstable", "SubInspector", "Inspector", "DSP", "ASP", "SP", "DIG", "IG", "ADGP", "DGP" };
var cities = new[] { "Chennai", "Coimbatore", "Madurai", "Tiruchirappalli", "Salem", "Tirunelveli", "Erode", "Vellore", "Thoothukudi", "Dindigul" };
var firstNames = new[] { "Durai", "Surya", "Vijay", "Ajith", "Vikram", "Karthi", "Siva", "Dhanush", "Simbu", "Kamal", "Rajini" };

var testSizes = new[] { 10_000, 100_000, 1_000_000 };
var results = new List<BenchmarkResult>();

foreach (var size in testSizes)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n========================================================");
    Console.WriteLine($" BENCHMARKING WITH {size:N0} DOCUMENTS");
    Console.WriteLine($"========================================================");
    Console.ResetColor();

    var collection = new Collection($"bench_{size}");
    var random = new Random(42);

    Console.Write($"[1/4] Populating {size:N0} records... ");
    var sw = Stopwatch.StartNew();

    // Generate documents in batches
    for (int i = 1; i <= size; i++)
    {
        var rank = (i % (size / 10 == 0 ? 1 : size / 10) == 0) ? "DCP" : ranks[random.Next(ranks.Length)];
        var doc = new Document(new Dictionary<string, object>
        {
            ["name"] = $"{firstNames[random.Next(firstNames.Length)]}_{i}",
            ["badge"] = $"TN-{100000 + i}",
            ["rank"] = rank,
            ["city"] = cities[random.Next(cities.Length)],
            ["age"] = 25 + (i % 35),
            ["active"] = (i % 2 == 0)
        });
        collection.Insert(doc);
    }
    sw.Stop();
    Console.WriteLine($"Done in {sw.ElapsedMilliseconds} ms.");

    // Measure 1: Full Scan (Without Index)
    Console.Write($"[2/4] Running Full Scan search (FIND rank='DCP')... ");
    
    // Warmup
    _ = collection.Scan("rank", "DCP");
    
    // Measure iterations
    int iterations = size >= 1_000_000 ? 5 : (size >= 100_000 ? 20 : 50);
    var scanWatch = Stopwatch.StartNew();
    int scanFoundCount = 0;
    for (int it = 0; it < iterations; it++)
    {
        var found = collection.Scan("rank", "DCP");
        scanFoundCount = found.Count;
    }
    scanWatch.Stop();
    double scanAvgMs = (double)scanWatch.ElapsedTicks / Stopwatch.Frequency * 1000.0 / iterations;
    Console.WriteLine($"Average: {scanAvgMs:F3} ms (Found {scanFoundCount} records)");

    // Measure 2: Create Index
    Console.Write($"[3/4] Building Secondary Index on 'rank'... ");
    var indexBuildWatch = Stopwatch.StartNew();
    collection.CreateIndex("rank");
    indexBuildWatch.Stop();
    double indexBuildMs = (double)indexBuildWatch.ElapsedTicks / Stopwatch.Frequency * 1000.0;
    Console.WriteLine($"Built in {indexBuildMs:F2} ms.");

    // Measure 3: Search with Index
    Console.Write($"[4/4] Running Indexed search (FIND rank='DCP')... ");
    
    // Warmup
    _ = collection.Find("rank", "DCP");

    int indexIterations = size >= 1_000_000 ? 100 : 500;
    var indexWatch = Stopwatch.StartNew();
    int indexFoundCount = 0;
    for (int it = 0; it < indexIterations; it++)
    {
        var found = collection.Find("rank", "DCP");
        indexFoundCount = found.Count;
    }
    indexWatch.Stop();
    double indexAvgMs = (double)indexWatch.ElapsedTicks / Stopwatch.Frequency * 1000.0 / indexIterations;
    Console.WriteLine($"Average: {indexAvgMs:F4} ms (Found {indexFoundCount} records)");

    double speedup = scanAvgMs / Math.Max(indexAvgMs, 0.0001);

    results.Add(new BenchmarkResult
    {
        DocumentCount = size,
        FullScanMs = scanAvgMs,
        IndexedMs = indexAvgMs,
        Speedup = speedup,
        IndexBuildMs = indexBuildMs,
        MatchedCount = indexFoundCount
    });

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($" Speedup with Index: {speedup:F1}x FASTER");
    Console.ResetColor();
}

// Final Summary Table
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("\n=========================================================================================");
Console.WriteLine("                         SINGAMDB BENCHMARK RESULTS TABLE                                ");
Console.WriteLine("=========================================================================================");
Console.ResetColor();
Console.WriteLine(string.Format("{0,-15} | {1,-18} | {2,-18} | {3,-15} | {4,-12}", "Documents", "Full Scan (No Index)", "With Index", "Speedup Factor", "Index Build"));
Console.WriteLine(new string('-', 89));

foreach (var r in results)
{
    string scanStr = r.FullScanMs >= 1000 ? $"{r.FullScanMs / 1000.0:F2} s" : $"{r.FullScanMs:F3} ms";
    string idxStr = r.IndexedMs < 0.01 ? $"{r.IndexedMs * 1000.0:F1} us ({r.IndexedMs:F4} ms)" : $"{r.IndexedMs:F3} ms";
    string speedupStr = $"{r.Speedup:F1}x faster";
    string buildStr = r.IndexBuildMs >= 1000 ? $"{r.IndexBuildMs / 1000.0:F2} s" : $"{r.IndexBuildMs:F1} ms";

    Console.WriteLine(string.Format("{0,-15:N0} | {1,-20} | {2,-18} | {3,-15} | {4,-12}",
        r.DocumentCount, scanStr, idxStr, speedupStr, buildStr));
}
Console.WriteLine(new string('-', 89));
Console.WriteLine();

public class BenchmarkResult
{
    public int DocumentCount { get; set; }
    public double FullScanMs { get; set; }
    public double IndexedMs { get; set; }
    public double Speedup { get; set; }
    public double IndexBuildMs { get; set; }
    public int MatchedCount { get; set; }
}
