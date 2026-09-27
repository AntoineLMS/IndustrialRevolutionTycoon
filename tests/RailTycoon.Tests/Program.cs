using RailTycoon.Sim;
using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Network;
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

        // Le déterminisme se vérifie contre des empreintes figées, pas en comparant
        // deux exécutions du même processus : voir ReferenceTraceTests.

        // ------------------------------------------- économie anticipante
        // Le solveur anticipant ne remplace pas la référence : elle reste le témoin
        // auquel on le compare. Ces cas vérifient donc deux choses distinctes —
        // que ses mécanismes font ce qu'ils prétendent, et qu'il bat effectivement
        // la référence sur le critère du contrat sans dégrader le reste.

        runner.Add("anticipation — configuration neutre, trace identique à la référence", () =>
        {
            // La propriété qui rend toutes les mesures suivantes interprétables :
            // à réglages neutres, le solveur anticipant EST la référence. Sans elle,
            // une différence mesurée pourrait venir d'une divergence
            // d'implémentation au lieu du mécanisme qu'on croit mesurer, et aucune
            // comparaison ne voudrait rien dire.
            string Fingerprint(IEconomySolver solver)
            {
                var scenario = ScenarioLoader.Load(Fixtures.HeartlandPath());
                scenario.Anticipating = new AnticipatingEconomyDef();
                var sim = new Simulation(scenario, solver);
                var recorder = new CsvRecorder();
                recorder.Record(sim.World);
                for (int i = 0; i < 240; i++)
                {
                    sim.Step();
                    recorder.Record(sim.World);
                }
                return recorder.Fingerprint();
            }

            Check.Equal(
                Fingerprint(new ReferenceEconomySolver()),
                Fingerprint(new AnticipatingEconomySolver()),
                "empreinte à configuration neutre");
        });

        runner.Add("anticipation — un stock qui fond renchérit avant la pénurie", () =>
        {
            // Le mécanisme central. Un marché dont le stock fond doit valoir plus
            // cher que le même marché au même stock mais qui se remplit : c'est ce
            // qui crée des vagues de prix au lieu d'un régime stationnaire.
            //
            // La mesure se prend au deuxième tick, dès que la pente est connue et
            // avant que les deux trajectoires n'aient eu le temps de s'écarter. Plus
            // tard, l'effet se retourne pour une raison qui n'est pas un défaut mais
            // qu'il faut connaître : un prix anticipé plus bas fait consommer
            // davantage, donc vide le stock, donc renchérit le marché quelques ticks
            // après. L'anticipation ne déplace pas seulement le prix, elle déplace le
            // stock — c'est précisément la boucle qu'on cherchait, et c'est aussi
            // pourquoi on ne peut pas la tester sur un état final.
            double PriceAfter(double weight, double production, double coverage)
            {
                var scenario = Fixtures.Bare("anticipation");
                double horizon = scenario.PriceModel.CoverageHorizonTicks;
                scenario.Cities.Add(Fixtures.City(
                    "ville", demand: 0.5, production: production, stock: coverage * 0.5 * horizon));
                scenario.Anticipating = new AnticipatingEconomyDef
                {
                    AnticipationTicks = 6,
                    AnticipationWeight = weight,
                    DriftSmoothing = 1.0,
                };

                var sim = new Simulation(scenario, new AnticipatingEconomySolver());
                sim.Run(2);
                return sim.World.CityById("ville").Market("grain").Price;
            }

            // Un stock qui fond : aucune production, une couverture de 2 qui se vide.
            Check.Less(PriceAfter(0.0, 0.0, 2.0), PriceAfter(1.0, 0.0, 2.0),
                "un stock qui fond doit être plus cher avec anticipation que sans");

            // Un stock qui se remplit : une production de 2 pour une consommation de
            // 0,5, et un entrepôt à un quart de sa capacité pour que le frein
            // d'encombrement ne l'arrête pas — première version du test, l'entrepôt
            // démarrait exactement plein, la production tombait à son débit résiduel
            // et les deux cas se vidaient.
            Check.Less(PriceAfter(1.0, 2.0, 1.0), PriceAfter(0.0, 2.0, 1.0),
                "un stock qui s'accumule doit être moins cher avec anticipation que sans");
        });

        runner.Add("anticipation — la saison est périodique et pilotée par le seul tick", () =>
        {
            // Une saison qui dériverait d'une année sur l'autre, ou qui dépendrait
            // d'autre chose que du tick, rendrait les traces de régression
            // incomparables — et, côté jeu, une récolte qu'on ne peut pas attendre
            // n'est plus une décision.
            var scenario = ScenarioLoader.Load(Fixtures.HeartlandPath());
            scenario.Anticipating.Seasons.Clear();
            scenario.Anticipating.Seasons.Add(new CargoSeasonDef
            {
                Cargo = "coal", DemandAmplitude = 0.5, DemandPeak = 0.25,
            });

            var sim = new Simulation(scenario, new AnticipatingEconomySolver());
            var market = sim.World.CityById("kingsport").Market("coal");
            double nominal = market.BaseDemandRate;

            var byDay = new Dictionary<int, double>();
            double yearlySum = 0;
            for (int i = 0; i < 2 * SimTick.TicksPerYear; i++)
            {
                sim.Step();
                int day = sim.World.Tick.DayOfYear;
                if (byDay.TryGetValue(day, out double previous))
                    Check.Near(previous, market.BaseDemandRate, 1e-12,
                        $"jour {day} : la demande saisonnière doit se répéter à l'identique");
                else
                    byDay[day] = market.BaseDemandRate;

                if (i < SimTick.TicksPerYear) yearlySum += market.BaseDemandRate;
            }

            // Le cosinus est centré sur 1 : la moyenne annuelle doit valoir le taux
            // nominal. Une saison qui déplacerait la moyenne serait un déséquilibre
            // structurel déguisé en dynamique, et --balance ne le verrait pas.
            Check.Near(nominal, yearlySum / SimTick.TicksPerYear, nominal * 1e-3,
                "moyenne annuelle de la demande saisonnière");
        });

        runner.Add("anticipation — la croissance des villes conserve la demande de la carte", () =>
        {
            // La croissance déplace la population, elle n'en crée pas. Sans cette
            // garantie elle faisait fondre la demande totale de 9 % en deux ans :
            // un scénario silencieusement plus mou que celui qu'on a équilibré, dont
            // la marge kilométrique flatteuse ne venait d'aucun modèle.
            var scenario = ScenarioLoader.Load(Fixtures.HeartlandPath());
            scenario.Anticipating.Seasons.Clear();
            scenario.Anticipating.GrowthRatePerTick = 0.05;
            scenario.Anticipating.GrowthMemoryTicks = 30;
            scenario.Anticipating.MinCitySize = 0.6;
            scenario.Anticipating.MaxCitySize = 1.4;

            var sim = new Simulation(scenario, new AnticipatingEconomySolver());

            double Total()
            {
                double sum = 0;
                foreach (var city in sim.World.Cities)
                    foreach (var market in sim.World.MarketsOf(city))
                        sum += market.BaseDemandRate;
                return sum;
            }

            double before = Total();
            sim.Run(500);
            double after = Total();

            // La conservation n'est pas exacte au dernier bit, et elle ne peut pas
            // l'être : une ville collée à sa borne de taille ne peut plus absorber sa
            // part de la correction. L'écart résiduel mesuré est de 0,04 % sur deux
            // ans ; le seuil est à 0,5 %, largement en dessous des 9 % de dérive que
            // ce mécanisme produisait avant renormalisation.
            Check.Near(before, after, before * 0.005, "demande totale des habitants");

            // Et la croissance doit avoir réellement bougé quelque chose, sans quoi
            // le test précédent passerait pour une raison sans intérêt.
            double spread = 0;
            foreach (var city in sim.World.Cities)
            {
                double ratio = city.Market("food").BaseDemandRate / city.Def.Demand["food"];
                spread = Math.Max(spread, Math.Abs(ratio - 1.0));
            }
            Check.True(spread > 0.05,
                $"la croissance doit redistribuer la demande, écart maximal = {spread:0.###}");
        });

        runner.Add("anticipation — 720 ticks du scénario de référence sans violation", () =>
        {
            var sim = new Simulation(
                ScenarioLoader.Load(Fixtures.HeartlandPath()), new AnticipatingEconomySolver());
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

        runner.Add("anticipation — l'économie anticipante reste vivante", () =>
        {
            // Même exigence que pour la référence, et pour la même raison : un
            // solveur qui gagnerait sur la mobilité en arrêtant les usines ou en
            // collant les prix au plafond n'aurait rien gagné du tout.
            var (sim, stats) = RunHeartland(new AnticipatingEconomySolver());
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

            double km = w.Trains.Sum(t => t.TotalKmTravelled);
            double marginPerKm = w.Company.NetProfit / km;
            Check.True(marginPerKm > 0,
                $"le transport doit être rentable, marge = {marginPerKm:0.00}/km");
            Check.Less(marginPerKm, 3.0,
                $"marge de {marginPerKm:0.00}/km invraisemblable — chercher une faille d'arbitrage");
        });

        runner.Add("anticipation — la dispersion se déplace plus que celle de la référence", () =>
        {
            // LE test du contrat du module économie. Il ne vérifie pas qu'il existe
            // des écarts de prix — la référence en a déjà — mais qu'ils *bougent* :
            // une dispersion figée donne une seule route à entretenir pour toute la
            // partie, et c'est l'économie morte la plus difficile à repérer, parce
            // qu'elle a l'air saine dans toutes les autres statistiques.
            //
            // Mesures du 27 septembre 2026, heartland, 720 ticks, trois trains :
            //   référence   mobilité 0,411   changement de tête 20,0 %
            //   anticipant  mobilité 0,615   changement de tête 26,2 %
            // Les seuils gardent de la marge : ils protègent contre une régression,
            // ils ne consacrent pas ces chiffres exacts.
            var (refSim, refStats) = RunHeartland(new ReferenceEconomySolver());
            var (sim, stats) = RunHeartland(new AnticipatingEconomySolver());

            double refMobility = refStats.MeanSpreadMobility(refSim.World);
            double mobility = stats.MeanSpreadMobility(sim.World);
            Check.True(mobility > refMobility * 1.25,
                $"mobilité de l'amplitude {mobility:0.000} contre {refMobility:0.000} pour la " +
                "référence — la dispersion ne respire pas assez plus");

            double refChurn = refStats.MeanLeaderChurn(refSim.World);
            double churn = stats.MeanLeaderChurn(sim.World);
            Check.True(churn > refChurn + 0.03,
                $"changement de ville la plus chère {churn * 100:0.0} % contre " +
                $"{refChurn * 100:0.0} % pour la référence — le meilleur débouché ne bouge pas assez");

            // La nourriture était la marchandise la plus uniforme du scénario, et
            // c'était une question ouverte de FINDINGS.md : dix acheteurs nourris au
            // prix de référence par un transporteur omniscient. C'est elle qui doit
            // le plus profiter de l'anticipation.
            Check.True(stats.SpreadMobility("food") > refStats.SpreadMobility("food") * 2.0,
                $"nourriture : mobilité {stats.SpreadMobility("food"):0.000} contre " +
                $"{refStats.SpreadMobility("food"):0.000} pour la référence");
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

        // ---------------------------------------------------------------- réseau
        // Les valeurs de référence de cette section sont le contrat du module
        // réseau. Elles ne décrivent pas un comportement « correct » dans l'absolu :
        // elles figent ce que l'algorithme actuel produit sur trois cartes connues,
        // pour qu'une retouche future doive s'expliquer au lieu de passer inaperçue.
        // Les mettre à jour est légitime ; les mettre à jour sans savoir pourquoi
        // elles ont bougé ne l'est pas.

        string TerrainScenario(string name) => Path.Combine(Fixtures.RepoRoot(), "data", name);

        IRailNetwork Network(string name)
        {
            var world = new Simulation(ScenarioLoader.Load(TerrainScenario(name))).World;
            return world.Network ?? throw new AssertionException($"{name} devrait déclarer un réseau");
        }

        runner.Add("réseau — le relief est reproductible à graine identique", () =>
        {
            // Sans cela, aucun devis n'est une valeur de référence : le coût d'un
            // tracé dépendrait du relief, et le relief du hasard.
            string First() => Network("terrain-pass.json").Terrain.Fingerprint();
            Check.Equal(First(), First(), "empreinte du relief");

            // Les cartes « vallée » et « col » déclarent le même relief : c'est ce qui
            // rend leurs devis comparables. Si l'une dérive, la comparaison ne veut
            // plus rien dire et il faut le savoir tout de suite.
            Check.Equal(
                Network("terrain-valley.json").Terrain.Fingerprint(),
                Network("terrain-pass.json").Terrain.Fingerprint(),
                "vallée et col doivent partager exactement le même relief");

            var def = new TerrainDef
            {
                Columns = 40, Rows = 40, CellSizeKm = 1, BaseElevationM = 100,
                Noise = new TerrainNoiseDef { AmplitudeM = 30, WavelengthKm = 5, Octaves = 3, Seed = 1 },
            };
            Check.Equal(
                TerrainFactory.Build(def, 99).Fingerprint(),
                TerrainFactory.Build(def, 99).Fingerprint(),
                "relief procédural à graine identique");

            def.Noise!.Seed = 2;
            Check.True(
                TerrainFactory.Build(def, 99).Fingerprint() != TerrainFactory.Build(def, 1).Fingerprint(),
                "deux graines différentes doivent donner deux reliefs différents");
        });

        runner.Add("réseau — devis de référence des trois cartes d'essai", () =>
        {
            // LE test du module. Trois tracés de soixante kilomètres exactement :
            // une plaine, un couloir de vallée, et le franchissement d'un col. Le
            // relief des deux derniers est identique ; seul change le tracé.
            var expected = new (string Scenario, string Edge, double Cost)[]
            {
                // plaine : 180 093, dont 180 009 de pose de voie. Une plaine coûte le
                // prix des rails, et c'est le plancher.
                ("terrain-plain.json", "plain", 180_092.65),
                // vallée : 219 402, soit 1,2 × la plaine. Le couloir naturel se paie
                // 39 000 de terrassement pour traverser deux contreforts, rien de plus.
                ("terrain-valley.json", "valley", 219_402.33),
                // col : 1 219 880, soit 6,8 × la plaine. 326 000 de terrassement et
                // 713 000 de viaducs sur les deux rampes d'accès.
                ("terrain-pass.json", "col", 1_219_880.03),
                // crête : 2 689 446, soit 14,9 × la plaine. Le même relief attaqué de
                // front à 36 km du col : un tunnel de 19 km, et la réponse est « non ».
                ("terrain-pass.json", "crest", 2_689_445.91),
            };

            foreach (var (scenario, edgeId, cost) in expected)
            {
                var edge = Network(scenario).Graph.Edge(edgeId);
                Check.Near(cost, edge.Construction.TotalCost, 0.5, $"devis du tronçon « {edgeId} »");
                Check.Near(60.0, edge.Construction.HorizontalLengthKm, 0.05,
                    $"le tronçon « {edgeId} » doit mesurer 60 km en plan");
            }
        });

        runner.Add("réseau — la vallée coûte une fraction du franchissement", () =>
        {
            // La propriété de conception, indépendamment des valeurs exactes : à
            // longueur égale et sur le même relief, suivre le couloir naturel doit
            // coûter une fraction du franchissement, et franchir au col une fraction
            // du franchissement de la crête. Si cet ordre s'inverse un jour, le choix
            // du tracé n'est plus un choix.
            var plain = Network("terrain-plain.json").Graph.Edge("plain").Construction;
            var pass = Network("terrain-pass.json");
            var valley = Network("terrain-valley.json").Graph.Edge("valley").Construction;
            var col = pass.Graph.Edge("col").Construction;
            var crest = pass.Graph.Edge("crest").Construction;

            Check.Less(valley.TotalCost, col.TotalCost / 4.0,
                "la vallée doit coûter moins du quart du col");
            Check.Less(col.TotalCost, crest.TotalCost,
                "franchir au col doit coûter moins que franchir la crête");
            Check.Less(plain.EarthworkCost, 1_000,
                "une plaine ne doit presque rien coûter en terrassement");
            Check.True(valley.Structures.Count == 0,
                $"la vallée ne doit exiger aucun ouvrage d'art, elle en compte {valley.Structures.Count}");
        });

        runner.Add("réseau — ponts et tunnels là où le relief les impose", () =>
        {
            // Le principe de l'arbitrage : le même relief se paie en remblais et
            // viaducs quand on le franchit par son point bas, et en tunnel quand on
            // l'attaque de front. Ce n'est pas le tracé qui change de nature, c'est
            // le devis qui désigne la stratégie la moins chère.
            var pass = Network("terrain-pass.json");
            var col = pass.Graph.Edge("col").Construction;
            var crest = pass.Graph.Edge("crest").Construction;

            Check.True(col.BridgeCost > 0, "le franchissement du col doit exiger des viaducs");
            Check.True(crest.TunnelCost > 0, "le franchissement de la crête doit exiger un tunnel");
            Check.Less(crest.ProfileBias, 0.01,
                "attaquer la crête de front doit conduire à un profil tout en déblai");

            foreach (var structure in col.Structures)
                Check.True(structure.LengthKm >= 0.3 - 1e-9,
                    $"ouvrage de {structure.LengthKm:0.###} km : en dessous du seuil, c'est du terrassement");
        });

        runner.Add("réseau — la pente maximale déclarée est respectée partout", () =>
        {
            // La contrainte de pente n'est pas indicative : c'est elle qui crée le
            // coût. Un tracé qui la dépasserait serait un tracé gratuit.
            foreach (string name in new[] { "terrain-plain.json", "terrain-valley.json", "terrain-pass.json" })
            {
                var scenario = ScenarioLoader.Load(TerrainScenario(name));
                double limit = scenario.Network.Alignment.MaxGradePercent;
                foreach (var edge in Network(name).Graph.Edges)
                {
                    Check.True(edge.Construction.MaxGradePercent <= limit + 1e-6,
                        $"{name} / {edge.Id} : {edge.Construction.MaxGradePercent:0.###} % > {limit:0.###} %");

                    double sections = 0;
                    foreach (var section in edge.Profile.Sections) sections += section.LengthKm;
                    Check.Near(edge.Construction.HorizontalLengthKm, sections, 1e-6,
                        $"{name} / {edge.Id} : les sections du profil doivent couvrir tout le tronçon");

                    Check.True(edge.Construction.LengthKm > edge.Construction.HorizontalLengthKm,
                        $"{name} / {edge.Id} : la longueur réelle doit dépasser la longueur en plan");
                }
            }
        });

        runner.Add("réseau — un virage plus serré que le rayon minimal est refusé", () =>
        {
            // Corriger un tracé impossible en silence donnerait au joueur une ligne
            // qu'il croit avoir posée et qui n'est pas la sienne.
            var scenario = Fixtures.NetworkScenario();
            scenario.Network.Nodes.Add(new TrackNodeDef { Id = "c", XKm = 1.5, YKm = 6.2 });
            scenario.Network.Edges.Add(new TrackEdgeDef
            {
                Id = "epingle", From = "a", To = "c",
                Via = { new MapPointDef { XKm = 9, YKm = 5 } },
            });

            Check.Throws<InvalidDataException>(() => new Simulation(scenario),
                "un virage en épingle doit être refusé au chargement");
        });

        runner.Add("réseau — un dénivelé inatteignable à la pente maximale est refusé", () =>
        {
            // Mille mètres en deux kilomètres, ce n'est pas cher : c'est impossible.
            // Le dire au chargement vaut mieux que produire une voie à 50 %.
            var scenario = Fixtures.NetworkScenario();
            scenario.Network.Terrain = new TerrainDef
            {
                CellSizeKm = 1,
                Heights = { "0 0 0", "500 500 500", "1000 1000 1000" },
            };
            scenario.Network.Nodes.Clear();
            scenario.Network.Edges.Clear();
            scenario.Network.Routes.Clear();
            scenario.Network.Nodes.Add(new TrackNodeDef { Id = "bas", City = "a-ville", XKm = 1, YKm = 0 });
            scenario.Network.Nodes.Add(new TrackNodeDef { Id = "haut", City = "b-ville", XKm = 1, YKm = 2 });
            scenario.Network.Edges.Add(new TrackEdgeDef { Id = "mur", From = "bas", To = "haut" });

            Check.Throws<InvalidDataException>(() => new Simulation(scenario),
                "un mur de mille mètres doit être refusé");
        });

        runner.Add("réseau — un nœud qui dessert une ville inconnue est refusé", () =>
        {
            var scenario = Fixtures.NetworkScenario();
            scenario.Network.Nodes.Add(new TrackNodeDef { Id = "z", City = "nulle-part", XKm = 5, YKm = 9 });

            Check.Throws<InvalidDataException>(() => new Simulation(scenario),
                "un nœud desservant une ville inconnue doit être refusé");
        });

        runner.Add("réseau — l'itinéraire le plus court est choisi, et il est stable", () =>
        {
            // Deux détours de longueur rigoureusement identique existent dès qu'une
            // carte est un peu régulière. Départager par l'indice d'arête, et jamais
            // par l'ordre d'un dictionnaire, est ce qui rend une partie rejouable.
            var scenario = Fixtures.NetworkScenario();
            scenario.Cities.Add(Fixtures.City("nord-ville"));
            scenario.Cities.Add(Fixtures.City("sud-ville"));
            scenario.Network.Nodes.Add(new TrackNodeDef { Id = "nord", City = "nord-ville", XKm = 5, YKm = 9 });
            scenario.Network.Nodes.Add(new TrackNodeDef { Id = "sud", City = "sud-ville", XKm = 5, YKm = 1 });
            scenario.Network.Edges.Add(new TrackEdgeDef { Id = "a-nord", From = "a", To = "nord" });
            scenario.Network.Edges.Add(new TrackEdgeDef { Id = "nord-b", From = "nord", To = "b" });
            scenario.Network.Edges.Add(new TrackEdgeDef { Id = "a-sud", From = "a", To = "sud" });
            scenario.Network.Edges.Add(new TrackEdgeDef { Id = "sud-b", From = "sud", To = "b" });

            string Chosen()
            {
                var network = new Simulation(scenario).World.Network!;
                Check.True(network.TryConnect("a", "b", out var route), "a et b doivent être reliés");
                return string.Join(",", route.Legs.Select(l => l.Edge.Id));
            }

            // Le tronçon direct « a-b » existe déjà dans le scénario de base : c'est
            // le plus court, les deux détours sont plus longs et de même longueur.
            Check.Equal("a-b", Chosen(), "itinéraire retenu");
            Check.Equal(Chosen(), Chosen(), "itinéraire retenu deux fois de suite");

            var graph = new Simulation(scenario).World.Network!.Graph;
            Check.True(graph.Node("nord").EdgeIndices.Count == 2,
                "le nœud nord doit connaître ses deux tronçons incidents");
            Check.True(graph.Node("a").PassingTracks >= 1,
                "un nœud doit déclarer sa capacité de croisement pour la signalisation");
        });

        runner.Add("réseau — le relief atteint le coût kilométrique du transport", () =>
        {
            // Le seul canal par lequel le relief touche l'économie. Sans lui, le
            // module réseau serait un décor : un col coûterait à construire et rien
            // à exploiter, et le rayon économique des marchandises à bas prix
            // (docs/FINDINGS.md) serait le même partout.
            var pass = new Simulation(ScenarioLoader.Load(TerrainScenario("terrain-pass.json")));
            var col = pass.World.Lines.First(l => l.Id == "main");
            Check.True(col.LegCostFactor(0, 1) > 1.05,
                $"franchir un col doit coûter plus cher au kilomètre, facteur = {col.LegCostFactor(0, 1):0.000}");
            Check.True(col.Segments.Count == 1 && col.Segments[0].Legs.Count == 1,
                "la ligne du col doit exposer son unique tronçon pour la signalisation");

            var plain = new Simulation(ScenarioLoader.Load(TerrainScenario("terrain-plain.json")));
            var flat = plain.World.Lines.First(l => l.Id == "main");
            Check.Less(flat.LegCostFactor(0, 1), col.LegCostFactor(0, 1),
                "la plaine doit coûter moins cher au kilomètre que le col");
        });

        runner.Add("compatibilité — le scénario de référence ignore le module réseau", () =>
        {
            // heartland reste sur ses distances saisies à la main. C'est la voie de
            // compatibilité, et c'est ce qui permet au module réseau d'arriver sans
            // invalider les traces de régression de la campagne de mesure.
            var sim = new Simulation(ScenarioLoader.Load(Fixtures.HeartlandPath()));
            Check.True(sim.World.Network is null,
                "heartland ne déclare pas de relief : son réseau doit rester nul");

            var line = sim.World.Lines[0];
            Check.Near(500, line.LengthKm, 1e-9, "longueur de la ligne principale");
            Check.Near(1.0, line.LegCostFactor(0, 1), 1e-9,
                "sans relief, le relief ne doit rien coûter");
            Check.Near(45, line.DistanceBetween(0, 1), 1e-9, "distance entre les deux premiers arrêts");
        });

        // ------------------------------------------------------------- content
        // Module `content` : catalogue de locomotives et second scénario
        // (data/ironpeak.json, chaîne minerai → fonte → acier). Ces tests
        // existent pour la même raison que ceux de heartland ci-dessus : une
        // faute de frappe dans un JSON de données ne doit jamais passer
        // inaperçue derrière un « ça charge sans exception ».

        runner.Add("content — le catalogue de locomotives se charge et est cohérent", () =>
        {
            var catalog = LocomotiveLoader.Load(Fixtures.LocomotivesPath());
            Check.True(catalog.Locomotives.Count >= 10,
                $"au moins 10 locomotives attendues, {catalog.Locomotives.Count} trouvées");

            int minYear = catalog.Locomotives.Min(l => l.Year);
            int maxYear = catalog.Locomotives.Max(l => l.Year);
            Check.True(minYear <= 1840, $"le catalogue doit couvrir le début du XIXe siècle, plus ancienne = {minYear}");
            Check.True(maxYear >= 1930, $"le catalogue doit couvrir le XXe siècle, plus récente = {maxYear}");

            // La progression technologique est le point du catalogue : une
            // locomotive plus tardive ne doit pas, en moyenne, être moins
            // capable qu'une locomotive ancienne. On vérifie seulement les
            // extrêmes : la dernière machine doit largement dépasser la première.
            var oldest = catalog.Locomotives.OrderBy(l => l.Year).First();
            var newest = catalog.Locomotives.OrderBy(l => l.Year).Last();
            Check.Less(oldest.TractiveEffortKn, newest.TractiveEffortKn,
                "la locomotive la plus récente doit avoir un effort de traction supérieur à la plus ancienne");
        });

        runner.Add("content — le scénario ironpeak se charge sans erreur", () =>
        {
            var scenario = ScenarioLoader.Load(Fixtures.IronpeakPath());
            Check.True(scenario.Cargos.Count == 7, $"7 marchandises attendues, {scenario.Cargos.Count} trouvées");
            Check.True(scenario.Cities.Count == 12, $"12 villes attendues, {scenario.Cities.Count} trouvées");
            Check.True(scenario.Recipes.Count == 3, $"3 recettes attendues, {scenario.Recipes.Count} trouvées");
        });

        runner.Add("content — la chaîne acier ajoute environ 70 % de valeur à chaque maillon", () =>
        {
            // Même vérification que celle qui manquait à heartland avant sa
            // calibration (voir FINDINGS.md, « une chaîne de valeur jamais
            // calibrée ») : on la fait ici, sur les vraies données, plutôt que de
            // se fier au commentaire de conception dans le JSON.
            var scenario = ScenarioLoader.Load(Fixtures.IronpeakPath());
            var cargo = scenario.Cargos.ToDictionary(c => c.Id);

            foreach (var recipe in scenario.Recipes)
            {
                double inputCost = recipe.Inputs.Sum(i => i.Qty * cargo[i.Cargo].BasePrice);
                double outputValue = recipe.Outputs.Sum(o => o.Qty * cargo[o.Cargo].BasePrice);
                double margin = outputValue / inputCost;
                Check.True(margin > 1.5 && margin < 1.9,
                    $"recette '{recipe.Id}' : marge {margin:0.###} hors de la plage visée [1.5, 1.9] (~70 %)");
            }
        });

        runner.Add("content — le scénario ironpeak est équilibré offre/demande", () =>
        {
            var scenario = ScenarioLoader.Load(Fixtures.IronpeakPath());
            foreach (var b in BalanceReport.Compute(scenario))
            {
                Check.True(b.Ratio >= 0.95 && b.Ratio <= 1.5,
                    $"{b.CargoName} : offre/demande = {b.Ratio:0.00} ({b.Verdict})");
            }
        });

        runner.Add("content — le scénario ironpeak tourne 720 ticks sans violer d'invariant", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(Fixtures.IronpeakPath()));
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

        runner.Add("content — les trains d'ironpeak référencent des locomotives du catalogue", () =>
        {
            // Le lien TrainDef.Locomotive est purement informatif (la simulation
            // ne le lit pas), mais une référence à une locomotive inexistante
            // serait une faute de frappe silencieuse : ce test la détecterait.
            var scenario = ScenarioLoader.Load(Fixtures.IronpeakPath());
            var catalog = LocomotiveLoader.Load(Fixtures.LocomotivesPath());
            var knownIds = catalog.Locomotives.Select(l => l.Id).ToHashSet();

            foreach (var train in scenario.Trains)
            {
                if (train.Locomotive is null) continue;
                Check.True(knownIds.Contains(train.Locomotive),
                    $"le train '{train.Id}' référence la locomotive inconnue '{train.Locomotive}'");
            }
        });

        // Les invariants du module finance vivent dans leur propre fichier :
        // société, emprunts, bourse, insolvabilité. Un seul point d'entrée ici,
        // pour que trois modules qui avancent en parallèle n'entrent pas en
        // conflit sur cette liste.
        FinanceTests.Register(runner);

        // ------------------------------------------------ traces de référence
        // En dernier : elles figent le comportement de tous les modules à la fois,
        // et un échec ici se lit mieux une fois les invariants de chacun passés.
        ReferenceTraceTests.Register(runner);

        Console.WriteLine("RailTycoon — invariants de simulation");
        Console.WriteLine();
        return runner.Run();
    }

    /// <summary>
    /// Exécution de référence : 720 ticks du scénario heartland, 90 de chauffe
    /// exclus des statistiques. Les deux solveurs doivent être comparés sur
    /// exactement le même protocole, sans quoi la comparaison ne mesure que le
    /// protocole.
    /// </summary>
    private static (Simulation Sim, RunStatistics Stats) RunHeartland(IEconomySolver solver)
    {
        var sim = new Simulation(ScenarioLoader.Load(Fixtures.HeartlandPath()), solver);
        var stats = new RunStatistics { WarmupTicks = 90 };
        for (int i = 0; i < 720; i++)
        {
            sim.Step();
            stats.Sample(sim.World);
        }
        return (sim, stats);
    }
}
