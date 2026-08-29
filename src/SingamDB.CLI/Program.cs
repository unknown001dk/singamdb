using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
{
    Console.WriteLine("SingamDB Interactive CLI Client");
    Console.WriteLine("Usage:");
    Console.WriteLine("  singam [server-url]");
    Console.WriteLine("  singam http://localhost:7777");
    Console.WriteLine("  singam --help | -h");
    Console.WriteLine("  singam --version | -v");
    return;
}

if (args.Length > 0 && (args[0] == "--version" || args[0] == "-v"))
{
    Console.WriteLine("SingamDB CLI v3.0.0");
    return;
}

var serverUrl = args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out _) ? args[0] : "http://localhost:7777";
var currentDb = "default";
string? currentCollection = null;

using var client = new HttpClient();
client.BaseAddress = new Uri(serverUrl);

PrintCliHeader();

// Test connection
try
{
    var health = await client.GetAsync("/health");
    if (health.IsSuccessStatusCode)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK] Connected to SingamDB at {serverUrl}");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[!] SingamDB responded with status: {health.StatusCode}");
        Console.ResetColor();
    }
}
catch
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[ERROR] Could not connect to SingamDB at {serverUrl}.");
    Console.WriteLine("    Make sure the server is running (`dotnet run --project SingamDB.Server`).\n");
    Console.ResetColor();
}

Console.WriteLine("Type 'help' for command list, 'exit' to quit.\n");

