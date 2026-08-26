using Amazon.S3;
using app;
using infra;
using MongoDB.Driver;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(IMongoDbContext).Assembly));
builder.Services.AddAutoMapper(cfg => { }, typeof(IMongoDbContext).Assembly);

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));

builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration["MongoDB:ConnectionString"]!));

builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var s3Config = new AmazonS3Config
    {
        ServiceURL = $"https://{builder.Configuration["R2:AccountId"]}.r2.cloudflarestorage.com",
        ForcePathStyle = true
    };

    return new AmazonS3Client(
        builder.Configuration["R2:AccessKeyId"],
        builder.Configuration["R2:SecretAccessKey"],
        s3Config);
});

builder.Services.AddScoped<IBcryptService, BCryptService>();
builder.Services.AddScoped<ICurrentIdentityService, CurrentIdentityService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IMongoDbContext, DbContext>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IR2Service, R2Service>();
builder.Services.AddScoped<IRedisService, RedisService>();
builder.Services.AddScoped<IRegistrationService, RegistrationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
