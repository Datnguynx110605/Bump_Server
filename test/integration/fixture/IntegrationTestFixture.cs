using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Testcontainers.Minio;
using Testcontainers.MongoDb;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace integration;

// One assembly-scoped fixture owns all four containers and the WebApplicationFactory, per
// test-strategy.md §4 — twelve QueueConsumerHost singletons and a blocking RabbitMQ
// connection factory make a per-class container slow enough that the suite stops being run.
public sealed class IntegrationTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string BucketName = "bump-test";
    private const string MinioAccessKey = "bump-test";
    private const string MinioSecretKey = "bump-test-secret";

    private readonly MongoDbContainer _mongo = new MongoDbBuilder()
        .WithImage("mongo:7")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .Build();

    // Neither of MinIO's own registries serves this anonymously any more — Docker Hub's
    // minio/minio and quay.io/minio/minio both answer 401 since the 2025 licensing change.
    // Chainguard's rebuild is a drop-in: same /usr/bin/minio entrypoint, so MinioBuilder's
    // `server /data` command, MINIO_ROOT_USER/PASSWORD and the /minio/health/ready wait all
    // work unchanged. It publishes only a rolling `latest` to the free tier, which is the one
    // thing to know about it — the image can move under a green suite.
    private readonly MinioContainer _minio = new MinioBuilder()
        .WithImage("cgr.dev/chainguard/minio:latest")
        .WithUsername(MinioAccessKey)
        .WithPassword(MinioSecretKey)
        .Build();

    public ScriptedHttpMessageHandler HttpHandler { get; } = new();

    public PublishedMessageLog PublishedMessages => Services.GetRequiredService<PublishedMessageLog>();

    public CapturingLogEventSink LogEvents { get; } = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _mongo.StartAsync(),
            _redis.StartAsync(),
            _rabbitMq.StartAsync(),
            _minio.StartAsync());

        await CreateBucketAsync();

        // Program.cs's top-level statements call AddApi(builder.Configuration) — which runs
        // RequireJwtConfiguration synchronously — before builder.Build() ever runs, so a
        // ConfigureAppConfiguration source added through ConfigureWebHost arrives too late:
        // WebApplicationFactory's HostFactoryResolver only injects it as part of the deferred
        // Build() call, well after that eager read already threw (caught by Program.cs's own
        // top-level try/catch, which logs and exits without ever building a host — the
        // "entry point exited without ever building an IHost" failure this produced the first
        // time). Environment variables are read by WebApplication.CreateBuilder(args) itself,
        // synchronously, before any of Program.cs's own code runs, so they are the one config
        // source that is not too late for this handler's eager checks.
        SetConfigurationEnvironmentVariables();

        // Building the host now, rather than lazily on the first test's first call, so
        // IndexInitializer's background index build below has somewhere to run before any
        // test starts. RabbitMQ's IConnection also blocks at resolution (§2.3), which is
        // why the containers above are already healthy by this point.
        using var scope = Services.CreateScope();
        var mongoClient = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // IndexInitializer runs as a fire-and-forget BackgroundService, so a test started
        // right after boot cannot assume its partial unique indexes already exist.
        // CreateIndexAsync is idempotent — running it again here just guarantees they are
        // in place before any test depends on them.
        await IndexConfiguration.CreateAsync(mongoClient, configuration, CancellationToken.None);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        await Task.WhenAll(
            _mongo.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbitMq.DisposeAsync().AsTask(),
            _minio.DisposeAsync().AsTask());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development is the only environment AddApi's host would turn on
        // ValidateScopes/ValidateOnBuild for, which would eagerly construct every
        // registered singleton (including the external-adapter token providers) just to
        // validate the graph. "Testing" keeps construction lazy, matching production.
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(HttpHandler);
            services.ConfigureHttpClientDefaults(http =>
                http.ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<ScriptedHttpMessageHandler>()));

            services.AddScoped<ICurrentIdentityService, FakeCurrentIdentityService>();

            // Picked up by Program.cs's own .ReadFrom.Services(services) call — no
            // production change needed. See CapturingLogEventSink's own comment.
            services.AddSingleton(LogEvents);
            services.AddSingleton<Serilog.Core.ILogEventSink>(LogEvents);

            // Wraps the real IRabbitMQService (registered Scoped in infra/DependencyInjection.cs)
            // rather than replacing it, so every queue consumer above still receives every
            // message unchanged — this only adds a record of what was published, for the
            // handler-tier "the published message" assertion in test-strategy.md §3.3.
            services.AddSingleton<PublishedMessageLog>();
            var rabbitDescriptor = services.Single(d => d.ServiceType == typeof(IRabbitMQService));
            services.Remove(rabbitDescriptor);
            services.AddScoped<IRabbitMQService>(sp =>
            {
                var inner = (IRabbitMQService)ActivatorUtilities.CreateInstance(sp, rabbitDescriptor.ImplementationType!);
                return new RecordingRabbitMQService(inner, sp.GetRequiredService<PublishedMessageLog>());
            });

            // §2.3: NormalizedFieldBackfillInitializer is a one-shot migration with nothing
            // to backfill in a fresh test database, and SoftDeletePurgeHost deletes rows on
            // a timer — both removed. IndexInitializer stays: the partial unique indexes it
            // builds are exactly what the CreatePostHandler race test depends on. The queue
            // consumers stay too — nothing in this slice sends a request racing their 202,
            // and AddQueueConsumer's ImplementationType-is-null registrations are exactly
            // what a class-based filter here could not remove without this workaround.
            var toRemove = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    (descriptor.ImplementationType == typeof(NormalizedFieldBackfillInitializer) ||
                        descriptor.ImplementationType == typeof(SoftDeletePurgeHost)))
                .ToList();

            foreach(var descriptor in toRemove)
                services.Remove(descriptor);
        });
    }

    private async Task CreateBucketAsync()
    {
        var s3Config = new AmazonS3Config
        {
            ServiceURL = _minio.GetConnectionString(),
            ForcePathStyle = true
        };

        using var client = new AmazonS3Client(MinioAccessKey, MinioSecretKey, s3Config);

        await client.PutBucketAsync(new PutBucketRequest { BucketName = BucketName });
    }

    private void SetConfigurationEnvironmentVariables()
    {
        foreach(var (key, value) in BuildConfiguration())
            Environment.SetEnvironmentVariable(key.Replace(":", "__"), value);
    }

    private Dictionary<string, string?> BuildConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "integration-test-signing-key-not-for-production-use",
            ["Jwt:Issuer"] = "bump-integration-tests",
            ["Jwt:Audience"] = "bump-integration-tests",
            ["Jwt:ExpiryMinutes"] = "15",

            ["MongoDB:ConnectionString"] = _mongo.GetConnectionString(),
            ["MongoDB:DatabaseName"] = "bump_integration_tests",

            ["Redis:ConnectionString"] = _redis.GetConnectionString(),

            ["RabbitMQ:ConnectionString"] = _rabbitMq.GetConnectionString(),

            ["R2:AccessKeyId"] = MinioAccessKey,
            ["R2:SecretAccessKey"] = MinioSecretKey,
            ["R2:BucketName"] = BucketName,
            ["R2:FolderName"] = "posts",
            ["R2:PublicUrl"] = "http://bump-test-public.invalid",
            ["R2:ServiceUrl"] = _minio.GetConnectionString(),

            // ApnsTokenProvider.Required() throws at construction if any of these are
            // missing, and Current() signs a real ES256 token from the PEM the moment
            // ApnsServiceTests sends a push — a real key, not a placeholder, or that
            // signing step throws before the scripted HttpMessageHandler ever sees the
            // request.
            ["Apns:KeyId"] = "test-key-id",
            ["Apns:TeamId"] = "test-team-id",
            ["Apns:PrivateKey"] = GenerateEcdsaPrivateKeyPem(),
            ["Apns:BundleId"] = "com.bump.test",

            // Maps:* left configured (unlike the two above, MapsTokenProvider treats a
            // missing key as "not configured" rather than throwing) so ApplePlaceService
            // can be exercised through the scripted HttpMessageHandler instead of always
            // short-circuiting to null.
            ["Maps:KeyId"] = "test-key-id",
            ["Maps:TeamId"] = "test-team-id",
            ["Maps:PrivateKey"] = GenerateEcdsaPrivateKeyPem()
        };
    }

    private static string GenerateEcdsaPrivateKeyPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(key.ExportECPrivateKeyPem()));
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationTestFixture>
{
    public const string Name = "integration";
}
