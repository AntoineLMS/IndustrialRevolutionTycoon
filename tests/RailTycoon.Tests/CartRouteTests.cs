using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Network;

namespace RailTycoon.Tests;

/// <summary>
/// Les routes de ce qui voyage sans train. Décisions de l'équipe des 27 et 28 octobre
/// 2026 : les charrettes suivent le terrain — elles longent les vallées, passent les
/// cols, contournent ce qui est trop raide —, ne franchissent un fleuve qu'à un point
/// de passage — gué, pont, bac ou pont du chemin de fer —, et les fleuves portent des
/// bateaux de port en port. Les cartes sont posées ici, petites et lisibles : 21 km sur
/// 11, une maille par kilomètre, deux villes sur la même latitude.
/// </summary>
internal static class CartRouteTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("route — sans relief, une charrette va en ligne droite", () =>
        {
            // Timberford sans sa carte : la route est la ligne droite.
            var scenario = ScenarioLoader.Load(Path.Combine(Fixtures.RepoRoot(), "data", "timberford.json"));
            scenario.Network.Terrain = null;
            var world = new Simulation(scenario).World;
            var city = world.CityById("timberford");
            var cfg = world.Def.Economy.Cartage;
            var route = Cartage.Route(world, city.ProductionMarket("logs")!, city.Industries.Single().Input("logs"));
            Check.Near(12, route.MapKm, 1e-9, "12 km de la forêt à la scierie");
            Check.Near(12 * cfg.CostPerLoadKm, route.TotalCost, 1e-9, "ni montée ni passage sur une carte sans relief");
            Check.Near(12 / cfg.SpeedKmPerTick, route.TotalDays, 1e-9, "à 15 km par jour");
        });

        runner.Add("route — une charrette contourne une pente trop raide par le col", () =>
        {
            // Une crête nord-sud de 800 m entre les deux villes, qui s'arrête au nord :
            // ses flancs dépassent de loin les 10 % qu'une charrette chargée gravit. La
            // route doit passer au bout de la crête, pas par-dessus.
            var (world, west, east) = Map(Ridge());
            var route = Cartage.Route(world, west, east);
            Check.True(route.Passable, "une route doit exister par le nord");
            Check.True(route.Points.Any(p => p.YKm >= 8.5), "la route doit passer au bout de la crête, au nord");
            Check.True(route.MapKm > 17, $"le détour doit allonger la route, {route.MapKm:0.0} km sur la carte");
        });

        runner.Add("route — une montée allonge la route, une descente non", () =>
        {
            var (world, west, east) = Map(new TerrainFeatureDef
            {
                Kind = "bowl", AmplitudeM = 150, HalfWidthKm = 5, XKm = 10, YKm = 5,
            });
            var cfg = world.Def.Economy.Cartage;
            var route = Cartage.Route(world, west, east);
            Check.True(route.TotalCost > cfg.CostPerLoadKm * route.MapKm, "la butte doit coûter plus que sa longueur");

            // Sur une pente régulière, monter coûte plus que descendre ; descendre ne
            // coûte que la longueur.
            var (slope, low, high) = Map(new TerrainFeatureDef
            {
                Kind = "ridge", AmplitudeM = 60, HalfWidthKm = 30, X1Km = 30, Y1Km = -10, X2Km = 30, Y2Km = 20,
            });
            var up = Cartage.Route(slope, low, high);
            var down = Cartage.Route(slope, high, low);
            Check.Less(down.TotalCost, up.TotalCost, "la montée doit coûter plus que la descente");
            Check.True(down.TotalCost <= cfg.CostPerLoadKm * down.MapKm + 1e-9, "la descente ne coûte que sa longueur");
            Check.Less(down.TotalDays, up.TotalDays, "et la montée prend plus de temps");
        });

        runner.Add("passage — un fleuve sans point de passage ne se franchit pas", () =>
        {
            // Le fleuve traverse toute la carte, du sud au nord : sans passage, et sans
            // source à contourner, aucune charrette ne va d'une rive à l'autre.
            var (world, west, east) = Map(River());
            Check.True(!Cartage.Route(world, west, east).Passable, "aucune route d'une rive à l'autre");
            Check.True(double.IsInfinity(Cartage.Cost(world, west, east)), "un coût infini : aucune charrette ne part");
        });

        runner.Add("passage — la route converge vers le passage le moins cher, au prix d'un détour", () =>
        {
            // Un gué en face des deux villes, et un pont 5 km plus au sud. Le gué vaut
            // 5 km de route, le pont 0,5 : le détour par le pont (un peu moins de 3 km)
            // coûte moins que le gué.
            var river = River(
                new RiverCrossingDef { Name = "gué", XKm = 11, YKm = 5, Kind = "ford" },
                new RiverCrossingDef { Name = "pont", XKm = 10 + 1.0 / 6, YKm = 0, Kind = "bridge" });
            var (world, west, east) = Map(river);
            var route = Cartage.Route(world, west, east);
            var course = world.Terrain!.Rivers.Single();
            Check.True(Crossings(route, course) == 1, "d'une rive à l'autre, un seul passage");
            var where = route.Points.Zip(route.Points.Skip(1)).SelectMany(s => course.Intersections(s.First, s.Second)).Single();
            Check.Less((where - new GeoPoint(10 + 1.0 / 6, 0)).Norm, 1.0 + 1e-9, $"la route passe au pont, en ({where.XKm:0.0}, {where.YKm:0.0})");

            // Sans le pont, elle prend le gué, et le paie.
            var fordOnly = River(new RiverCrossingDef { Name = "gué", XKm = 11, YKm = 5, Kind = "ford" });
            var (w2, a2, b2) = Map(fordOnly);
            var viaFord = Cartage.Route(w2, a2, b2);
            var cfg = w2.Def.Economy.Cartage;
            Check.True(viaFord.Passable, "le gué doit laisser passer");
            Check.True(viaFord.TotalCost >= cfg.CostPerLoadKm * (viaFord.MapKm + cfg.FordKm) - 1e-9, "le gué doit se payer");
        });

        runner.Add("passage — sur la même rive, la route longe le fleuve sans le franchir", () =>
        {
            // À x = 10,6, le fleuve passe à l'ouest au nord et au sud (x = 10,33), mais à
            // l'est au milieu de son méandre (x = 11). La ligne droite le couperait deux
            // fois ; la route le longe, même avec un gué à portée.
            var river = River(new RiverCrossingDef { Name = "gué", XKm = 11, YKm = 5, Kind = "ford" });
            var (world, north, south) = Map(river, west: (10.6, 9), east: (10.6, 1));
            var course = world.Terrain!.Rivers.Single();
            var straight = CartRoute.Straight(new GeoPoint(10.6, 9), new GeoPoint(10.6, 1), world.Def.Economy.Cartage);
            Check.True(Crossings(straight, course) == 2, "la ligne droite doit couper le méandre deux fois");
            int alongside = Crossings(Cartage.Route(world, north, south), course);
            Check.True(alongside == 0, $"sur la même rive, aucun passage ; {alongside}");
        });

        runner.Add("passage — un pont du chemin de fer est un point de passage, et la voie le construit", () =>
        {
            // Une voie droite d'ouest en est coupe le fleuve, qui n'a aucun passage
            // déclaré : le géomètre y jette un pont, et les charrettes l'empruntent.
            var scenario = Scenario(River(), (2, 5), (18, 5));
            scenario.Network.Nodes.Add(new TrackNodeDef { Id = "o", City = "ouest", XKm = 2, YKm = 5 });
            scenario.Network.Nodes.Add(new TrackNodeDef { Id = "e", City = "est", XKm = 18, YKm = 5 });
            scenario.Network.Edges.Add(new TrackEdgeDef { Id = "o-e", From = "o", To = "e" });
            scenario.Network.Routes.Add(new TrackRouteDef { Id = "ligne", Name = "ligne", Nodes = { "o", "e" } });
            var world = new Simulation(scenario).World;

            var edge = world.Network!.Graph.Edges.Single();
            Check.True(edge.Construction.Structures.Any(s => s.Kind == StructureKind.Bridge),
                "la voie doit franchir le fleuve sur un pont");
            var bridge = Cartage.Router(world).Crossings.Single().Single();
            Check.True(bridge.Kind == CrossingKind.Bridge && Math.Abs(bridge.Point.XKm - 11) < 0.2,
                $"le pont du chemin de fer est le passage, en ({bridge.Point.XKm:0.00}, {bridge.Point.YKm:0.00})");
            Check.True(Cartage.Route(world, world.CityById("ouest").Market("grain"), world.CityById("est").Market("grain")).Passable,
                "les charrettes passent le fleuve par le pont du chemin de fer");
        });

        runner.Add("eau — un fleuve navigable porte des bateaux de port en port, vite à l'aval, lentement à l'amont", () =>
        {
            // Un fleuve d'ouest en est, au nord des deux villes, avec un port en face de
            // chacune. À 0,02 par km et 1 par chargement ou déchargement, le bateau coûte
            // bien moins que 16 km de charrette (8).
            var (world, west, east) = Map(Waterway(), west: (2, 2), east: (18, 2));
            var downstream = Cartage.Route(world, west, east);
            var upstream = Cartage.Route(world, east, west);
            var water = world.Def.Economy.Waterways;
            Check.True(downstream.UsesWater, "la route doit prendre le fleuve");
            Check.Near(16, downstream.WaterKm, 1e-9, "seize kilomètres de fleuve, d'un port à l'autre");
            Check.Less(downstream.TotalCost, world.Def.Economy.Cartage.CostPerLoadKm * 16, "le bateau doit coûter moins que la charrette");
            Check.True(downstream.TotalCost >= 2 * water.CostPerLoad + water.CostPerLoadKm * 16 - 1e-9,
                "chargement et déchargement se paient");
            Check.Near(downstream.TotalCost, upstream.TotalCost, 1e-9, "le prix ne dépend pas du sens");
            Check.Less(downstream.TotalDays + 0.5, upstream.TotalDays, "remonter le fleuve prend plus longtemps");
        });

        runner.Add("eau — des bateaux partent, livrent, et suivent le fleuve", () =>
        {
            // Une ferme à l'ouest, des acheteurs à l'est, aucun train : le blé descend le
            // fleuve.
            var scenario = Scenario(Waterway(), (2, 2), (18, 2));
            scenario.Cities[0].Demand.Clear();
            scenario.Cities[0].Production["grain"] = 2.0;
            scenario.Cities[1].Demand["grain"] = 1.0;
            var sim = new Simulation(scenario);

            CartShipment? boat = null;
            double initial = RailTycoon.Sim.Telemetry.Invariants.InitialStockTotal(sim.World);
            for (int i = 0; i < 60; i++)
            {
                sim.Step();
                boat ??= sim.World.Cartage.Shipments.FirstOrDefault(s => s.Route.UsesWater);
                var violations = RailTycoon.Sim.Telemetry.Invariants.Check(sim.World, initial);
                Check.True(violations.Count == 0, violations.Count > 0 ? violations[0].Detail : "");
            }
            Check.True(sim.World.Cartage.DispatchedByWater("grain") > 0, "le blé doit partir par le fleuve");
            Check.True(sim.World.Cartage.Delivered("grain") > 0, "et arriver");

            // À mi-traversée — 0,4 jour à la descente, entre deux ticks ici —, le
            // chargement est sur le bateau, et le bateau sur le fleuve.
            var route = boat!.Route;
            int leg = Enumerable.Range(0, route.ByWater.Count).First(i => route.ByWater[i]);
            int last = Enumerable.Range(0, route.ByWater.Count).Last(i => route.ByWater[i]);
            boat.TravelledDays = (route.Days[leg] + route.Days[last + 1]) / 2;
            Check.True(boat.OnWater, "à mi-traversée, sur l'eau");
            Check.Near(4.4, boat.Position.YKm, 1e-9, "sur le fleuve");
        });

        runner.Add("fleuve — des données absurdes sont refusées au chargement", () =>
        {
            var single = Scenario(new TerrainFeatureDef { Kind = "river", AmplitudeM = -10, HalfWidthKm = 1, Points = { new MapPointDef { XKm = 1, YKm = 1 } } }, (2, 5), (18, 5));
            Check.Throws<InvalidDataException>(() => new Simulation(single), "un fleuve d'un seul point");

            var dry = Scenario(River(new RiverCrossingDef { Name = "gué perdu", XKm = 15, YKm = 5 }), (2, 5), (18, 5));
            Check.Throws<InvalidDataException>(() => new Simulation(dry), "un gué loin de l'eau");

            var odd = Scenario(River(new RiverCrossingDef { Name = "pont-levis", XKm = 11, YKm = 5, Kind = "drawbridge" }), (2, 5), (18, 5));
            Check.Throws<InvalidDataException>(() => new Simulation(odd), "une sorte de passage inconnue");

            var waterway = Waterway();
            waterway.Ports.Add(new RiverPortDef { Name = "port sec", XKm = 10, YKm = 9 });
            Check.Throws<InvalidDataException>(() => new Simulation(Scenario(waterway, (2, 2), (18, 2))), "un port loin de l'eau");

            var flat = Scenario(null, (2, 5), (18, 5));
            flat.Economy.Cartage.MaxGradePercent = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(flat), "une pente maximale nulle");

            var still = Scenario(null, (2, 5), (18, 5));
            still.Economy.Waterways.UpstreamKmPerTick = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(still), "un bateau qui ne remonte pas");
        });
    }

    private static TerrainFeatureDef Ridge() => new()
    {
        Kind = "ridge", AmplitudeM = 800, HalfWidthKm = 2,
        X1Km = 10, Y1Km = -2, X2Km = 10, Y2Km = 7,
    };

    /// <summary>Un fleuve du sud au nord, qui traverse toute la carte avec un méandre vers l'est.</summary>
    private static TerrainFeatureDef River(params RiverCrossingDef[] crossings)
    {
        var river = new TerrainFeatureDef
        {
            Kind = "river", Name = "la Rivière", AmplitudeM = -20, HalfWidthKm = 2,
            Points = { new MapPointDef { XKm = 10, YKm = -1 }, new MapPointDef { XKm = 11, YKm = 5 }, new MapPointDef { XKm = 10, YKm = 11 } },
        };
        river.Crossings.AddRange(crossings);
        return river;
    }

    /// <summary>Un fleuve d'ouest en est à y = 4,4, entre deux rangées de la grille, avec deux ports.</summary>
    private static TerrainFeatureDef Waterway() => new()
    {
        Kind = "river", Name = "le Canal", AmplitudeM = -5, HalfWidthKm = 1,
        Points = { new MapPointDef { XKm = 0, YKm = 4.4 }, new MapPointDef { XKm = 20, YKm = 4.4 } },
        Ports = { new RiverPortDef { Name = "amont", XKm = 2, YKm = 4.4 }, new RiverPortDef { Name = "aval", XKm = 18, YKm = 4.4 } },
    };

    private static int Crossings(CartRoute route, River course)
    {
        int count = 0;
        for (int i = 1; i < route.Points.Count; i++)
            count += course.Intersections(route.Points[i - 1], route.Points[i]).Count();
        return count;
    }

    /// <summary>
    /// Une carte de 21 km sur 11 à 100 m d'altitude, une forme de relief, deux villes
    /// qui consomment du blé. Rend le monde et les deux places de marché.
    /// </summary>
    private static (WorldState World, Market West, Market East) Map(
        TerrainFeatureDef? feature, (double X, double Y)? west = null, (double X, double Y)? east = null)
    {
        var world = new Simulation(Scenario(feature, west ?? (2, 5), east ?? (18, 5))).World;
        return (world, world.CityById("ouest").Market("grain"), world.CityById("est").Market("grain"));
    }

    private static ScenarioDef Scenario(TerrainFeatureDef? feature, (double X, double Y) west, (double X, double Y) east)
    {
        var scenario = Fixtures.Bare("routes");
        var a = Fixtures.City("ouest", demand: 0.1);
        (a.XKm, a.YKm) = (west.X, west.Y);
        var b = Fixtures.City("est", demand: 0.1);
        (b.XKm, b.YKm) = (east.X, east.Y);
        scenario.Cities.Add(a);
        scenario.Cities.Add(b);
        scenario.Network.Terrain = new TerrainDef { Columns = 21, Rows = 11, CellSizeKm = 1, BaseElevationM = 100 };
        if (feature is not null) scenario.Network.Terrain.Features.Add(feature);
        return scenario;
    }
}
