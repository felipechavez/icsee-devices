using CamGuard.Api.Common.Data;
using CamGuard.Api.Features.Detection;

namespace CamGuard.Tests.Features.Detection;

public class DetectionPolicyTests
{
    private const int Dog = 16;
    private const int Car = 2;

    [Test]
    public async Task Evaluate_PersonAndDog_PersonWins()
    {
        var verdict = DetectionPolicy.Evaluate([new(Dog, 0.9), new(CocoClasses.Person, 0.6)], people: true, animals: true, 0.5);

        await Assert.That(verdict).IsEqualTo(new DetectionVerdict("persona", 0.6, true));
    }

    [Test]
    public async Task Evaluate_AnimalsDisabled_DogIgnored()
    {
        var verdict = DetectionPolicy.Evaluate([new(Dog, 0.9)], people: true, animals: false, 0.5);

        await Assert.That(verdict).IsEqualTo(new DetectionVerdict("nada", 0, false));
    }

    [Test]
    public async Task Evaluate_PeopleDisabled_DogFound()
    {
        var verdict = DetectionPolicy.Evaluate([new(CocoClasses.Person, 0.9), new(Dog, 0.7)], people: false, animals: true, 0.5);

        await Assert.That(verdict).IsEqualTo(new DetectionVerdict("perro", 0.7, true));
    }

    [Test]
    public async Task Evaluate_BelowThreshold_NothingWithBestScore()
    {
        var verdict = DetectionPolicy.Evaluate([new(CocoClasses.Person, 0.3), new(Dog, 0.4)], people: true, animals: true, 0.5);

        await Assert.That(verdict).IsEqualTo(new DetectionVerdict("nada", 0.4, false));
    }

    [Test]
    public async Task Evaluate_OnlyIrrelevantClasses_Nothing()
    {
        var verdict = DetectionPolicy.Evaluate([new(Car, 0.95)], people: true, animals: true, 0.5);

        await Assert.That(verdict).IsEqualTo(new DetectionVerdict("nada", 0, false));
    }

    [Test]
    public async Task Evaluate_PersonBelowThresholdAndDogAbove_Dog()
    {
        var verdict = DetectionPolicy.Evaluate([new(CocoClasses.Person, 0.4), new(Dog, 0.8)], people: true, animals: true, 0.5);

        await Assert.That(verdict.Label).IsEqualTo("perro");
    }

    private static readonly DetectionVerdict Person = new("persona", 0.8, true);
    private static readonly DetectionVerdict Nothing = new("nada", 0.1, false);

    [Test]
    [Arguments(DetectionMode.Off, false, false, false, DetectionDecision.Keep)]
    [Arguments(DetectionMode.Tag, false, false, false, DetectionDecision.Keep)]
    [Arguments(DetectionMode.Tag, true, false, false, DetectionDecision.Keep)]
    [Arguments(DetectionMode.Filter, true, true, false, DetectionDecision.Keep)]
    [Arguments(DetectionMode.Filter, true, false, false, DetectionDecision.Discard)]
    [Arguments(DetectionMode.Filter, true, false, true, DetectionDecision.Keep)]
    [Arguments(DetectionMode.Filter, false, false, false, DetectionDecision.Keep)]
    [Arguments(DetectionMode.Filter, false, false, true, DetectionDecision.Keep)]
    public async Task Decide_AllCombinations(DetectionMode mode, bool analyzed, bool found, bool priorityEvent, DetectionDecision expected)
    {
        DetectionVerdict? verdict = analyzed ? (found ? Person : Nothing) : null;

        await Assert.That(DetectionPolicy.Decide(mode, verdict, priorityEvent)).IsEqualTo(expected);
    }
}
