using RailTycoon.Sim;
using RailTycoon.Sim.Economy;

namespace RailTycoon.Tests;

/// <summary>
/// Le prix chez un producteur qui ne consomme pas sa marchandise
/// (<c>HyperbolicPriceModel</c>, <c>priceModel.producerMaxMultiplier</c>). Décision
/// de l'équipe du 27 octobre 2026 : la quantité disponible à un endroit fait le prix,
/// là aussi — plus il y en a, moins c'est cher. L'entrepôt du producteur
/// (<c>economy.productionStorageTicks</c>) tient lieu de couverture : vide, la
/// marchandise vaut 0,5 × la référence ; plein, le plancher.
/// </summary>
internal static class ProducerPriceTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("prix producteur — plus il y en a dans l'entrepôt, moins c'est cher", () =>
        {
            // Une ferme qui produit un chargement par tick : 30 chargements d'entrepôt.
            // Le prix se lit à la construction du monde, avant tout échange : aucun
            // train, rien d'autre que le stock déclaré.
            double PriceAt(double stock)
            {
                var scenario = Fixtures.Bare("ferme");
                scenario.Cities.Add(Fixtures.City("ferme", production: 1.0, stock: stock));
                return new Simulation(scenario).World.CityById("ferme").ProductionMarket("grain")!.Price;
            }

            Check.Near(10 * 0.5, PriceAt(0), 1e-12, "entrepôt vide : le prix d'un producteur sollicité");
            Check.Near(10 * 0.12, PriceAt(30), 1e-12, "entrepôt plein : le plancher");
            Check.Near(10 * 0.12, PriceAt(90), 1e-12, "au-delà de l'entrepôt : toujours le plancher");
            Check.Near(10 * (0.5 + 0.12) / 2, PriceAt(15), 1e-12, "à moitié plein : à mi-chemin, en ligne droite");

            double previous = double.MaxValue;
            for (int stock = 0; stock <= 30; stock += 3)
            {
                double price = PriceAt(stock);
                Check.Less(price, previous + 1e-12, $"le prix doit baisser quand le stock monte, stock {stock}");
                previous = price;
            }
        });

        runner.Add("prix producteur — un train qui se sert au départ y fait monter le prix", () =>
        {
            // L'occasion se referme aussi au départ : chaque tranche achetée vide
            // l'entrepôt et renchérit la suivante. Le train charge à l'initialisation,
            // à la ferme, pour une ville qui manque de blé.
            var scenario = Fixtures.Bare("depart");
            scenario.StartingCash = 100_000;
            scenario.Cities.Add(Fixtures.City("ferme", production: 1.0, stock: 30));
            scenario.Cities.Add(Fixtures.City("ville", demand: 1.0));
            scenario.Lines.Add(new LineDef
            {
                Id = "l1", Name = "l1",
                Stops = { new StopDef { City = "ferme", DistanceKm = 0 }, new StopDef { City = "ville", DistanceKm = 50 } },
            });
            scenario.Trains.Add(new TrainDef { Id = "t1", Line = "l1", Capacity = 24 });

            var world = new Simulation(scenario).World;
            var farm = world.CityById("ferme").ProductionMarket("grain")!;
            Check.True(world.Trains[0].LoadedUnits > 1, $"le train doit avoir chargé, {world.Trains[0].LoadedUnits:0.0} chargements");
            Check.True(farm.Stock < 30, "l'entrepôt doit s'être vidé d'autant");
            Check.Near(10 * (0.5 - (0.5 - 0.12) * farm.Stock / 30), farm.Price, 1e-9,
                "le prix de départ doit suivre ce qui reste dans l'entrepôt");
            Check.True(farm.Price > 10 * 0.12 + 0.5, $"le départ doit être devenu plus cher que le plancher, {farm.Price:0.00}");
        });

        runner.Add("prix producteur — là où rien ne se produit ni ne se consomme, le plancher", () =>
        {
            // Ni entrepôt, ni couverture : rien ne dit si la marchandise abonde, et
            // le marché garde le prix de la première campagne, celui qui a corrigé le
            // blé livré là où personne n'en voulait.
            var scenario = Fixtures.Bare("desert");
            scenario.Cities.Add(Fixtures.City("ferme", production: 1.0));
            scenario.Cities.Add(Fixtures.City("desert"));
            var desert = new Simulation(scenario).World.CityById("desert").Market("grain");
            Check.Near(10 * 0.12, desert.Price, 1e-12, "prix d'un marché sans production ni demande");
        });

        runner.Add("prix producteur — un prix de producteur hors des bornes est refusé", () =>
        {
            foreach (double value in new[] { 0.05, 3.5 })
            {
                var scenario = Fixtures.Bare("bornes");
                scenario.Cities.Add(Fixtures.City("ferme", production: 1.0));
                scenario.PriceModel.ProducerMaxMultiplier = value;
                Check.Throws<InvalidDataException>(() => new Simulation(scenario), $"producerMaxMultiplier = {value}");
            }
        });
    }
}
