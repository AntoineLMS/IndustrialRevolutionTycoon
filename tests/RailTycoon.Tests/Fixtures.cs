using RailTycoon.Sim.Economy;

namespace RailTycoon.Tests;

internal static class Fixtures
{
    /// <summary>
    /// Remonte l'arborescence jusqu'au dossier contenant data/, pour que les
    /// tests trouvent les scénarios quel que soit le dossier de compilation.
    /// </summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data")) &&
                File.Exists(Path.Combine(dir.FullName, "data", "heartland.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Impossible de localiser la racine du dépôt (dossier data/).");
    }

    public static string HeartlandPath() => Path.Combine(RepoRoot(), "data", "heartland.json");

    public static CargoDef Grain(double basePrice = 10, double elasticity = 0.4)
        => new() { Id = "grain", Name = "Blé", BasePrice = basePrice, Elasticity = elasticity };

    public static PriceModelDef DefaultPriceModel() => new();

    /// <summary>Scénario nu, à compléter par chaque test.</summary>
    public static ScenarioDef Bare(string id = "test")
        => new()
        {
            Id = id,
            Name = id,
            Seed = 1,
            StartingCash = 10_000,
            Cargos = { Grain() },
        };

    public static CityDef City(string id, double demand = 0, double production = 0, double stock = 0)
    {
        var city = new CityDef { Id = id, Name = id };
        if (demand > 0) city.Demand["grain"] = demand;
        if (production > 0) city.Production["grain"] = production;
        if (stock > 0) city.InitialStock["grain"] = stock;
        return city;
    }
}
