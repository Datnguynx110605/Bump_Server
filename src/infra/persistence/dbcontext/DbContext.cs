using app;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace infra;

public class DbContext : IMongoDbContext
{
    private readonly IMongoDatabase _database;

    public DbContext(
        IMongoClient client,
        IConfiguration configuration)
    {
        _database = client.GetDatabase(configuration["MongoDB:DatabaseName"]!);
    }

    public IMongoCollection<T> GetCollection<T>(string name) => _database.GetCollection<T>(name);
}
