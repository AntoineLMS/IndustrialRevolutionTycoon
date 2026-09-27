using RailTycoon.Sim;
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

        Console.WriteLine("RailTycoon — invariants de simulation");
        Console.WriteLine();
        return runner.Run();
    }
}
