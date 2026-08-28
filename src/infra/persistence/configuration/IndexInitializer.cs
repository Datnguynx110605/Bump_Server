using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace infra;

internal sealed class IndexInitializer : IHostedService
{
    private readonly IMongoClient _client;
    private readonly IConfiguration _configuration;

    public IndexInitializer(IMongoClient client, IConfiguration configuration)
    {
        _client = client;
        _configuration = configuration;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return IndexConfiguration.CreateAsync(_client, _configuration, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
