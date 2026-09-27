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

    /// <summary>Second scénario du module `content` : chaîne acier, géographie isolée.</summary>
    public static string IronpeakPath() => Path.Combine(RepoRoot(), "data", "ironpeak.json");

    /// <summary>Catalogue de locomotives historiques du module `content`.</summary>
    public static string LocomotivesPath() => Path.Combine(RepoRoot(), "data", "locomotives.json");

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

    /// <summary>
    /// Scénario doté d'un réseau minimal : un relief plat de dix kilomètres de côté,
    /// deux gares et un tronçon droit entre elles. Les tests du module réseau le
    /// complètent pour construire le cas qu'ils veulent refuser ou mesurer.
    /// </summary>
    public static ScenarioDef NetworkScenario()
    {
        var scenario = Bare("reseau");
        scenario.Cities.Add(City("a-ville", production: 1.0, stock: 40));
        scenario.Cities.Add(City("b-ville", demand: 0.4));
        scenario.Network = new RailTycoon.Sim.Network.RailNetworkDef
        {
            Terrain = new RailTycoon.Sim.Network.TerrainDef
            {
                Columns = 11, Rows = 11, CellSizeKm = 1.0, BaseElevationM = 100,
            },
            Nodes =
            {
                new RailTycoon.Sim.Network.TrackNodeDef { Id = "a", City = "a-ville", XKm = 1, YKm = 5 },
                new RailTycoon.Sim.Network.TrackNodeDef { Id = "b", City = "b-ville", XKm = 9, YKm = 5 },
            },
            Edges = { new RailTycoon.Sim.Network.TrackEdgeDef { Id = "a-b", From = "a", To = "b" } },
            Routes =
            {
                new RailTycoon.Sim.Network.TrackRouteDef { Id = "main", Name = "main", Nodes = { "a", "b" } },
            },
        };
        return scenario;
    }

    public static CityDef City(string id, double demand = 0, double production = 0, double stock = 0)
    {
        var city = new CityDef { Id = id, Name = id };
        if (demand > 0) city.Demand["grain"] = demand;
        if (production > 0) city.Production["grain"] = production;
        if (stock > 0) city.InitialStock["grain"] = stock;
        return city;
    }
}
