using SingamDB.Core;

Console.WriteLine("=== SingamDB Basic Usage Example ===");

// 1. Initialize engine
var engine = new DatabaseEngine("sample_data");
var db = engine.GetOrCreateDatabase("app_db");
var coll = db.GetOrCreateCollection("customers");

// 2. Create an index
coll.CreateIndex("email", isBTree: true);

// 3. Insert document
var customer = new Document(new Dictionary<string, object>
{
    { "name", "John Doe" },
    { "email", "john@example.com" },
    { "country", "USA" }
});

coll.Insert(customer);
Console.WriteLine($"Inserted Customer ID: {customer.Id}");

// 4. Find document
var found = coll.FindById(customer.Id);
if (found != null)
{
    Console.WriteLine($"Found Customer: {found.GetValue("name")} ({found.GetValue("email")})");
}

// 5. Flush to disk
db.Flush();
Console.WriteLine("Data flushed successfully.");
