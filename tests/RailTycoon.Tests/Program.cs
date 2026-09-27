using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Telemetry;

namespace RailTycoon.Tests;

internal static class Program
{
    private static int Main()
    {
        var runner = new TestRunner();

        // ---------------------------------------------------------------- prix

        runner.Add("prix — le prix baisse quand le stock monte", () =>
        {
            var cfg = Fixtures.DefaultPriceModel();
            var model = new HyperbolicPriceModel(cfg);
            var cargo = Fixtures.Grain();

            double previous = double.MaxValue;
            for (double stock = 0; stock <= 200; stock += 10)
            {
                var market = new Market { CargoId = "grain", CityId = "c", Stock = stock, BaseDemandRate = 1.0 };
                double price = model.PriceFor(cargo, market);
                Check.True(price <= previous,
                    $"prix non monotone : stock {stock} donne {price:0.##} après {previous:0.##}");
                previous = price;
            }
        });

        runner.Add("prix — une couverture de 1 donne exactement le prix de référence", () =>
        {
            var cfg = Fixtures.DefaultPriceModel();
            var model = new HyperbolicPriceModel(cfg);
            var cargo = Fixtures.Grain();

            var market = new Market
            {
                CargoId = "grain",
                CityId = "c",
                BaseDemandRate = 1.0,
                Stock = 1.0 * cfg.CoverageHorizonTicks,
            };

            Check.Near(1.0, model.Coverage(market), 1e-9, "couverture");
            Check.Near(cargo.BasePrice, model.PriceFor(cargo, market), 1e-9, "prix de référence");
        });

        runner.Add("prix — les bornes sont respectées aux extrêmes", () =>
        {
            var cfg = Fixtures.DefaultPriceModel();
            var model = new HyperbolicPriceModel(cfg);
            var cargo = Fixtures.Grain();

            var empty = new Market { CargoId = "grain", CityId = "c", Stock = 0, BaseDemandRate = 1.0 };
            var glut = new Market { CargoId = "grain", CityId = "c", Stock = 1e6, BaseDemandRate = 1.0 };

            Check.Near(cargo.BasePrice * cfg.MaxMultiplier, model.PriceFor(cargo, empty), 1e-9, "prix en pénurie");
            Check.Near(cargo.BasePrice * cfg.MinMultiplier, model.PriceFor(cargo, glut), 1e-9, "prix en surabondance");
        });

        // ------------------------------------------------------------ économie

        runner.Add("économie — une usine sans intrants ne tourne pas", () =>
        {
            var scenario = Fixtures.Bare("mill-sans-ble");
            scenario.Cargos.Add(new CargoDef { Id = "flour", Name = "Farine", BasePrice = 22 });
            scenario.Recipes.Add(new RecipeDef
            {
                Id = "mill",
                Name = "Moulin",
                Inputs = { new IngredientDef { Cargo = "grain", Qty = 2 } },
                Outputs = { new IngredientDef { Cargo = "flour", Qty = 1 } },
            });
            var city = Fixtures.City("town");
            city.Industries.Add(new IndustryDef { Recipe = "mill", Capacity = 1 });
            scenario.Cities.Add(city);

            var sim = new Simulation(scenario);
            sim.Run(50);

            var industry = sim.World.CityById("town").Industries[0];
            Check.Near(0, industry.Utilization, 1e-9, "utilisation sans intrants");
            Check.Near(0, sim.World.CityById("town").Market("flour").TotalProduced, 1e-9, "farine produite");
        });

        runner.Add("économie — la consommation recule quand le prix monte", () =>
        {
            // Deux villes identiques, mais l'une est trois fois mieux
            // approvisionnée : son prix est plus bas, donc elle doit consommer
            // davantage. Aucune des deux n'est limitée par son stock.
            var scenario = Fixtures.Bare("elasticite");
            double horizon = scenario.PriceModel.CoverageHorizonTicks;
            scenario.Cities.Add(Fixtures.City("chere", demand: 0.5, stock: 0.5 * horizon));
            scenario.Cities.Add(Fixtures.City("bonmarche", demand: 0.5, stock: 3 * 0.5 * horizon));

            var sim = new Simulation(scenario);
            sim.Step();

            var expensive = sim.World.CityById("chere").Market("grain");
            var cheap = sim.World.CityById("bonmarche").Market("grain");

            Check.Less(cheap.Price, expensive.Price, "la ville bien approvisionnée doit être moins chère");
            Check.Less(expensive.ConsumedThisTick, cheap.ConsumedThisTick,
                "la ville la moins chère doit consommer davantage");
        });

        runner.Add("économie — la production primaire se bride en cas de surabondance", () =>
        {
            var scenario = Fixtures.Bare("saturation");
            scenario.Cities.Add(Fixtures.City("ferme", production: 1.0));

            var sim = new Simulation(scenario);
            var market = sim.World.CityById("ferme").Market("grain");

            sim.Step();
            double firstTick = market.ProducedThisTick;

            sim.Run(400);
            double lateTick = market.ProducedThisTick;

            Check.Near(1.0, firstTick, 1e-9, "production à plein régime au départ");
            Check.Less(lateTick, 0.5, "la production doit s'effondrer quand rien n'est écoulé");
        });

        // ------------------------------------------------------------ transport

        runner.Add("transport — livrer fait baisser le prix local", () =>
        {
            var scenario = Fixtures.Bare("arbitrage");
            scenario.Cities.Add(Fixtures.City("ferme", production: 2.0, stock: 200));
            scenario.Cities.Add(Fixtures.City("ville", demand: 0.1));
            scenario.Lines.Add(new LineDef
            {
                Id = "l1",
                Name = "l1",
                Stops =
                {
                    new StopDef { City = "ferme", DistanceKm = 0 },
                    new StopDef { City = "ville", DistanceKm = 100 },
                },
            });
            scenario.Trains.Add(new TrainDef
            {
                Id = "t1", Line = "l1", Capacity = 10, SpeedKmPerTick = 120, CostPerKm = 0.2,
            });

            var sim = new Simulation(scenario);
            double priceBefore = sim.World.CityById("ville").Market("grain").Price;
            Check.True(priceBefore > 0, "la ville doit avoir un prix initial");

            sim.Run(30);

            var town = sim.World.CityById("ville").Market("grain");
            Check.True(town.ImportedThisTick >= 0, "les imports doivent être comptés");
            Check.True(town.Stock > 0, "la ville doit avoir reçu du blé");
            Check.Less(town.Price, priceBefore, "le prix local doit avoir baissé après livraison");
            Check.True(sim.World.Company.TotalHaulRevenue > 0, "le transport doit avoir généré des recettes");
        });

        runner.Add("transport — la capacité des trains n'est jamais dépassée", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(Fixtures.HeartlandPath()));
            sim.Run(360);
            foreach (var train in sim.World.Trains)
                Check.True(train.LoadedUnits <= train.Capacity + 1e-6,
                    $"train {train.Id} : {train.LoadedUnits:0.##} > {train.Capacity:0.##}");
        });

        // -------------------------------------------------- régressions vécues
        // Les quatre cas suivants ont tous été observés en vrai sur le scénario de
        // référence, et aucun test existant ne les avait vus : la simulation
        // tournait, les invariants tenaient, et l'économie était morte. Un
        // invariant technique dit que rien n'est cassé, pas que le jeu fonctionne.

        runner.Add("régression — une ville ne revend pas ce qu'on vient de lui livrer", () =>
        {
            // Le transporteur déchargeait puis rachetait au même arrêt, encaissant
            // une marge à chaque tronçon sans jamais approvisionner personne.
            var market = new Market
            {
                CargoId = "grain", CityId = "ville",
                BaseDemandRate = 0.5,
                Stock = 0.5 * 30 * 0.8, // couverture 0,8 : en dessous de sa réserve
            };
            Check.Near(0, market.SellableStock(30, 1.0), 1e-9,
                "une ville sous sa réserve ne doit rien céder");

            market.Stock = 0.5 * 30 * 2.0; // couverture 2 : un excédent existe
            Check.Near(0.5 * 30, market.SellableStock(30, 1.0), 1e-9,
                "seul l'excédent au-delà de la réserve est cessible");

            var producer = new Market { CargoId = "grain", CityId = "ferme", Stock = 50 };
            Check.Near(50, producer.SellableStock(30, 1.0), 1e-9,
                "un site sans besoin local cède tout son stock");
        });

        runner.Add("régression — un train arrivant à un terminus en repart chargé", () =>
        {
            // Le sens de marche n'était retourné qu'après l'échange : au terminus,
            // le train ne voyait aucune destination devant lui et repartait vide.
            // La ferme à blé du scénario de référence est précisément au terminus.
            var scenario = Fixtures.Bare("terminus");
            scenario.Cities.Add(Fixtures.City("ferme", production: 2.0, stock: 100));
            scenario.Cities.Add(Fixtures.City("etape"));
            scenario.Cities.Add(Fixtures.City("ville", demand: 0.4));
            scenario.Lines.Add(new LineDef
            {
                Id = "l1", Name = "l1",
                Stops =
                {
                    new StopDef { City = "ferme", DistanceKm = 0 },
                    new StopDef { City = "etape", DistanceKm = 60 },
                    new StopDef { City = "ville", DistanceKm = 120 },
                },
            });
            // Le train démarre au terminus opposé : il atteindra donc « ferme » en
            // roulant vers l'origine, ce qui est exactement le cas qui échouait.
            scenario.Trains.Add(new TrainDef
            {
                Id = "t1", Line = "l1", Capacity = 10,
                SpeedKmPerTick = 60, CostPerKm = 0.1, StartStop = 2,
            });

            var sim = new Simulation(scenario);
            sim.Run(40);

            Check.True(sim.World.CityById("ville").Market("grain").Stock > 0,
                "la ville doit avoir reçu du blé chargé au terminus");
        });

        runner.Add("régression — une usine tourne même si sa production n'a aucun acheteur local", () =>
        {
            // Un moulin ne consomme pas sa farine : chez lui, elle est au prix
            // plancher. Valoriser sa production au prix local revenait à conclure
            // que moudre n'est jamais rentable, et toutes les usines s'arrêtaient.
            var scenario = Fixtures.Bare("moulin-sans-client-local");
            scenario.Cargos.Add(new CargoDef { Id = "flour", Name = "Farine", BasePrice = 34 });
            scenario.Recipes.Add(new RecipeDef
            {
                Id = "mill", Name = "Moulin",
                Inputs = { new IngredientDef { Cargo = "grain", Qty = 2 } },
                Outputs = { new IngredientDef { Cargo = "flour", Qty = 1 } },
            });

            var city = Fixtures.City("millbrook", stock: 400);
            city.Industries.Add(new IndustryDef { Recipe = "mill", Capacity = 1 });
            scenario.Cities.Add(city);

            var sim = new Simulation(scenario);
            sim.Step();

            var mill = sim.World.CityById("millbrook").Industries[0];
            Check.True(mill.Utilization > 0.9,
                $"le moulin doit tourner avec du blé abondant, utilisation = {mill.Utilization:0.00}");
            Check.Near(0.12 * 34, sim.World.CityById("millbrook").Market("flour").Price, 1e-6,
                "la farine reste au plancher chez le moulin, et c'est normal");
        });

        runner.Add("régression — l'économie de référence reste vivante", () =>
        {
            // LE test qui aurait détecté les trois bugs précédents d'un coup.
            // Il ne vérifie pas une implémentation, il vérifie que le jeu marche :
            // les usines tournent, les prix portent de l'information, et le
            // transport gagne de l'argent sans en fabriquer.
            var sim = new Simulation(ScenarioLoader.Load(Fixtures.HeartlandPath()));
            var stats = new RunStatistics { WarmupTicks = 90 };
            for (int i = 0; i < 720; i++)
            {
                sim.Step();
                stats.Sample(sim.World);
            }

            var w = sim.World;

            int running = 0, total = 0;
            foreach (var city in w.Cities)
                foreach (var industry in city.Industries)
                {
                    total++;
                    if (stats.MeanUtilization(city.Id, industry.Recipe.Id) > 0.5) running++;
                }
            Check.True(running >= total - 1,
                $"au plus une usine peut rester à l'arrêt ; {total - running} sur {total} le sont");

            foreach (string cargoId in w.CargoOrder)
            {
                double ceiling = stats.MeanCeilingFraction(w, cargoId);
                Check.True(ceiling <= 0.4,
                    $"{w.Cargo(cargoId).Name} : {ceiling * 100:0} % du temps au plafond — " +
                    "le prix ne porte plus d'information");
            }

            // Sentinelle contre le lavage de fret. Une marge kilométrique
            // invraisemblable ne signale pas un transporteur brillant mais de
            // l'argent créé par une faille : le bug du rachat immédiat affichait
            // 7,79 par kilomètre pendant que toutes les usines étaient arrêtées.
            double km = w.Trains.Sum(t => t.TotalKmTravelled);
            double marginPerKm = w.Company.NetProfit / km;
            Check.True(marginPerKm > 0,
                $"le transport doit être rentable, marge = {marginPerKm:0.00}/km");
            Check.Less(marginPerKm, 3.0,
                $"marge de {marginPerKm:0.00}/km invraisemblable — chercher une faille d'arbitrage");
        });

        runner.Add("régression — le scénario de référence est équilibré offre/demande", () =>
        {
            // Une pénurie structurelle ne ressemble pas à un bug : tout tourne, et
            // pourtant les marchés concernés restent collés au plafond à jamais.
            var scenario = ScenarioLoader.Load(Fixtures.HeartlandPath());
            foreach (var b in BalanceReport.Compute(scenario))
            {
                Check.True(b.Ratio >= 0.95 && b.Ratio <= 1.5,
                    $"{b.CargoName} : offre/demande = {b.Ratio:0.00} ({b.Verdict})");
            }
        });

        // ----------------------------------------------------------- invariants

        runner.Add("invariants — 720 ticks sur le scénario de référence", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(Fixtures.HeartlandPath()));
            double initial = Invariants.InitialStockTotal(sim.World);

            for (int i = 0; i < 720; i++)
            {
                sim.Step();
                var violations = Invariants.Check(sim.World, initial);
                if (violations.Count > 0)
                    Check.True(false,
                        $"tick {sim.World.Tick.Index} : {violations[0].Rule} — {violations[0].Detail}");
            }
        });

        runner.Add("déterminisme — deux exécutions donnent la même trace", () =>
        {
            string Fingerprint()
            {
                var sim = new Simulation(ScenarioLoader.Load(Fixtures.HeartlandPath()));
                var recorder = new CsvRecorder();
                recorder.Record(sim.World);
                for (int i = 0; i < 240; i++)
                {
                    sim.Step();
                    recorder.Record(sim.World);
                }
                return recorder.Fingerprint();
            }

            Check.Equal(Fingerprint(), Fingerprint(), "empreinte de la trace");
        });

        // ----------------------------------------------------------- validation

        runner.Add("validation — distances d'arrêts non croissantes rejetées", () =>
        {
            var scenario = Fixtures.Bare("mauvaise-ligne");
            scenario.Cities.Add(Fixtures.City("a"));
            scenario.Cities.Add(Fixtures.City("b"));
            scenario.Lines.Add(new LineDef
            {
                Id = "l1", Name = "l1",
                Stops =
                {
                    new StopDef { City = "a", DistanceKm = 100 },
                    new StopDef { City = "b", DistanceKm = 100 },
                },
            });

            Check.Throws<InvalidDataException>(() => new Simulation(scenario),
                "une ligne aux distances non croissantes doit être refusée");
        });

        runner.Add("validation — référence à une marchandise inconnue rejetée", () =>
        {
            var scenario = Fixtures.Bare("mauvaise-recette");
            scenario.Recipes.Add(new RecipeDef
            {
                Id = "mill", Name = "Moulin",
                Inputs = { new IngredientDef { Cargo = "grain", Qty = 2 } },
                Outputs = { new IngredientDef { Cargo = "fantome", Qty = 1 } },
            });

            Check.Throws<InvalidDataException>(() => new Simulation(scenario),
                "une recette référençant une marchandise inconnue doit être refusée");
        });

        runner.Add("validation — le scénario de référence se charge sans erreur", () =>
        {
            var scenario = ScenarioLoader.Load(Fixtures.HeartlandPath());
            Check.True(scenario.Cargos.Count == 6, $"6 marchandises attendues, {scenario.Cargos.Count} trouvées");
            Check.True(scenario.Cities.Count == 10, $"10 villes attendues, {scenario.Cities.Count} trouvées");
            Check.True(scenario.Recipes.Count == 3, $"3 recettes attendues, {scenario.Recipes.Count} trouvées");
        });

        // Les invariants du module finance vivent dans leur propre fichier :
        // société, emprunts, bourse, insolvabilité. Un seul point d'entrée ici,
        // pour que trois modules qui avancent en parallèle n'entrent pas en
        // conflit sur cette liste.
        FinanceTests.Register(runner);

        Console.WriteLine("RailTycoon — invariants de simulation");
        Console.WriteLine();
        return runner.Run();
    }
}
