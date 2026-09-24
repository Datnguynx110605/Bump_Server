using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace integration;

// test-strategy.md §6's last row: every QueueName member has exactly one AddQueueConsumer
// registration — a queue nobody consumes fills silently, and two consumers on the same
// queue would double-process every message. QueueConsumerHost<TMessage> is internal and its
// _queueName field is private, so reflection is what reaches it; a descriptor count alone
// cannot discriminate which queue each closed QueueConsumerHost<> actually consumes.
[Collection(IntegrationCollection.Name)]
public class QueueRegistrationTests
{
    private readonly IntegrationTestFixture _fixture;

    public QueueRegistrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void EveryQueueNameMember_HasExactlyOneAddQueueConsumerRegistration()
    {
        var queueNames = typeof(QueueName)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        Assert.NotEmpty(queueNames);

        // By instance, not by closed type: QueueConsumerHost<ReactionMessage> alone backs
        // both the react-post and react-comment queues (SaveMessage and CommentMessage are
        // each shared the same way), so grouping by GetType() first would collapse two
        // distinct registrations into one and silently hide a missing consumer.
        var consumedQueueNames = _fixture.Services
            .GetServices<IHostedService>()
            .Where(s => s.GetType().IsGenericType &&
                s.GetType().GetGenericTypeDefinition().Name.StartsWith("QueueConsumerHost", StringComparison.Ordinal))
            .Select(s => (string)s.GetType()
                .GetField("_queueName", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(s)!)
            .ToList();

        Assert.NotEmpty(consumedQueueNames);

        foreach(string queueName in queueNames)
            Assert.True(
                consumedQueueNames.Count(x => x == queueName) == 1,
                $"expected exactly one AddQueueConsumer registration for '{queueName}', found {consumedQueueNames.Count(x => x == queueName)}");

        // No consumer registered for a name that isn't even a QueueName member — a copy-paste
        // typo in AddQueueConsumer's queueName argument would otherwise pass silently.
        Assert.All(consumedQueueNames, name => Assert.Contains(name, queueNames));
    }
}
