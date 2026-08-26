using app;
using domain;
using MediatR;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace infra;

public class DbContext : IMongoDbContext
{
    private readonly IMongoDatabase _database;
    private readonly IPublisher _publisher;

    public DbContext(IMongoClient client, IConfiguration configuration, IPublisher publisher)
    {
        _database = client.GetDatabase(configuration["MongoDB:DatabaseName"]!);
        _publisher = publisher;
    }

    public IMongoCollection<T> GetCollection<T>(string name) => _database.GetCollection<T>(name);

    public async Task DispatchEventAsync(IEnumerable<Event> events, CancellationToken cancellationToken)
    {
        foreach (dynamic @event in events)
        {
            await PublishEvent(@event, cancellationToken);
        }
    }

    private Task PublishEvent<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : Event
    {
        return _publisher.Publish(new DomainEventNotification<TEvent>(@event), cancellationToken);
    }
}
