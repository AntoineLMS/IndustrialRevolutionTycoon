using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Telemetry;

namespace RailTycoon.Tests;

/// <summary>
/// Le commerce entre installations d'une même ville. Décision de l'équipe du
/// 27 octobre 2026 : chaque installation a son marché et sa position, le bois est
/// moins cher à la forêt qu'à la scierie même dans la même ville, un charroi local
/// limité passe sans train, et une gare ne sert que ce qui est à sa portée. Le
/// scénario de référence est <c>data/timberford.json</c> : une ville, deux gares,
/// un train qui n'en sort jamais.
/// </summary>
internal static class LocalTradeTests
{
    private const int Ticks = 720;

    private static string TimberfordPath() => Path.Combine(Fixtures.RepoRoot(), "data", "timberford.json");

    public static void Register(TestRunner runner)
    {
        runner.Add("navette locale — un train qui ne sort pas de sa ville gagne de l'argent", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(TimberfordPath()));
            Check.True(sim.World.Lines.Single().Stops.All(s => s.CityId == "timberford"),
                "la ligne ne doit desservir que Timberford");

            double initial = Invariants.InitialStockTotal(sim.World);
            for (int i = 0; i < Ticks; i++)
            {
                sim.Step();
                var violations = Invariants.Check(sim.World, initial);
                if (violations.Count > 0)
                    Check.True(false, $"tick {sim.World.Tick.Index} : {violations[0].Rule} — {violations[0].Detail}");
            }

            // Mesuré le 27 octobre 2026 : 14 776 de résultat, 1 260 chargements de
            // grumes portés de la forêt à la scierie, la scierie à 100 %. Le seuil
            // protège contre une régression, il ne consacre pas ce chiffre.
            double profit = sim.World.Company.NetProfit;
            Check.True(profit > 5_000, $"la navette doit rapporter, résultat {profit:0}");
            var sawmill = sim.World.CityById("timberford").Industries.Single();
            Check.True(sawmill.Input("logs").TotalProduced == 0, "la scierie ne produit pas de grumes");
        });

        runner.Add("navette locale — les grumes sont moins chères à la forêt qu'à la scierie, dans la même ville", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(TimberfordPath()));
            var stats = new RunStatistics { WarmupTicks = 90 };
            for (int i = 0; i < Ticks; i++)
            {
                sim.Step();
                stats.Sample(sim.World);
            }

            var city = sim.World.CityById("timberford");
            double forest = stats.Market(city.ProductionMarket("logs")!).MeanRatio;
            double sawmill = stats.Market(city.Industries.Single().Input("logs")).MeanRatio;
            Check.Less(forest * 1.5, sawmill,
                $"grumes à la forêt ×{forest:0.00} contre ×{sawmill:0.00} à la scierie");
        });

        runner.Add("gare — une gare ne sert que les marchés à sa portée", () =>
        {
            var world = new Simulation(ScenarioLoader.Load(TimberfordPath())).World;
            var stops = world.Lines.Single().Stops;
            var city = world.CityById("timberford");
            var forest = city.ProductionMarket("logs")!;
            var sawmillLogs = city.Industries.Single().Input("logs");

            Check.True(stops[0].Markets.Contains(forest), "la gare de la forêt dessert la forêt");
            Check.True(stops[0].Markets.Contains(city.Market("boards")), "la gare de la forêt dessert la place, à 6 km");
            Check.True(!stops[0].Markets.Contains(sawmillLogs), "la gare de la forêt ne dessert pas la scierie, à 12 km");
            Check.True(stops[1].Markets.All(m => m.SiteId == "sawmill"), "la gare de la scierie ne dessert que la scierie");
        });

        runner.Add("charroi — il ne passe jamais plus que sa capacité", () =>
        {
            // Sans train : tout ce qui arrive à la scierie y vient par charrette.
            var scenario = ScenarioLoader.Load(TimberfordPath());
            scenario.Trains.Clear();
            var sim = new Simulation(scenario);
            var city = sim.World.CityById("timberford");
            double capacity = scenario.Economy.Cartage.CapacityPerTick;

            double carted = 0;
            for (int i = 0; i < Ticks; i++)
            {
                sim.Step();
                foreach (string cargoId in sim.World.CargoOrder)
                {
                    double tick = sim.World.MarketsOf(city).Where(m => m.CargoId == cargoId).Sum(m => m.CartedInThisTick);
                    Check.True(tick <= capacity + 1e-9,
                        $"tick {sim.World.Tick.Index}, {cargoId} : {tick:0.###} chargements par charroi pour {capacity}");
                    if (cargoId == "logs") carted += tick;
                }
            }
            Check.True(carted > 0.9 * capacity * Ticks,
                $"l'écart forêt → scierie doit occuper le charroi presque tout le temps, {carted:0} chargements");
        });

        runner.Add("charroi — il s'arrête quand l'écart de prix ne paie plus la charrette", () =>
        {
            var scenario = ScenarioLoader.Load(TimberfordPath());
            scenario.Trains.Clear();
            scenario.Economy.Cartage.CostPerLoadKm = 100;
            var sim = new Simulation(scenario);
            sim.Run(100);
            var city = sim.World.CityById("timberford");
            Check.Near(0, city.ProductionMarket("logs")!.CartedOutThisTick, 1e-12,
                "à 1 200 la charrette sur 12 km, aucune grume ne doit quitter la forêt");
        });

        runner.Add("charroi et gares — des données absurdes sont refusées au chargement", () =>
        {
            var negative = ScenarioLoader.Load(TimberfordPath());
            negative.Economy.Cartage.CapacityPerTick = -1;
            Check.Throws<InvalidDataException>(() => new Simulation(negative), "une capacité de charroi négative");

            var noSlice = ScenarioLoader.Load(TimberfordPath());
            noSlice.Economy.Cartage.Slices = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(noSlice), "un charroi sans tranche");

            var blind = ScenarioLoader.Load(TimberfordPath());
            blind.Lines.Single().Stops[1].RadiusKm = 0.5;
            blind.Cities.Single().Industries.Single().DxKm = 4;
            Check.Throws<InvalidDataException>(() => new Simulation(blind), "une gare qui ne dessert rien");

            var phantom = ScenarioLoader.Load(TimberfordPath());
            phantom.Cities.Single().ProductionSites["boards"] = new SiteOffsetDef { DxKm = 1 };
            Check.Throws<InvalidDataException>(() => new Simulation(phantom), "une production placée mais jamais déclarée");
        });
    }
}
