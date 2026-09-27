using System.Text.Json;
using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim;

public static class ScenarioLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ScenarioDef Load(string path)
    {
        string json = File.ReadAllText(path);
        return Parse(json, path);
    }

    public static ScenarioDef Parse(string json, string origin = "<inline>")
    {
        try
        {
            return JsonSerializer.Deserialize<ScenarioDef>(json, Options)
                ?? throw new InvalidDataException($"Scénario vide : {origin}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Scénario illisible ({origin}) : {ex.Message}", ex);
        }
    }
}
