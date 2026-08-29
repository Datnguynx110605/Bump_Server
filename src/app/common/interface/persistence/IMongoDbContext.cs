using MongoDB.Driver;

namespace app;

public interface IMongoDbContext
{
    IMongoCollection<T> GetCollection<T>(string name);
}
