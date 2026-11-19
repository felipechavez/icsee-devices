using CamGuard.Api.Features.Discovery;

namespace CamGuard.Tests.Features.Discovery;

public class SubnetPlannerTests
{
    [Test]
    public async Task Plan_ConfiguredSlash24_HostsWithoutNetworkAndBroadcast()
    {
        var plan = SubnetPlanner.Plan("192.168.1.0/24", [], 1024);

        await Assert.That(plan.Subnets).IsEquivalentTo(["192.168.1.0/24"]);
        await Assert.That(plan.Hosts.Count).IsEqualTo(254);
        await Assert.That(plan.Hosts[0]).IsEqualTo("192.168.1.1");
        await Assert.That(plan.Hosts[^1]).IsEqualTo("192.168.1.254");
        await Assert.That(plan.Warnings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Plan_AddressInsideSubnet_NormalizedToNetwork()
    {
        var plan = SubnetPlanner.Plan("192.168.1.37/30", [], 1024);

        await Assert.That(plan.Subnets).IsEquivalentTo(["192.168.1.36/30"]);
        await Assert.That(plan.Hosts).IsEquivalentTo(["192.168.1.37", "192.168.1.38"]);
    }

    [Test]
    public async Task Plan_SeveralSubnetsOverlapping_HostsNotRepeated()
    {
        var plan = SubnetPlanner.Plan("10.0.0.0/30, 10.0.0.0/29", [], 1024);

        await Assert.That(plan.Subnets.Count).IsEqualTo(2);
        await Assert.That(plan.Hosts.Distinct().Count()).IsEqualTo(plan.Hosts.Count);
        await Assert.That(plan.Hosts.Count).IsEqualTo(6);
    }

    [Test]
    [Arguments("192.168.1.0")]
    [Arguments("192.168.1.5/32")]
    public async Task Plan_SingleAddress_OneHost(string text)
    {
        var plan = SubnetPlanner.Plan(text, [], 1024);

        await Assert.That(plan.Hosts.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("192.168.1.0/33")]
    [Arguments("192.168.1/24")]
    [Arguments("casa")]
    [Arguments("fe80::1/64")]
    public async Task Plan_InvalidSubnet_Warning(string text)
    {
        var plan = SubnetPlanner.Plan(text, [], 1024);

        await Assert.That(plan.Hosts.Count).IsEqualTo(0);
        await Assert.That(plan.Warnings.Count).IsEqualTo(1);
        await Assert.That(plan.Warnings[0]).Contains("inválida");
    }

    [Test]
    public async Task Plan_PublicSubnet_Skipped()
    {
        var plan = SubnetPlanner.Plan("8.8.8.0/24", [], 1024);

        await Assert.That(plan.Hosts.Count).IsEqualTo(0);
        await Assert.That(plan.Warnings[0]).Contains("no es una red local privada");
    }

    [Test]
    public async Task Plan_TooManyHosts_Skipped()
    {
        var plan = SubnetPlanner.Plan("10.0.0.0/16, 192.168.0.0/24", [], 1024);

        await Assert.That(plan.Subnets).IsEquivalentTo(["192.168.0.0/24"]);
        await Assert.That(plan.Warnings.Count).IsEqualTo(1);
        await Assert.That(plan.Warnings[0]).Contains("10.0.0.0/16");
    }

    [Test]
    public async Task Plan_NotConfigured_Slash24OfCameras()
    {
        var plan = SubnetPlanner.Plan("", ["192.168.1.23", "192.168.1.40", "10.1.2.3", "", "no-ip"], 1024);

        await Assert.That(plan.Subnets).IsEquivalentTo(["192.168.1.0/24", "10.1.2.0/24"]);
        await Assert.That(plan.Hosts.Count).IsEqualTo(508);
    }

    [Test]
    public async Task Plan_NotConfiguredWithoutCameras_WarnsToConfigure()
    {
        var plan = SubnetPlanner.Plan(null, [], 1024);

        await Assert.That(plan.Hosts.Count).IsEqualTo(0);
        await Assert.That(plan.Warnings[0]).Contains("LAN_SUBNETS");
    }

    [Test]
    [Arguments("10.20.30.40", true)]
    [Arguments("172.16.0.1", true)]
    [Arguments("172.31.255.1", true)]
    [Arguments("172.32.0.1", false)]
    [Arguments("192.168.50.1", true)]
    [Arguments("100.64.0.1", false)]
    public async Task IsPrivate_Rfc1918(string ip, bool expected)
    {
        SubnetPlanner.TryParseCidr(ip, out var address, out _);

        await Assert.That(SubnetPlanner.IsPrivate(address)).IsEqualTo(expected);
    }
}
