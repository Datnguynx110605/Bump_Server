using domain;
using MongoDB.Driver;

namespace app;

public interface IMongoDbContext
{
    IMongoCollection<T> GetCollection<T>(string name);

    Task DispatchEventAsync(IEnumerable<Event> events, CancellationToken cancellationToken);
}
