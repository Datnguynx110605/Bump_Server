using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace unit;

public class AutoMapperConfigurationTests
{
    [Fact]
    public void Configuration_FromAllThirteenProfiles_IsValid()
    {
        var configuration = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<AccountProfile>();
            cfg.AddProfile<AvatarProfile>();
            cfg.AddProfile<BlockProfile>();
            cfg.AddProfile<CommentProfile>();
            cfg.AddProfile<FollowerProfile>();
            cfg.AddProfile<FollowingProfile>();
            cfg.AddProfile<ImageProfile>();
            cfg.AddProfile<NotificationProfile>();
            cfg.AddProfile<PostProfile>();
            cfg.AddProfile<ReactionProfile>();
            cfg.AddProfile<RefreshTokenProfile>();
            cfg.AddProfile<ReportProfile>();
            cfg.AddProfile<ShopProfile>();
        }, NullLoggerFactory.Instance);

        configuration.AssertConfigurationIsValid();
    }
}
