using System.Net.Http.Json;
using System.Text.Json;

var serverUrl = args.Length > 0 ? args[0] : "http://localhost:7777";
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
                    currentCollection = null; // Reset collection when switching database
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

            case "FIND":
            case "ALL":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                if (string.IsNullOrWhiteSpace(param) || param.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    var allDocs = await client.GetFromJsonAsync<List<JsonElement>>($"/api/databases/{currentDb}/collections/{currentCollection}/documents");
                    PrintPrettyJson(JsonSerializer.Serialize(allDocs, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    var queryFilter = JsonSerializer.Deserialize<Dictionary<string, object>>(param);
                    var queryResp = await client.PostAsJsonAsync($"/api/databases/{currentDb}/collections/{currentCollection}/query", queryFilter);
                    if (queryResp.IsSuccessStatusCode)
                    {
                        var queryResult = await queryResp.Content.ReadAsStringAsync();
                        PrintPrettyJson(queryResult);
                    }
                    else
                    {
                        Console.WriteLine($"Error: {await queryResp.Content.ReadAsStringAsync()}");
                    }
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

            case "EXPLAIN":
                if (string.IsNullOrWhiteSpace(currentCollection))
                {
                    Console.WriteLine("[!] No active collection. Use 'COLL <collection_name>' first.");
                    break;
                }
                string queryStr = param;
                if (queryStr.StartsWith("FIND ", StringComparison.OrdinalIgnoreCase))
                {
                    queryStr = queryStr.Substring(5).Trim();
                }
                if (string.IsNullOrWhiteSpace(queryStr))
                {
                    queryStr = "{}";
                }

                var explainFilter = JsonSerializer.Deserialize<Dictionary<string, object>>(queryStr);
                var explainResp = await client.PostAsJsonAsync($"/api/databases/{currentDb}/collections/{currentCollection}/explain", explainFilter);
                if (explainResp.IsSuccessStatusCode)
                {
                    var explainJson = await explainResp.Content.ReadAsStringAsync();
                    PrintPrettyJson(explainJson);
                }
                else
                {
                    Console.WriteLine($"Explain error: {await explainResp.Content.ReadAsStringAsync()}");
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
                    Console.WriteLine("Usage: INDEX <field_name>");
                    break;
                }

                var idxResp = await client.PostAsJsonAsync($"/api/databases/{currentDb}/collections/{currentCollection}/indexes", new { field = param });
                if (idxResp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[OK] Index created on '{param}' successfully.");
                }
                else
                {
                    Console.WriteLine($"Error: {await idxResp.Content.ReadAsStringAsync()}");
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
    Console.WriteLine("  SingamDB Interactive Shell v3.0.0");
    Console.WriteLine("===================================================\n");
    Console.ResetColor();
}

static void PrintHelp()
{
    Console.WriteLine(@"
Available Commands:
----------------------------------------------------------------------
  SHOW DBS                     List all databases
  USE <db_name>                Switch active database (e.g. USE production)
  SHOW COLLECTIONS             List all collections in active database
  COLL <coll_name>             Switch or create active collection (e.g. COLL cops)
  
  INSERT <json>                Insert document (e.g. INSERT {""name"":""Raj"", ""age"":30})
  FIND [json]                  Query documents (e.g. FIND or FIND {""age"":{""$gt"":25}})
  GET <id>                     Fetch document by ID via Primary Index
  UPDATE <id> <json>           Update document by ID
  DELETE <id>                  Delete document by ID
  
  EXPLAIN FIND <json>          Explain query plan (FULL_SCAN vs INDEX_SCAN)
  INDEX <field>                Create secondary index on field
  STATS                        Show current collection document count & indexes
  CLEAR                        Clear terminal screen
  EXIT                         Close shell
----------------------------------------------------------------------
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
