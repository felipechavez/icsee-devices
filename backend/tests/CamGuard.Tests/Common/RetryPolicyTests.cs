using CamGuard.Api.Features.Upload;

namespace CamGuard.Tests.Features.Upload;

public class RetryPolicyTests
{
    [Test]
    [Arguments(1, 5)]
    [Arguments(2, 10)]
    [Arguments(3, 20)]
    [Arguments(4, 40)]
    [Arguments(7, 320)]
    public async Task Delay_GrowsExponentially(int attempts, int expectedSeconds)
    {
        await Assert.That(RetryPolicy.Delay(attempts)).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Test]
    [Arguments(8)]
    [Arguments(50)]
    [Arguments(int.MaxValue)]
    public async Task Delay_IsCappedAtMax(int attempts)
    {
        await Assert.That(RetryPolicy.Delay(attempts)).IsEqualTo(RetryPolicy.MaxDelay);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-3)]
    public async Task Delay_NonPositiveAttempts_UsesBaseDelay(int attempts)
    {
        await Assert.That(RetryPolicy.Delay(attempts)).IsEqualTo(RetryPolicy.BaseDelay);
    }
}
