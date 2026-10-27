using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Network;

namespace RailTycoon.Tests;

/// <summary>
/// Les routes des charrettes. Décision de l'équipe du 27 octobre 2026 : les
/// charrettes suivent le terrain — elles longent les fleuves et les vallées, passent
/// les cols, contournent ce qui est trop raide, et ne franchissent un fleuve qu'à gué.
/// Les cartes sont posées ici, petites et lisibles : 21 km sur 11, une maille par
/// kilomètre, deux villes à 16 km l'une de l'autre sur la même latitude.
/// </summary>
internal static class CartRouteTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("route — sans relief, une charrette va en ligne droite", () =>
        {
            var world = new Simulation(ScenarioLoader.Load(Path.Combine(Fixtures.RepoRoot(), "data", "timberford.json"))).World;
            var city = world.CityById("timberford");
            var route = Cartage.Route(world, city.ProductionMarket("logs")!, city.Industries.Single().Input("logs"));
            Check.Near(12, route.MapKm, 1e-9, "12 km de la forêt à la scierie");
            Check.Near(12, route.LengthKm, 1e-9, "ni montée ni gué sur une carte sans relief");
        });

        runner.Add("route — une charrette contourne une pente trop raide par le col", () =>
        {
            // Une crête nord-sud de 800 m entre les deux villes, qui s'arrête au nord :
            // ses flancs dépassent de loin les 10 % qu'une charrette chargée gravit. La
            // route doit passer au bout de la crête, pas par-dessus.
            var (world, west, east) = Map(new TerrainFeatureDef
            {
                Kind = "ridge", AmplitudeM = 800, HalfWidthKm = 2,
                X1Km = 10, Y1Km = -2, X2Km = 10, Y2Km = 7,
            });
            var route = Cartage.Route(world, west, east);
            Check.True(route.Passable, "une route doit exister par le nord");
            Check.True(route.Points.Any(p => p.YKm >= 8.5), "la route doit passer au bout de la crête, au nord");
            Check.True(route.MapKm > 17, $"le détour doit allonger la route, {route.MapKm:0.0} km sur la carte");
        });

        runner.Add("route — une montée allonge la route, une descente non", () =>
        {
            // Une butte de 150 m, douce : ses flancs restent sous 10 %. La route la
            // passe ou la contourne, mais paie ce qu'elle gravit.
            var (world, west, east) = Map(new TerrainFeatureDef
            {
                Kind = "bowl", AmplitudeM = 150, HalfWidthKm = 5, XKm = 10, YKm = 5,
            });
            var route = Cartage.Route(world, west, east);
            Check.True(route.LengthKm > route.MapKm, "la route doit compter plus que la carte");

            // Sur un relief en pente régulière, aller vers le haut coûte plus que
            // revenir vers le bas, de ce que vaut la montée exactement.
            var (slope, low, high) = Map(new TerrainFeatureDef
            {
                Kind = "ridge", AmplitudeM = 60, HalfWidthKm = 30, X1Km = 30, Y1Km = -10, X2Km = 30, Y2Km = 20,
            });
            var up = Cartage.Route(slope, low, high);
            var down = Cartage.Route(slope, high, low);
            Check.Less(down.LengthKm, up.LengthKm, "la montée doit coûter plus que la descente");
            Check.True(down.LengthKm <= down.MapKm + 1e-9, "la descente ne coûte rien de plus que sa longueur");
        });

        runner.Add("route — un fleuve ne se franchit qu'une fois, et se longe sinon", () =>
        {
            // Un fleuve nord-sud entre les deux villes, dans sa vallée.
            var river = new TerrainFeatureDef
            {
                Kind = "river", AmplitudeM = -20, HalfWidthKm = 2,
                Points = { new MapPointDef { XKm = 10, YKm = -1 }, new MapPointDef { XKm = 11, YKm = 5 }, new MapPointDef { XKm = 10, YKm = 11 } },
            };
            var (world, west, east) = Map(river);
            var cfg = world.Def.Economy.Cartage;
            var course = world.Terrain!.Rivers.Single();

            int Crossings(CartRoute route)
            {
                int count = 0;
                for (int i = 1; i < route.Points.Count; i++)
                    for (int j = 1; j < course.Count; j++)
                        if (TerrainFactory.SegmentsIntersect(route.Points[i - 1], route.Points[i], course[j - 1], course[j])) count++;
                return count;
            }

            var across = Cartage.Route(world, west, east);
            int crossings = Crossings(across);
            Check.True(crossings == 1, $"d'une rive à l'autre, un seul gué ; {crossings} passages");
            Check.True(across.LengthKm >= across.MapKm + cfg.RiverCrossingKm - 1e-9, "le gué doit se payer");

            // Deux marchés de la même rive, la rive est : à x = 10,6, le fleuve passe à
            // l'ouest d'eux au nord et au sud (x = 10,33), mais à l'est au milieu de son
            // méandre (x = 11). La ligne droite le couperait deux fois ; la route le
            // longe sans le franchir.
            var (sameBank, north, south) = Map(river, west: (10.6, 9), east: (10.6, 1));
            var straight = CartRoute.Straight(new GeoPoint(10.6, 9), new GeoPoint(10.6, 1));
            Check.True(Crossings(straight) == 2, "la ligne droite doit couper le méandre deux fois");
            int alongside = Crossings(Cartage.Route(sameBank, north, south));
            Check.True(alongside == 0, $"sur la même rive, aucun gué ; {alongside} passages");
        });

        runner.Add("route — une charrette avance sur sa route, pas en ligne droite", () =>
        {
            var (world, west, east) = Map(new TerrainFeatureDef
            {
                Kind = "ridge", AmplitudeM = 800, HalfWidthKm = 2,
                X1Km = 10, Y1Km = -2, X2Km = 10, Y2Km = 7,
            });
            var route = Cartage.Route(world, west, east);
            Check.Near(2, route.PointAt(0).XKm, 1e-9, "au départ, la charrette est à l'ouest");
            Check.Near(18, route.PointAt(route.LengthKm).XKm, 1e-9, "à l'arrivée, à l'est");
            double highest = Enumerable.Range(0, 101).Max(i => route.PointAt(route.LengthKm * i / 100).YKm);
            Check.True(highest >= 8.5, $"en route, elle doit monter au nord pour passer la crête, y max {highest:0.0}");
        });

        runner.Add("route — un fleuve sans cours ou une pente nulle sont refusés", () =>
        {
            var scenario = Scenario(new TerrainFeatureDef { Kind = "river", AmplitudeM = -10, HalfWidthKm = 1, Points = { new MapPointDef { XKm = 1, YKm = 1 } } }, (2, 5), (18, 5));
            Check.Throws<InvalidDataException>(() => new Simulation(scenario), "un fleuve d'un seul point");

            var flat = Scenario(null, (2, 5), (18, 5));
            flat.Economy.Cartage.MaxGradePercent = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(flat), "une pente maximale nulle");
        });
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
