using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace infra;

// Runs as a BackgroundService rather than IHostedService.StartAsync so index builds
// never block the app from serving traffic. Queries touching a new index may run
// unindexed until it finishes building.
internal sealed class IndexInitializer : BackgroundService
{
    private readonly IMongoClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IndexInitializer> _logger;

    public IndexInitializer(
        IMongoClient client,
        IConfiguration configuration,
        ILogger<IndexInitializer> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await IndexConfiguration.CreateAsync(
                _client,
                _configuration,
                stoppingToken);

            _logger.LogInformation("Index creation completed");
        }
        catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Index creation failed");
        }
    }
}
