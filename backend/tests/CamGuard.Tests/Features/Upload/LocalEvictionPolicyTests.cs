using CamGuard.Api.Features.Upload;

namespace CamGuard.Tests.Features.Upload;

public class LocalEvictionPolicyTests
{
    private const long Mb = 1L << 20;
    private const long Gb = 1L << 30;
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static EvictionCandidate Clip(string name, int minutes, EvictionTier tier, long bytes = 100 * Mb) =>
        new(Guid.Parse($"00000000-0000-0000-0000-{name.PadLeft(12, '0')}"), T0.AddMinutes(minutes), bytes, tier);

    private static IReadOnlyList<EvictionCandidate> Select(IEnumerable<EvictionCandidate> c, long used, long free = 100 * Gb, long max = Gb, long minFree = 512 * Mb) =>
        LocalEvictionPolicy.Select(c, used, free, max, minFree);

    [Test]
    [Arguments("nada", false, EvictionTier.Nothing)]
    [Arguments(null, false, EvictionTier.Unanalyzed)]
    [Arguments("perro", false, EvictionTier.Animal)]
    [Arguments("persona", false, EvictionTier.Person)]
    [Arguments("persona", true, EvictionTier.PriorityEvent)]
    public async Task Tier_ByDetection(string? detection, bool priority, EvictionTier expected)
    {
        await Assert.That(LocalEvictionPolicy.Tier(detection, priority)).IsEqualTo(expected);
    }

    [Test]
    public async Task Tier_DoorbellBeatsNothing()
    {
        await Assert.That(LocalEvictionPolicy.Tier("nada", hasPriorityEvent: true)).IsEqualTo(EvictionTier.PriorityEvent);
    }

    [Test]
    public async Task Select_UnderCap_NothingToEvict()
    {
        var candidates = new[] { Clip("1", 0, EvictionTier.Nothing) };

        await Assert.That(Select(candidates, used: 500 * Mb)).IsEmpty();
    }

    [Test]
    public async Task Select_OrdersByTierThenAge()
    {
        var person = Clip("1", 0, EvictionTier.Person);
        var newNothing = Clip("2", 10, EvictionTier.Nothing);
        var oldNothing = Clip("3", 5, EvictionTier.Nothing);
        var unanalyzed = Clip("4", 1, EvictionTier.Unanalyzed);

        // 1 GB + 350 MB usados: hay que soltar 350 MB → cuatro clips de 100 MB.
        var selected = Select([person, newNothing, oldNothing, unanalyzed], used: Gb + 350 * Mb);

        await Assert.That(string.Join(",", selected.Select(c => c.Id)))
            .IsEqualTo(string.Join(",", new[] { oldNothing.Id, newNothing.Id, unanalyzed.Id, person.Id }));
    }

    [Test]
    public async Task Select_StopsWhenBothLimitsMet()
    {
        var candidates = new[] { Clip("1", 0, EvictionTier.Nothing), Clip("2", 1, EvictionTier.Nothing), Clip("3", 2, EvictionTier.Nothing) };

        // Bajo el tope tras uno, pero el disco pide 150 MB más libres → dos.
        var selected = Select(candidates, used: Gb + 50 * Mb, free: 400 * Mb);

        await Assert.That(selected.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Select_LowDiskOnly_Evicts()
    {
        var candidates = new[] { Clip("1", 0, EvictionTier.Animal) };

        await Assert.That(Select(candidates, used: 100 * Mb, free: 450 * Mb).Count).IsEqualTo(1);
    }

    [Test]
    public async Task Select_NotEnough_ReturnsAll()
    {
        var candidates = new[] { Clip("1", 0, EvictionTier.Nothing), Clip("2", 1, EvictionTier.PriorityEvent) };

        await Assert.That(Select(candidates, used: 10 * Gb).Count).IsEqualTo(2);
    }
}
