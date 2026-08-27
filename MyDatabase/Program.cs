using MyDatabase;

// Initialize Database stored under "data" directory
var db = new Database("data");

// Get or Create "users" collection
var users = db.GetCollection("users");

Console.WriteLine($"Current documents in 'users' collection: {users.GetAll().Count}");

// Insert a sample user
var user = new Document();
user.Data["name"] = "Dinesh";
user.Data["age"] = 30;
user.Data["email"] = "dinesh@example.com";
user.Data["city"] = "San Francisco";

users.Insert(user);
Console.WriteLine($"[+] Inserted user '{user.Data["name"]}' with ID: {user.Id}");

// Persist collection to disk
db.Save("users");
Console.WriteLine("[*] Saved collection to disk.\n");

// Read and display all users
Console.WriteLine("--- Current Users in Database ---");
foreach (var doc in users.GetAll())
{
    Console.WriteLine($"ID: {doc.Id}");
    foreach (var kvp in doc.Data)
    {
        Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
    }
    Console.WriteLine();
}
