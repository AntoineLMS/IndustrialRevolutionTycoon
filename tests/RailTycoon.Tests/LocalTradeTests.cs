using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Telemetry;

namespace RailTycoon.Tests;

/// <summary>
/// Le commerce entre installations d'une même ville. Décision de l'équipe du
/// 27 octobre 2026 : chaque installation a son marché et sa position, le bois est
/// moins cher à la forêt qu'à la scierie même dans la même ville, tout peut passer
/// par charrette, mais à 15 km par jour, et une gare sert ce qui est à sa portée, y
/// compris les charrettes qui y passent. Le
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

            // Mesuré le 27 octobre 2026 : 15 275 de résultat, 1 260 chargements de
            // grumes portés de la forêt à la scierie, 718 de planches captés sur les
            // charrettes qui partent de la scierie vers la place, la scierie à 100 %.
            // Le seuil protège contre une régression, il ne consacre pas ce chiffre.
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

        runner.Add("ligne — deux arrêts consécutifs ne peuvent pas être la même gare", () =>
        {
            // Décision de l'équipe du 27 octobre 2026 : une ligne de deux arrêts qui
            // sont la même gare n'est pas une ligne. Le train y rachèterait au même
            // quai ce qu'il vient d'y vendre. Sur le réseau, un aller-retour par un
            // aiguillage la fabriquerait sans le dire : forêt → aiguillage → forêt.
            var scenario = Fixtures.NetworkScenario();
            scenario.Network.Nodes.Add(new RailTycoon.Sim.Network.TrackNodeDef { Id = "aiguillage", XKm = 5, YKm = 8 });
            scenario.Network.Edges.Add(new RailTycoon.Sim.Network.TrackEdgeDef { Id = "a-aiguillage", From = "a", To = "aiguillage" });
            scenario.Network.Routes.Clear();
            scenario.Network.Routes.Add(new RailTycoon.Sim.Network.TrackRouteDef
            {
                Id = "boucle", Name = "boucle", Nodes = { "a", "aiguillage", "a" },
            });

            string? message = null;
            try { _ = new Simulation(scenario); }
            catch (InvalidDataException ex) { message = ex.Message; }
            Check.True(message is not null && message.Contains("deux fois de suite"),
                $"l'itinéraire a → aiguillage → a doit être refusé pour cette raison, message : {message ?? "aucun"}");

            // Sur une ligne saisie à la main, deux arrêts au même point kilométrique
            // seraient la même gare : les distances strictement croissantes l'interdisent.
            var flat = ScenarioLoader.Load(TimberfordPath());
            flat.Lines.Single().Stops[1].DistanceKm = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(flat), "deux arrêts au même point de la ligne");

            // Deux gares différentes d'une même ville, elles, font une ligne : c'est
            // la navette de Timberford, à 12 km l'une de l'autre.
            var stops = new Simulation(ScenarioLoader.Load(TimberfordPath())).World.Lines.Single().Stops;
            Check.True(stops[0].CityId == stops[1].CityId && stops[0].XKm != stops[1].XKm,
                "Timberford : deux gares distinctes dans la même ville");
        });

        runner.Add("charroi — une charrette met le temps du trajet, à 15 km par jour", () =>
        {
            // Sans train, et la forêt reculée à 45 km de la scierie : trois jours de
            // route. La charrette avance d'un jour à l'autre, et ne livre qu'à l'arrivée.
            var scenario = ScenarioLoader.Load(TimberfordPath());
            scenario.Trains.Clear();
            scenario.Cities.Single().ProductionSites["logs"].DxKm = -39;
            scenario.Cities.Single().InitialStock["logs"] = 20;
            // À 0,5 par km, 45 km coûtent 23,5 par chargement, plus que ne vaut l'écart :
            // aucune charrette ne partirait. Le test parle du temps, pas du prix.
            scenario.Economy.Cartage.CostPerLoadKm = 0.05;
            var sim = new Simulation(scenario);
            var sawmill = sim.World.CityById("timberford").Industries.Single().Input("logs");

            sim.Step();
            var cart = sim.World.Cartage.Shipments.FirstOrDefault(c => c.CargoId == "logs" && ReferenceEquals(c.To, sawmill));
            Check.True(cart is not null, "une charrette de grumes doit partir vers la scierie");
            Check.Near(45, cart!.DistanceKm, 1e-9, "la forêt est à 45 km de la scierie");
            Check.Near(0, cart.TravelledKm, 1e-12, "partie aujourd'hui, elle n'a pas encore roulé");

            sim.Step();
            Check.Near(15, cart.TravelledKm, 1e-9, "un jour de route");
            sim.Step();
            Check.Near(30, cart.TravelledKm, 1e-9, "deux jours de route");
            Check.True(sim.World.Cartage.Shipments.Contains(cart), "encore en route");
            Check.Near(0, sim.World.Cartage.Delivered("logs"), 1e-12, "rien n'est livré avant l'arrivée");

            sim.Step();
            Check.True(!sim.World.Cartage.Shipments.Contains(cart), "arrivée au troisième jour");
            Check.True(sim.World.Cartage.Delivered("logs") > 0, "la charrette a livré la scierie");
        });

        runner.Add("charroi — tout peut passer par charrette, sans limite de quantité", () =>
        {
            // Sans train : tout ce que la scierie reçoit y vient par charrette, et elle
            // reçoit presque tout ce que la forêt produit.
            var scenario = ScenarioLoader.Load(TimberfordPath());
            scenario.Trains.Clear();
            var sim = new Simulation(scenario);
            sim.Run(Ticks);
            var city = sim.World.CityById("timberford");
            double produced = city.ProductionMarket("logs")!.TotalProduced;
            double delivered = sim.World.Cartage.Delivered("logs");
            Check.True(delivered > 0.8 * produced,
                $"{delivered:0} chargements de grumes livrés par charrette pour {produced:0} produits");
            Check.True(city.Industries.Single().Utilization > 0.5, "la scierie tourne sans train");
        });

        runner.Add("charroi — il s'arrête quand l'écart de prix ne paie plus la charrette", () =>
        {
            var scenario = ScenarioLoader.Load(TimberfordPath());
            scenario.Trains.Clear();
            scenario.Economy.Cartage.CostPerLoadKm = 100;
            var sim = new Simulation(scenario);
            sim.Run(100);
            Check.Near(0, sim.World.Cartage.Dispatched("logs"), 1e-12,
                "à 1 200 la charrette sur 12 km, aucune grume ne doit quitter la forêt");
        });

        runner.Add("captage — un train capte une charrette dans le rayon de sa gare, au prix qu'elle y coûte", () =>
        {
            // Une charrette de grumes posée à la main, à 1 km de la forêt sur la route
            // de la scierie, à 1 km par jour : elle reste dans le rayon de la gare de la
            // forêt (7 km). Le train part de la scierie et arrive à la forêt dans le
            // tick. Elle lui coûte son prix de départ plus le charroi parcouru :
            // 2 + 1 + 0,5 × 1 = 3,5, moins que la forêt, vide, qui vend à 4.
            var scenario = ScenarioLoader.Load(TimberfordPath());
            scenario.Economy.Cartage.SpeedKmPerTick = 1;
            scenario.Trains.Single().StartStop = 1;
            var sim = new Simulation(scenario);
            var city = sim.World.CityById("timberford");
            var cart = new CartShipment
            {
                CargoId = "logs", From = city.ProductionMarket("logs")!, To = city.Industries.Single().Input("logs"),
                DistanceKm = 12, SourcePrice = 2.0, Quantity = 5,
            };
            sim.World.Cartage.Shipments.Add(cart);
            double initial = Invariants.InitialStockTotal(sim.World) + 5;

            sim.Step();
            Check.Near(3.5, cart.PriceHere(scenario.Economy.Cartage), 1e-12, "prix à la gare : départ et charroi parcouru");
            Check.Near(0, cart.Quantity, 1e-12, "le train doit avoir chargé toute la charrette");
            Check.Near(5, sim.World.Cartage.Captured("logs"), 1e-12, "cinq chargements captés");
            var violations = Invariants.Check(sim.World, initial);
            Check.True(violations.Count == 0, violations.Count > 0 ? violations[0].Detail : "");
        });

        runner.Add("charroi et gares — des données absurdes sont refusées au chargement", () =>
        {
            var still = ScenarioLoader.Load(TimberfordPath());
            still.Economy.Cartage.SpeedKmPerTick = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(still), "une charrette qui n'avance pas");

            var empty = ScenarioLoader.Load(TimberfordPath());
            empty.Economy.Cartage.LoadsPerCart = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(empty), "une charrette sans chargement");

            var negative = ScenarioLoader.Load(TimberfordPath());
            negative.Economy.Cartage.CostPerLoadKm = -1;
            Check.Throws<InvalidDataException>(() => new Simulation(negative), "un charroi à coût négatif");

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
