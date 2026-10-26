namespace CamGuard.Api.Features.Detection;

/// <summary>Las clases COCO (80) que interesan, con su nombre en español para etiquetas y avisos.</summary>
public static class CocoClasses
{
    public const int Count = 80;
    public const int Person = 0;

    public const string PersonLabel = "persona";

    /// <summary>Etiqueta de un clip analizado sin personas ni animales.</summary>
    public const string NothingLabel = "nada";

    private static readonly Dictionary<int, string> Animals = new()
    {
        [14] = "pájaro",
        [15] = "gato",
        [16] = "perro",
        [17] = "caballo",
        [18] = "oveja",
        [19] = "vaca",
        [20] = "elefante",
        [21] = "oso",
        [22] = "cebra",
        [23] = "jirafa"
    };

    public static bool IsAnimal(int classId) => Animals.ContainsKey(classId);

    public static bool IsAnimalLabel(string? label) => label is not null && Animals.ContainsValue(label);

    public static string? Name(int classId) => classId == Person ? PersonLabel : Animals.GetValueOrDefault(classId);
}