while (true)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    string promptLocation = string.IsNullOrEmpty(currentCollection) ? currentDb : $"{currentDb}.{currentCollection}";
    Console.Write($"singam [{promptLocation}]> ");
    Console.ResetColor();

    var input = Console.ReadLine();
    if (input == null) break;

    var line = input.Trim();
    if (string.IsNullOrEmpty(line)) continue;

    var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var cmd = parts[0].ToUpperInvariant();
    var param = parts.Length > 1 ? parts[1].Trim() : string.Empty;

    try
    {
        switch (cmd)
        {
            case "EXIT":
            case "QUIT":
                Console.WriteLine("Bye! SingamDB Shell closed.");
                return;

            case "CLEAR":
            case "CLS":
                Console.Clear();
                PrintCliHeader();
                break;

            case "HELP":
                PrintHelp();
                break;

            case "SHOW":
                if (param.Equals("DBS", StringComparison.OrdinalIgnoreCase) || param.Equals("DATABASES", StringComparison.OrdinalIgnoreCase))
                {
                    var dbs = await client.GetFromJsonAsync<List<string>>("/api/databases");
                    Console.WriteLine("\nDatabases:");
                    foreach (var db in dbs ?? new())
                    {
                        Console.WriteLine($"  * {db} {(db.Equals(currentDb, StringComparison.OrdinalIgnoreCase) ? "(current)" : "")}");
                    }
                    Console.WriteLine();
                }
                else if (param.Equals("COLLECTIONS", StringComparison.OrdinalIgnoreCase) || param.Equals("COLLS", StringComparison.OrdinalIgnoreCase))
                {
                    var colls = await client.GetFromJsonAsync<List<string>>($"/api/databases/{currentDb}/collections");
                    Console.WriteLine($"\nCollections in '{currentDb}':");
                    if (colls == null || colls.Count == 0)
                    {
                        Console.WriteLine("  (No collections exist in this database. Use 'COLL <name>' to select/create one.)");
                    }
                    else
                    {
                        foreach (var c in colls)
                        {
                            Console.WriteLine($"  * {c} {(c.Equals(currentCollection, StringComparison.OrdinalIgnoreCase) ? "(active)" : "")}");
                        }
                    }
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine("Usage: SHOW DBS or SHOW COLLECTIONS");
                }
                break;

            case "USE":
                if (string.IsNullOrWhiteSpace(param))
                {
                    Console.WriteLine("Usage: USE <database_name>");
                }
                else
                {
                    currentDb = param;
                    currentCollection = null;
                    await client.PostAsync($"/api/databases/{currentDb}", null);
                    Console.WriteLine($"Switched to database: '{currentDb}'");
                }
                break;

            case "COLL":
            case "COLLECTION":
                if (string.IsNullOrWhiteSpace(param))
                {
                    Console.WriteLine("Usage: COLL <collection_name>");
                }
                else
                {
                    currentCollection = param;
                    await client.PostAsync($"/api/databases/{currentDb}/collections/{currentCollection}", null);
                    Console.WriteLine($"Active collection set to: '{currentCollection}'");
                }
                break;

            case "INSERT":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                if (string.IsNullOrWhiteSpace(param))
                {
                    Console.WriteLine("Usage: INSERT {\"name\": \"Alice\", \"age\": 30}");
                    break;
                }

                var docData = JsonSerializer.Deserialize<Dictionary<string, object>>(param);
                if (docData == null)
                {
                    Console.WriteLine("Invalid JSON object.");
                    break;
                }

                var insertResp = await client.PostAsJsonAsync($"/api/databases/{currentDb}/collections/{currentCollection}/documents", docData);
                if (insertResp.IsSuccessStatusCode)
                {
                    var created = await insertResp.Content.ReadAsStringAsync();
                    PrintPrettyJson(created);
                }
                else
                {
                    Console.WriteLine($"Error: {await insertResp.Content.ReadAsStringAsync()}");
                }
                break;

            case "BATCH":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                if (string.IsNullOrWhiteSpace(param))
                {
                    Console.WriteLine("Usage: BATCH [{\"name\":\"A\"}, {\"name\":\"B\"}]");
                    break;
                }

                var batchDocs = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(param);
                if (batchDocs == null)
                {
                    Console.WriteLine("Invalid JSON array.");
                    break;
                }

                var batchResp = await client.PostAsJsonAsync($"/api/databases/{currentDb}/collections/{currentCollection}/batch", batchDocs);
                if (batchResp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[OK] Batch inserted {batchDocs.Count} documents successfully.");
                }
                else
                {
                    Console.WriteLine($"Batch error: {await batchResp.Content.ReadAsStringAsync()}");
                }
                break;

            case "FIND":
            case "ALL":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                await HandleFindQueryAsync(client, currentDb, currentCollection, param, isExplain: false);
                break;

            case "EXPLAIN":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                string explainParam = param.StartsWith("FIND ", StringComparison.OrdinalIgnoreCase) ? param.Substring(5).Trim() : param;
                await HandleFindQueryAsync(client, currentDb, currentCollection, explainParam, isExplain: true);
                break;

            case "AGGREGATE":
            case "AGG":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                if (string.IsNullOrWhiteSpace(param))
                {
                    Console.WriteLine("Usage: AGGREGATE {\"groupBy\": \"city\", \"avg\": \"salary\", \"count\": true}");
                    break;
                }

                var aggPayload = JsonSerializer.Deserialize<Dictionary<string, object>>(param);
                var aggResp = await client.PostAsJsonAsync($"/api/databases/{currentDb}/collections/{currentCollection}/aggregate", aggPayload);
                if (aggResp.IsSuccessStatusCode)
                {
                    PrintPrettyJson(await aggResp.Content.ReadAsStringAsync());
                }
                else
                {
                    Console.WriteLine($"Aggregate error: {await aggResp.Content.ReadAsStringAsync()}");
                }
                break;

            case "GET":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                if (string.IsNullOrWhiteSpace(param))
                {
                    Console.WriteLine("Usage: GET <document_id>");
                    break;
                }

                var getResp = await client.GetAsync($"/api/databases/{currentDb}/collections/{currentCollection}/documents/{param}");
                if (getResp.IsSuccessStatusCode)
                {
                    var doc = await getResp.Content.ReadAsStringAsync();
                    PrintPrettyJson(doc);
                }
                else
                {
                    Console.WriteLine($"Document '{param}' not found.");
                }
                break;

            case "UPDATE":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                var updateParts = param.Split(' ', 2);
                if (updateParts.Length < 2)
                {
                    Console.WriteLine("Usage: UPDATE <document_id> {\"field\": \"newValue\"}");
                    break;
                }

                var targetId = updateParts[0];
                var updatePayload = JsonSerializer.Deserialize<Dictionary<string, object>>(updateParts[1]);
                var updResp = await client.PutAsJsonAsync($"/api/databases/{currentDb}/collections/{currentCollection}/documents/{targetId}", updatePayload);
                if (updResp.IsSuccessStatusCode)
                {
                    PrintPrettyJson(await updResp.Content.ReadAsStringAsync());
                }
                else
                {
                    Console.WriteLine($"Update error: {await updResp.Content.ReadAsStringAsync()}");
                }
                break;

            case "DELETE":
            case "DEL":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                if (string.IsNullOrWhiteSpace(param))
                {
                    Console.WriteLine("Usage: DELETE <document_id>");
                    break;
                }

                var delResp = await client.DeleteAsync($"/api/databases/{currentDb}/collections/{currentCollection}/documents/{param}");
                if (delResp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[OK] Document '{param}' deleted successfully.");
                }
                else
                {
                    Console.WriteLine($"Delete error: {await delResp.Content.ReadAsStringAsync()}");
                }
                break;

            case "INDEX":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                if (string.IsNullOrWhiteSpace(param))
                {
                    Console.WriteLine("Usage: INDEX <field_name> [btree] OR INDEX <f1,f2> (Composite)");
                    break;
                }

                if (param.Contains(','))
                {
                    var fields = param.Split(',', StringSplitOptions.TrimEntries);
                    var compResp = await client.PostAsJsonAsync($"/api/databases/{currentDb}/collections/{currentCollection}/indexes/composite", new { fields });
                    if (compResp.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[OK] Composite index created on ({string.Join(", ", fields)}).");
                    }
                    else
                    {
                        Console.WriteLine($"Index error: {await compResp.Content.ReadAsStringAsync()}");
                    }
                }
                else
                {
                    bool isBTree = param.EndsWith(" btree", StringComparison.OrdinalIgnoreCase);
                    string fieldName = isBTree ? param.Substring(0, param.Length - 6).Trim() : param;

                    var idxResp = await client.PostAsJsonAsync($"/api/databases/{currentDb}/collections/{currentCollection}/indexes?isBTree={isBTree}", new { field = fieldName });
                    if (idxResp.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[OK] {(isBTree ? "B-Tree Range" : "Secondary Hash")} index created on '{fieldName}'.");
                    }
                    else
                    {
                        Console.WriteLine($"Index error: {await idxResp.Content.ReadAsStringAsync()}");
                    }
                }
                break;

            case "DROP":
                var dropParts = param.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (dropParts.Length < 2)
                {
                    Console.WriteLine("Usage: DROP DB <db_name> OR DROP COLL <coll_name> OR DROP INDEX <field_name>");
                    break;
                }

                string dropType = dropParts[0].ToUpperInvariant();
                string dropTarget = dropParts[1].Trim();

                if (dropType == "DB" || dropType == "DATABASE")
                {
                    var r = await client.DeleteAsync($"/api/databases/{dropTarget}");
                    Console.WriteLine(r.IsSuccessStatusCode ? $"[OK] Database '{dropTarget}' dropped." : $"Error: {await r.Content.ReadAsStringAsync()}");
                }
                else if (dropType == "COLL" || dropType == "COLLECTION")
                {
                    var r = await client.DeleteAsync($"/api/databases/{currentDb}/collections/{dropTarget}");
                    Console.WriteLine(r.IsSuccessStatusCode ? $"[OK] Collection '{dropTarget}' dropped." : $"Error: {await r.Content.ReadAsStringAsync()}");
                    if (currentCollection == dropTarget) currentCollection = null;
                }
                else if (dropType == "INDEX")
                {
                    if (string.IsNullOrWhiteSpace(currentCollection))
                    {
                        Console.WriteLine("[!] No active collection.");
                        break;
                    }
                    var r = await client.DeleteAsync($"/api/databases/{currentDb}/collections/{currentCollection}/indexes/{dropTarget}");
                    Console.WriteLine(r.IsSuccessStatusCode ? $"[OK] Index on '{dropTarget}' dropped." : $"Error: {await r.Content.ReadAsStringAsync()}");
                }
                break;

            case "CHECKPOINT":
            case "FLUSH":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                var ckptResp = await client.PostAsync($"/api/databases/{currentDb}/collections/{currentCollection}/checkpoint", null);
                if (ckptResp.IsSuccessStatusCode)
                {
                    PrintPrettyJson(await ckptResp.Content.ReadAsStringAsync());
                }
                else
                {
                    Console.WriteLine($"Checkpoint error: {await ckptResp.Content.ReadAsStringAsync()}");
                }
                break;

            case "STATS":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                var statsResp = await client.GetAsync($"/api/databases/{currentDb}/collections/{currentCollection}/stats");
                if (statsResp.IsSuccessStatusCode)
                {
                    PrintPrettyJson(await statsResp.Content.ReadAsStringAsync());
                }
                else
                {
                    Console.WriteLine($"Error: {await statsResp.Content.ReadAsStringAsync()}");
                }
                break;

            default:
                Console.WriteLine($"Unknown command: '{cmd}'. Type 'help' for available commands.");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Command failed: {ex.Message}");
        Console.ResetColor();
    }
}

