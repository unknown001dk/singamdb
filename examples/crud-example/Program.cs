using SingamDB.Core;

Console.WriteLine("=== SingamDB Full CRUD Example ===");

var engine = new DatabaseEngine("crud_data");
var db = engine.GetOrCreateDatabase("inventory_db");
var products = db.GetOrCreateCollection("products");

// CREATE
var item = new Document(new Dictionary<string, object>
{
    { "sku", "LAPTOP-PRO-16" },
    { "title", "Enterprise Laptop 16-inch" },
    { "price", 1499.99 },
    { "stock", 50 }
});
products.Insert(item);
Console.WriteLine($"[CREATE] Inserted product ID: {item.Id}");

// READ
var fetched = products.FindById(item.Id);
Console.WriteLine($"[READ] Fetched: {fetched?.GetValue("title")} - Price: ${fetched?.GetValue("price")}");

// UPDATE
if (fetched != null)
{
    fetched.Data["price"] = 1399.99;
    fetched.Data["stock"] = 45;
    products.Update(fetched);
    Console.WriteLine($"[UPDATE] Updated product price to ${products.FindById(item.Id)?.GetValue("price")}");
}

// DELETE
bool deleted = products.Delete(item.Id);
Console.WriteLine($"[DELETE] Deleted product ID {item.Id}: {deleted}");

Console.WriteLine($"Remaining products in collection: {products.Count()}");
