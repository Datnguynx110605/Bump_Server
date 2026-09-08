using Amazon.Runtime;
using Amazon.S3;
using app;
using FluentResults;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace infra;

public static class DependencyInjection
{
    public static IServiceCollection AddInfra(this IServiceCollection services, IConfiguration configuration)
    {
        BsonConfiguration.Register();

        services.AddHttpContextAccessor();

        // AbortOnConnectFail defaults to true, so Connect throws outright when Redis is briefly
        // unreachable — and Lazy<T> caches that exception for the process lifetime, which would
        // leave every Redis call failing forever after one blip. Letting the multiplexer reconnect
        // in the background is both the fix and the standard production setting.
        var redisOptions = ConfigurationOptions.Parse(configuration["Redis:ConnectionString"]!);
        redisOptions.AbortOnConnectFail = false;

        var redisConnection = new Lazy<IConnectionMultiplexer>(() =>
            ConnectionMultiplexer.Connect(redisOptions));

        services.AddSingleton<IConnectionMultiplexer>(_ => redisConnection.Value);

        // Data Protection defaults to a key ring on the container filesystem, which a PaaS
        // discards on every deploy and never shares between replicas. Those keys protect the
        // registration token payload, so losing them silently breaks registration, recovery,
        // password reset and email change for every token in flight. Redis is already a hard
        // dependency, so the ring lives there instead.
        services.AddDataProtection()
            .SetApplicationName("Bump")
            .PersistKeysToStackExchangeRedis(
                () => redisConnection.Value.GetDatabase(),
                "bump:dataprotection-keys");

        services.AddSingleton<IMongoClient>(_ =>
            new MongoClient(configuration["MongoDB:ConnectionString"]!));

        services.AddSingleton<IAmazonS3>(_ =>
        {
            var s3Config = new AmazonS3Config
            {
                ServiceURL = $"https://{configuration["R2:AccountId"]}.r2.cloudflarestorage.com",
                ForcePathStyle = true,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            };

            return new AmazonS3Client(
                configuration["R2:AccessKeyId"],
                configuration["R2:SecretAccessKey"],
                s3Config);
        });

        services.AddSingleton<IConnection>(_ =>
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(configuration["RabbitMQ:ConnectionString"]!),
                RequestedHeartbeat = TimeSpan.FromSeconds(30),
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        services.AddHealthChecks()
            .AddCheck<MongoHealthCheck>("mongodb")
            .AddCheck<RedisHealthCheck>("redis")
            .AddCheck<RabbitMQHealthCheck>("rabbitmq");

        services.AddScoped<IBcryptService, BCryptService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<ICurrentIdentityService, CurrentIdentityService>();
        services.AddScoped<IDeleteAccountService, DeleteAccountService>();
        services.AddScoped<IDeletePostService, DeletePostService>();
        services.AddHttpClient<IEmailService, EmailService>(
            client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IFollowService, FollowService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IMongoDbContext, DbContext>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IR2Service, R2Service>();
        services.AddScoped<IRabbitMQService, RabbitMQService>();
        services.AddScoped<IReactionService, ReactionService>();
        services.AddScoped<IRedisService, RedisService>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<ISaveService, SaveService>();
        services.AddScoped<IShopRateService, ShopRateService>();

        services.AddHostedService<IndexInitializer>();
        services.AddHostedService<NormalizedFieldBackfillInitializer>();
        services.AddHostedService<AdminSeeder>();
        services.AddHostedService<SoftDeletePurgeHost>();

        services.AddQueueConsumer<FollowMessage>(
            QueueName.Follow,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<IFollowService>()
                .ProcessFollowAsync(message.FollowerId, message.FollowingId, cancellationToken));

        services.AddQueueConsumer<FollowMessage>(
            QueueName.Unfollow,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<IFollowService>()
                .ProcessUnfollowAsync(message.FollowerId, message.FollowingId, cancellationToken));

        services.AddQueueConsumer<ReactionMessage>(
            QueueName.ReactPost,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<IReactionService>()
                .ProcessReactPostAsync(message.AccountId, message.TargetId, cancellationToken));

        services.AddQueueConsumer<ReactionMessage>(
            QueueName.UnreactPost,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<IReactionService>()
                .ProcessUnreactPostAsync(message.AccountId, message.TargetId, cancellationToken));

        services.AddQueueConsumer<ReactionMessage>(
            QueueName.ReactComment,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<IReactionService>()
                .ProcessReactCommentAsync(message.AccountId, message.TargetId, cancellationToken));

        services.AddQueueConsumer<ReactionMessage>(
            QueueName.UnreactComment,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<IReactionService>()
                .ProcessUnreactCommentAsync(message.AccountId, message.TargetId, cancellationToken));

        services.AddQueueConsumer<CommentMessage>(
            QueueName.CreateComment,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<ICommentService>()
                .ProcessCreateCommentAsync(message.AccountId, message.CommentId, cancellationToken));

        services.AddQueueConsumer<RecalculateShopRateMessage>(
            QueueName.RecalculateShopRate,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<IShopRateService>()
                .ProcessRecalculateShopRateAsync(message.ShopId, cancellationToken));

        services.AddQueueConsumer<SaveMessage>(
            QueueName.SavePost,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<ISaveService>()
                .ProcessSavePostAsync(message.AccountId, message.TargetId, cancellationToken));

        services.AddQueueConsumer<SaveMessage>(
            QueueName.UnsavePost,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<ISaveService>()
                .ProcessUnsavePostAsync(message.AccountId, message.TargetId, cancellationToken));

        services.AddQueueConsumer<SaveMessage>(
            QueueName.SaveShop,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<ISaveService>()
                .ProcessSaveShopAsync(message.AccountId, message.TargetId, cancellationToken));

        services.AddQueueConsumer<SaveMessage>(
            QueueName.UnsaveShop,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<ISaveService>()
                .ProcessUnsaveShopAsync(message.AccountId, message.TargetId, cancellationToken));

        services.AddQueueConsumer<AccountMessage>(
            QueueName.DeleteAccount,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<IDeleteAccountService>()
                .ProcessAfterDeleteAsync(message.AccountId, cancellationToken));

        services.AddQueueConsumer<PostMessage>(
            QueueName.DeletePost,
            message => message.MessageId,
            (provider, message, cancellationToken) => provider.GetRequiredService<IDeletePostService>()
                .ProcessAfterDeleteAsync(message.PostId, cancellationToken));

        return services;
    }

    private static void AddQueueConsumer<TMessage>(
        this IServiceCollection services,
        string queueName,
        Func<TMessage, Guid> messageId,
        Func<IServiceProvider, TMessage, CancellationToken, Task<Result>> handle)
    {
        // AddHostedService uses TryAddEnumerable, which dedupes by implementation type.
        // Queues sharing a message type close the same generic, so registering via
        // AddHostedService would silently drop all but the first of each group.
        services.AddSingleton<IHostedService>(provider => new QueueConsumerHost<TMessage>(
            queueName,
            messageId,
            handle,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<QueueConsumerHost<TMessage>>>()));
    }
}