static async Task HandleFindQueryAsync(HttpClient client, string db, string coll, string inputParams, bool isExplain)
{
    string jsonFilter = "{}";
    string? sort = null;
    bool asc = true;
    string? project = null;
    int limit = 100;
    int skip = 0;

    if (!string.IsNullOrWhiteSpace(inputParams) && !inputParams.Equals("ALL", StringComparison.OrdinalIgnoreCase))
    {
        // Parse SQL/Fluent CLI clauses: SORT <field> [asc|desc], PROJECT <f1,f2>, LIMIT <n>, SKIP <n>
        var remaining = inputParams;

        // LIMIT
        var limitMatch = Regex.Match(remaining, @"\bLIMIT\s+(\d+)", RegexOptions.IgnoreCase);
        if (limitMatch.Success)
        {
            limit = int.Parse(limitMatch.Groups[1].Value);
            remaining = remaining.Remove(limitMatch.Index, limitMatch.Length).Trim();
        }

        // SKIP
        var skipMatch = Regex.Match(remaining, @"\bSKIP\s+(\d+)", RegexOptions.IgnoreCase);
        if (skipMatch.Success)
        {
            skip = int.Parse(skipMatch.Groups[1].Value);
            remaining = remaining.Remove(skipMatch.Index, skipMatch.Length).Trim();
        }

        // PROJECT
        var projMatch = Regex.Match(remaining, @"\bPROJECT\s+([a-zA-Z0-9_, ]+)", RegexOptions.IgnoreCase);
        if (projMatch.Success)
        {
            project = projMatch.Groups[1].Value.Replace(" ", "");
            remaining = remaining.Remove(projMatch.Index, projMatch.Length).Trim();
        }

        // SORT
        var sortMatch = Regex.Match(remaining, @"\bSORT\s+([a-zA-Z0-9_]+)(\s+(ASC|DESC))?", RegexOptions.IgnoreCase);
        if (sortMatch.Success)
        {
            sort = sortMatch.Groups[1].Value;
            if (sortMatch.Groups[3].Success && sortMatch.Groups[3].Value.Equals("DESC", StringComparison.OrdinalIgnoreCase))
            {
                asc = false;
            }
            remaining = remaining.Remove(sortMatch.Index, sortMatch.Length).Trim();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            jsonFilter = remaining;
        }
    }

    var filterObj = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonFilter) ?? new();

    string url = isExplain
        ? $"/api/databases/{db}/collections/{coll}/explain?sort={sort}&project={project}&limit={limit}&skip={skip}"
        : $"/api/databases/{db}/collections/{coll}/query?sort={sort}&asc={asc}&project={project}&limit={limit}&skip={skip}";

    var resp = await client.PostAsJsonAsync(url, filterObj);
    if (resp.IsSuccessStatusCode)
    {
        PrintPrettyJson(await resp.Content.ReadAsStringAsync());
    }
    else
    {
        Console.WriteLine($"Query error: {await resp.Content.ReadAsStringAsync()}");
    }
}

