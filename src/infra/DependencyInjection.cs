using Amazon.S3;
using app;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddDataProtection();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configuration["Redis:ConnectionString"]!));

        services.AddSingleton<IMongoClient>(_ =>
            new MongoClient(configuration["MongoDB:ConnectionString"]!));

        services.AddSingleton<IAmazonS3>(_ =>
        {
            var s3Config = new AmazonS3Config
            {
                ServiceURL = $"https://{configuration["R2:AccountId"]}.r2.cloudflarestorage.com",
                ForcePathStyle = true
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
                Uri = new Uri(configuration["RabbitMQ:ConnectionString"]!)
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
        services.AddScoped<IEmailService, EmailService>();
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
        services.AddScoped<IShopRateService, ShopRateService>();

        services.AddHostedService<IndexInitializer>();
        services.AddHostedService<FollowConsumerHost>();
        services.AddHostedService<UnfollowConsumerHost>();
        services.AddHostedService<ReactPostConsumerHost>();
        services.AddHostedService<UnreactPostConsumerHost>();
        services.AddHostedService<ReactCommentConsumerHost>();
        services.AddHostedService<UnreactCommentConsumerHost>();
        services.AddHostedService<CreateCommentConsumerHost>();
        services.AddHostedService<RecalculateShopRateConsumerHost>();

        return services;
    }
}
