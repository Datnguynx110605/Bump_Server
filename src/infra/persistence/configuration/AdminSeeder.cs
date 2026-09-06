using app;
using domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace infra;

// No endpoint ever grants Role.Admin, so the only way to get an admin account was a
// manual mongosh update. This seeds/promotes one account from config on every boot,
// which makes it work on Railway via Admin__Email/Admin__Password env vars. Leaving
// those unset skips seeding entirely, so it's a no-op everywhere else.
internal sealed class AdminSeeder : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminSeeder> _logger;

    public AdminSeeder(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AdminSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? email = _configuration["Admin:Email"];
        string? password = _configuration["Admin:Password"];

        if(string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogInformation("Admin seeding skipped: Admin:Email/Admin:Password not configured");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
            var bcryptService = scope.ServiceProvider.GetRequiredService<IBcryptService>();
            var accountCollection = dbContext.GetCollection<Account>(CollectionName.Account);

            var existing = await accountCollection
                .Find(x => x.Email.Value == email && !x.IsDeleted)
                .FirstOrDefaultAsync(stoppingToken);

            if(existing is null)
            {
                await CreateAdminAsync(accountCollection, bcryptService, email, password, stoppingToken);
            }
            else if(existing.Role != Role.Admin)
            {
                await PromoteAdminAsync(accountCollection, existing, stoppingToken);
            }
        }
        catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Admin seeding failed");
        }
    }

    private async Task CreateAdminAsync(
        IMongoCollection<Account> accountCollection,
        IBcryptService bcryptService,
        string email,
        string password,
        CancellationToken stoppingToken)
    {
        var emailResult = Email.Create(email);
        var nameResult = Name.Create(_configuration["Admin:Name"] ?? "admin");
        if(!emailResult.IsSuccess || !nameResult.IsSuccess)
        {
            _logger.LogError("Admin seeding failed: {Error}", emailResult.Error ?? nameResult.Error);
            return;
        }

        var accountResult = Account.Create(
            emailResult.Value!,
            nameResult.Value!,
            bcryptService.HashPassword(password),
            bio: null);
        if(!accountResult.IsSuccess)
        {
            _logger.LogError("Admin seeding failed: {Error}", accountResult.Error);
            return;
        }

        Account account = accountResult.Value!;
        account.PromoteToAdmin();

        await accountCollection.InsertOneAsync(account, cancellationToken: stoppingToken);

        _logger.LogInformation("Admin account seeded for {Email}", email);
    }

    private async Task PromoteAdminAsync(
        IMongoCollection<Account> accountCollection,
        Account existing,
        CancellationToken stoppingToken)
    {
        var promoteResult = existing.PromoteToAdmin();
        if(!promoteResult.IsSuccess)
        {
            _logger.LogError("Admin promotion failed: {Error}", promoteResult.Error);
            return;
        }

        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, existing.Id),
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false));

        var update = Builders<Account>.Update
            .Set(x => x.Role, existing.Role)
            .Set(x => x.UpdatedAt, existing.UpdatedAt);

        await accountCollection.UpdateOneAsync(filter, update, cancellationToken: stoppingToken);

        _logger.LogInformation("Existing account promoted to admin for {Email}", existing.Email.Value);
    }
}