static void PrintCliHeader()
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(@"
   ____  _                             ____  ____  
  / ___|(_)_ __   __ _  __ _ _ __ ___ |  _ \| __ ) 
  \___ \| | '_ \ / _` |/ _` | '_ ` _ \| | | |  _ \ 
   ___) | | | | | (_| | (_| | | | | | | |_| | |_) |
  |____/|_|_| |_|\__, |\__,_|_| |_| |_|____/|____/ 
                 |___/                             
");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  SingamDB Full-Feature Interactive Shell v3.0.0");
    Console.WriteLine("===================================================\n");
    Console.ResetColor();
}

static void PrintHelp()
{
    Console.WriteLine(@"
Available Commands:
-------------------------------------------------------------------------------------
  SHOW DBS                                List all databases
  USE <db_name>                           Switch active database (e.g. USE production)
  SHOW COLLECTIONS                        List all collections in active database
  COLL <coll_name>                        Switch or create collection (e.g. COLL cops)
  
  INSERT <json>                           Insert single document
  BATCH [<json>, <json>]                  Batch insert multiple documents
  GET <id>                                Fetch document by ID via Primary O(1) Index
  UPDATE <id> <json>                      Update document fields by ID
  DELETE <id>                             Delete document by ID
  
  FIND [json] [SORT <f> [ASC|DESC]]       Execute full Volcano query pipeline
       [PROJECT <f1,f2>] [LIMIT n]        e.g. FIND {""age"":{""$gt"":25}} SORT age DESC LIMIT 5
  
  EXPLAIN FIND [json] [SORT ...]          Display Volcano Execution Plan & Cost
  AGGREGATE <json>                        Aggregation pipeline (groupBy, count, avg, sum, min, max)
  
  INDEX <field> [btree]                   Create Hash or B-Tree Range Index
  INDEX <field1,field2>                   Create Composite Multi-Field Index
  
  STATS                                   Show collection document counts & index metadata
  CHECKPOINT                              Execute Fuzzy Checkpoint and WAL Truncation
  DROP DB <name> | COLL <name> | INDEX <f> Drop database, collection, or index
  CLEAR                                   Clear terminal screen
  EXIT                                    Close shell
-------------------------------------------------------------------------------------
");
}

static void PrintPrettyJson(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var formatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(formatted);
    }
    catch
    {
        Console.WriteLine(json);
    }
}
