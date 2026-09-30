using System.Globalization;
using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Objectives;
using RailTycoon.Sim.Telemetry;

namespace RailTycoon.Tests;

/// <summary>
/// Invariants du module objectives. Même organisation que les autres modules : un
/// fichier à part, un seul appel dans <c>Program</c>.
/// <para>
/// Chaque date attendue est recalculée ici par un autre chemin que celui du module —
/// la série des fortunes relevée tick par tick, les dépôts et retraits des marchés,
/// la distance et la vitesse d'un train — : un test qui relirait le module pour
/// savoir quand le module aurait dû conclure ne prouverait rien.
/// </para>
/// </summary>
internal static class ObjectiveTests
{
    private static string CyclePath() => Path.Combine(Fixtures.RepoRoot(), "data", "heartland-cycle.json");
    private static string FinancePath() => Path.Combine(Fixtures.RepoRoot(), "data", "heartland-finance.json");
    private static string SierraPath() => Path.Combine(Fixtures.RepoRoot(), "data", "sierra.json");

    private static IEconomySolver Solver(string name)
        => name == "reference" ? new ReferenceEconomySolver() : new AnticipatingEconomySolver();

    private static readonly string[] Solvers = ["reference", "anticipating"];

    public static void Register(TestRunner runner)
    {
        // ------------------------------------------------------ observateur pur

        runner.Add("objectifs — observateur pur : la partie est la même avec et sans objectifs, sous les deux solveurs", () =>
        {
            // Les deux scénarios livrés qui portent des objectifs, joués avec leur bloc
            // puis sans. L'empreinte couvre les marchés, la compagnie, les usines,
            // l'état financier de fin de partie au centime, et les journaux des
            // événements et de la conjoncture : rien de ce que la partie produit ne
            // doit bouger. Le module n'a donc le droit ni d'écrire, ni de tirer.
            foreach (string solver in Solvers)
            {
                foreach (var (path, ticks) in new[] { (CyclePath(), CycleTests.Ticks), (SierraPath(), 720) })
                {
                    var with = ScenarioLoader.Load(path);
                    Check.True(with.Objectives.Enabled && with.Objectives.Goals.Count > 0,
                        $"{Path.GetFileName(path)} doit porter des objectifs, sinon le test ne prouve rien");
                    var without = ScenarioLoader.Load(path);
                    without.Objectives = new ObjectivesDef();

                    var (withPrint, withSim) = Fingerprint(with, solver, ticks);
                    var (withoutPrint, _) = Fingerprint(without, solver, ticks);
                    Check.True(withSim.World.Objectives.Journal.Count > 0,
                        $"{Path.GetFileName(path)} ({solver}) : aucun palier conclu, le module n'a rien observé");
                    Check.Equal(withoutPrint, withPrint,
                        $"{Path.GetFileName(path)} avec et sans objectifs (solveur {solver})");
                }
            }
        });

        runner.Add("objectifs — sans bloc, le module n'écrit rien", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(FinancePath()));
            sim.Run(60);
            var state = sim.World.Objectives;
            Check.True(!state.Enabled && state.Goals.Count == 0 && state.Journal.Count == 0,
                "heartland-finance ne déclare pas d'objectifs : ni progression, ni journal");
            Check.Equal("", state.Summary(), "résumé d'un module inactif");
            Check.Equal("tick,objective,tier,kind,outcome,measure,target,detail\n", CsvRecorder.ObjectivesCsv(sim.World),
                "sans module, objectives.csv ne contient que son en-tête");

            // Un bloc plein mais désactivé ne fait rien non plus.
            var disabled = ScenarioLoader.Load(CyclePath());
            disabled.Objectives.Enabled = false;
            var quiet = new Simulation(disabled);
            quiet.Run(30);
            Check.True(quiet.World.Objectives.Goals.Count == 0 && quiet.World.Objectives.Journal.Count == 0,
                "un bloc désactivé n'ouvre aucun objectif");
        });

        runner.Add("objectifs — le carnet de route égale les dépôts et retraits des marchés, marchandise par ville", () =>
        {
            // Le carnet est écrit par le transport au moment de l'échange. S'il manquait
            // un site d'échange, un objectif de livraisons compterait faux sans que rien
            // ne le signale. Les marchés tiennent leurs propres compteurs, remis à zéro
            // chaque tick : on les cumule ici, échanges de l'initialisation compris, et
            // les deux comptes doivent coïncider.
            var sim = new Simulation(ScenarioLoader.Load(Fixtures.HeartlandPath()));
            var w = sim.World;
            var imported = new Dictionary<(string, string), double>();
            var exported = new Dictionary<(string, string), double>();
            void Accumulate()
            {
                foreach (var city in w.Cities)
                    foreach (var market in w.MarketsOf(city))
                    {
                        var key = (market.CargoId, city.Id);
                        imported[key] = imported.GetValueOrDefault(key) + market.ImportedThisTick;
                        exported[key] = exported.GetValueOrDefault(key) + market.ExportedThisTick;
                    }
            }
            Accumulate(); // les trains ont échangé pendant l'initialisation
            for (int i = 0; i < 720; i++)
            {
                sim.Step();
                Accumulate();
            }

            var ledger = w.Company.Freight;
            double traded = 0;
            foreach (var city in w.Cities)
                foreach (string cargo in w.CargoOrder)
                {
                    double sold = ledger.Sold(cargo, city.Id), bought = ledger.Bought(cargo, city.Id);
                    Check.Near(imported[(cargo, city.Id)], sold, 1e-6 * Math.Max(1, sold), $"{city.Id}/{cargo} : vendu");
                    Check.Near(exported[(cargo, city.Id)], bought, 1e-6 * Math.Max(1, bought), $"{city.Id}/{cargo} : acheté");
                    traded += sold + bought;
                }
            Check.True(traded > 1000, "le test perdrait son sens sans échanges");
        });

        // -------------------------------------------------------------- fortune

        runner.Add("objectifs — fortune : atteinte le jour où la fortune du magnat (ou sa moyenne) franchit la cible", () =>
        {
            // La série des fortunes du magnat est relevée tick par tick sur une partie
            // sans objectifs ; la date attendue s'en déduit, au tick courant et en
            // moyenne sur 30 jours. La partie avec objectifs doit conclure ces jours-là
            // — observateur pur, c'est la même partie.
            const int ticks = 400;
            var series = new List<decimal>();
            var plain = new Simulation(ScenarioLoader.Load(FinancePath()));
            for (int i = 0; i < ticks; i++)
            {
                plain.Step();
                series.Add(plain.World.Finance.Magnate!.NetWorth(plain.World.Finance));
            }
            decimal Average(int tickIndex, int window)
            {
                decimal sum = 0m;
                for (int k = tickIndex - window + 1; k <= tickIndex; k++) sum += series[k - 1];
                return sum / window;
            }
            int FirstAt(decimal target, int window)
            {
                for (int t = window; t <= ticks; t++)
                    if (Average(t, window) >= target) return t;
                return -1;
            }

            // Des cibles choisies pour tomber en cours de partie, sur la remontée qui
            // suit les premiers mois (la fortune du magnat témoin fond d'abord, puis
            // remonte) : la moyenne de 30 jours au tick 330, et la fortune du jour la
            // plus haute des 300 premiers.
            decimal averagedTarget = Money(Average(330, 30));
            decimal instantTarget = series.Take(300).Max();
            int expectedAveraged = FirstAt(averagedTarget, 30);
            int expectedInstant = FirstAt(instantTarget, 1);
            Check.True(expectedAveraged > 30 && expectedAveraged <= 330 && expectedInstant > 30 && expectedInstant <= 300,
                $"cibles mal choisies : {expectedAveraged}, {expectedInstant}");
            Check.True(expectedAveraged != FirstAt(averagedTarget, 1),
                "la cible doit distinguer la moyenne de la fortune du jour, sinon la fenêtre n'est pas éprouvée");

            var scenario = ScenarioLoader.Load(FinancePath());
            scenario.Objectives = new ObjectivesDef
            {
                Enabled = true,
                Goals =
                {
                    Goal("moyenne", ObjectiveKinds.Fortune, g => g.AverageTicks = 30, Tier("t", averagedTarget)),
                    Goal("jour", ObjectiveKinds.Fortune, g => g.AverageTicks = 1, Tier("t", instantTarget)),
                },
            };
            var sim = new Simulation(scenario);
            sim.Run(ticks);
            var goals = sim.World.Objectives.Goals;
            Check.Equal(expectedAveraged.ToString(CultureInfo.InvariantCulture),
                goals[0].Tiers[0].Tick.ToString(CultureInfo.InvariantCulture), "moyenne sur 30 jours : jour atteint");
            Check.Equal(expectedInstant.ToString(CultureInfo.InvariantCulture),
                goals[1].Tiers[0].Tick.ToString(CultureInfo.InvariantCulture), "fortune du jour : jour atteint");
            Check.True(goals.All(g => g.Tiers[0].Status == TierStatus.Attained), "les deux paliers doivent être atteints");

            // La mesure est la fortune du magnat, pas la caisse de la compagnie : la
            // vision distingue les deux, et le module aussi.
            var finance = sim.World.Finance;
            Check.Near((double)finance.Magnate!.NetWorth(finance), goals[1].Measure!.Value, 0.005, "mesure du jour = fortune du magnat");
            Check.True(Math.Abs(goals[1].Measure!.Value - sim.World.Company.Cash) > 1000,
                "la fortune du magnat ne doit pas se confondre avec la trésorerie de la compagnie");

            // Pendant les 29 premiers jours, une moyenne sur 30 n'est pas lisible.
            var early = new Simulation(scenario);
            early.Run(29);
            Check.True(early.World.Objectives.Goals[0].Measure is null, "moyenne sur 30 jours lisible avant 30 jours");
        });

        // ----------------------------------------------------------- livraisons

        runner.Add("objectifs — livraisons : atteintes le jour où le rail a laissé la cible dans la ville", () =>
        {
            // a-ville produit du blé, b-ville en mange. La date attendue se lit sur les
            // dépôts et retraits du marché de b-ville, cumulés tick par tick —
            // initialisation comprise —, sans passer par le carnet de route.
            var plain = Delivery(null);
            var market = plain.World.CityById("b-ville").Market("grain");
            double net = market.ImportedThisTick - market.ExportedThisTick;
            var netByTick = new List<double>();
            for (int i = 0; i < 200; i++)
            {
                plain.Step();
                net += market.ImportedThisTick - market.ExportedThisTick;
                netByTick.Add(net);
            }
            decimal target = 30m;
            int expected = netByTick.FindIndex(n => n >= (double)target) + 1;
            Check.True(expected > 5 && expected < 200, $"cible mal choisie : jour {expected}");

            var sim = Delivery(new ObjectivesDef
            {
                Enabled = true,
                Goals =
                {
                    Goal("b", ObjectiveKinds.Deliveries, g => { g.Cargo = "grain"; g.City = "b-ville"; }, Tier("t", target)),
                    Goal("total", ObjectiveKinds.Deliveries, g => g.Cargo = "grain", Tier("t", target)),
                },
            });
            sim.Run(200);
            var goals = sim.World.Objectives.Goals;
            Check.Equal(expected.ToString(CultureInfo.InvariantCulture),
                goals[0].Tiers[0].Tick.ToString(CultureInfo.InvariantCulture), "livraisons à b-ville : jour atteint");
            // Seule b-ville consomme : le total est ce qu'elle a reçu, a-ville ne fait qu'expédier.
            Check.Equal(expected.ToString(CultureInfo.InvariantCulture),
                goals[1].Tiers[0].Tick.ToString(CultureInfo.InvariantCulture), "livraisons au total : jour atteint");
            Check.Near(netByTick[^1], goals[0].Measure!.Value, 1e-9, "mesure = vendu moins racheté à b-ville");
        });

        runner.Add("objectifs — livraisons : la revente de ville en ville ne gonfle pas le compteur", () =>
        {
            // Deux lignes qui se touchent à b-ville : t1 porte le blé d'a-ville à
            // b-ville, t2 le rachète à b-ville et le revend à c-ville, plus affamée. Le
            // même blé est donc vendu deux fois. Compter les ventes le compterait deux
            // fois ; « livré » (vendu moins racheté, ville par ville) le compte une
            // fois, là où il est resté.
            var sim = Resale(Deliveries("grain"));
            sim.Run(360);
            var w = sim.World;
            var ledger = w.Company.Freight;
            double leftA = ledger.Bought("grain", "a-ville") - ledger.Sold("grain", "a-ville");
            double resold = ledger.Bought("grain", "b-ville");
            double sold = w.Cities.Sum(c => ledger.Sold("grain", c.Id));
            Check.True(resold > 20, $"b-ville doit revendre : {resold:0.#} rachetés, le test perdrait son sens");
            Check.True(sold > leftA + 20, $"les ventes ({sold:0.#}) doivent compter deux fois le blé revendu ({leftA:0.#} sortis d'a-ville)");

            // Conservation : ce que le rail a laissé dans les villes importatrices est
            // exactement ce qu'il a pris aux villes exportatrices, moins ce qui est
            // encore à bord. Seule a-ville produit : rien ne peut être livré deux fois.
            double delivered = w.Objectives.Goals[0].Measure!.Value;
            double taken = w.Cities.Sum(c => Math.Max(0, ledger.Bought("grain", c.Id) - ledger.Sold("grain", c.Id)));
            double aboard = w.Trains.Sum(t => t.Cargo.GetValueOrDefault("grain"));
            Check.Near(taken - aboard, delivered, 1e-6, "livré = pris aux villes exportatrices − à bord");
            Check.True(delivered <= leftA + 1e-9,
                $"livré au total ({delivered:0.##}) ne peut pas dépasser ce qui a quitté a-ville ({leftA:0.##})");

            // Et sur heartland-cycle, la nourriture : vendue des dizaines de fois pour
            // une fois produite ; le compteur reste sous ce qui a été produit.
            var cycle = new Simulation(ScenarioLoader.Load(CyclePath()));
            cycle.Run(720);
            var cw = cycle.World;
            double foodSold = cw.Cities.Sum(c => cw.Company.Freight.Sold("food", c.Id));
            double foodProduced = cw.Cities.Sum(c => c.Market("food").TotalProduced);
            double foodDelivered = cw.Objectives.Goals.Single(g => g.Def.Id == "vivres").Measure!.Value;
            Check.True(foodSold > 20 * foodProduced, $"la nourriture doit tourner : {foodSold:0} vendus pour {foodProduced:0} produits");
            Check.True(foodDelivered <= foodProduced, $"livré {foodDelivered:0} pour {foodProduced:0} produits");
        });

        // -------------------------------------------------------------- échéance

        runner.Add("objectifs — manqué le soir de l'échéance, pas avant ; atteint le jour même, c'est atteint", () =>
        {
            // Même monde que le test des livraisons : on relève d'abord le jour où
            // b-ville a reçu 30 chargements, puis on place l'échéance autour.
            var probe = Delivery(Deliveries("grain", "b-ville", Tier("t", 30m)));
            probe.Run(200);
            int reached = probe.World.Objectives.Goals[0].Tiers[0].Tick;
            Check.True(reached > 5, "le palier doit être atteint en cours de partie");

            // Échéance le jour même : atteint. La veille : manqué, ce soir-là.
            var onTime = Delivery(Deliveries("grain", "b-ville", Tier("t", 30m, reached)));
            onTime.Run(200);
            var tier = onTime.World.Objectives.Goals[0].Tiers[0];
            Check.True(tier.Status == TierStatus.Attained && tier.Tick == reached, "atteint le jour de l'échéance");

            var late = Delivery(Deliveries("grain", "b-ville", Tier("t", 30m, reached - 1)));
            late.Run(reached - 2);
            Check.True(late.World.Objectives.Goals[0].Tiers[0].Status == TierStatus.InProgress, "en cours la veille de l'échéance");
            late.Run(1);
            tier = late.World.Objectives.Goals[0].Tiers[0];
            Check.True(tier.Status == TierStatus.Missed && tier.Tick == reached - 1, "manqué le soir de l'échéance");
            late.Run(100);
            Check.True(tier.Status == TierStatus.Missed && late.World.Objectives.Journal.Count == 1,
                "un palier manqué le reste, même quand la mesure franchit la cible ensuite");

            // Sans échéance, une cible hors d'atteinte reste en cours pour toujours.
            var open = Delivery(Deliveries("grain", "b-ville", Tier("t", 1_000_000m)));
            open.Run(200);
            Check.True(open.World.Objectives.Goals[0].Tiers[0].Status == TierStatus.InProgress
                       && open.World.Objectives.Journal.Count == 0, "sans échéance, jamais manqué");
        });

        // ---------------------------------------------------------------- liaison

        runner.Add("objectifs — liaison : un même train, sur une même ligne, s'est arrêté dans les deux villes", () =>
        {
            // Deux lignes disjointes : a-ville — m-ville — b-ville (t1, départ à
            // a-ville) et c-ville — d-ville (t2). m-ville est à 10 km d'a-ville, b-ville
            // à 100 km ; t1 roule à 30 km par tick. Il passe m-ville au premier tick
            // sans s'y trouver le soir, et arrive à b-ville au quatrième (100 ÷ 30).
            var scenario = Fixtures.Bare("liaison");
            foreach (string id in new[] { "a-ville", "m-ville", "b-ville", "c-ville", "d-ville" })
                scenario.Cities.Add(Fixtures.City(id));
            scenario.Lines.Add(Line("ouest", ("a-ville", 0), ("m-ville", 10), ("b-ville", 100)));
            scenario.Lines.Add(Line("est", ("c-ville", 0), ("d-ville", 60)));
            scenario.Trains.Add(new TrainDef { Id = "t1", Line = "ouest", SpeedKmPerTick = 30, StartStop = 0 });
            scenario.Trains.Add(new TrainDef { Id = "t2", Line = "est", SpeedKmPerTick = 30, StartStop = 0 });
            scenario.Objectives = new ObjectivesDef
            {
                Enabled = true,
                Goals =
                {
                    Goal("a-b", ObjectiveKinds.Connect, g => g.Cities = ["a-ville", "b-ville"], Tier("t", null)),
                    Goal("b-a", ObjectiveKinds.Connect, g => g.Cities = ["b-ville", "a-ville"], Tier("t", null)),
                    Goal("a-m", ObjectiveKinds.Connect, g => g.Cities = ["a-ville", "m-ville"], Tier("t", null)),
                    // a-ville et d-ville sont toutes deux desservies, mais par deux trains
                    // sur deux réseaux qui ne se touchent pas : elles ne sont pas reliées.
                    Goal("a-d", ObjectiveKinds.Connect, g => g.Cities = ["a-ville", "d-ville"], Tier("t", null, 50)),
                },
            };
            var sim = new Simulation(scenario);
            sim.Run(60);
            var goals = sim.World.Objectives.Goals;
            Check.True(sim.World.Company.Freight.HasVisited(sim.World.Trains[1], "d-ville"), "t2 doit avoir desservi d-ville");
            Check.True(goals[0].Tiers[0].Tick == 4 && goals[0].Detail == "t1", $"a-b relié au tick {goals[0].Tiers[0].Tick}, attendu 4 par t1");
            Check.True(goals[1].Tiers[0].Tick == 4, "une liaison n'a pas de sens");
            Check.True(goals[2].Tiers[0].Tick == 1, $"a-m relié au tick {goals[2].Tiers[0].Tick}, attendu 1 : un arrêt traversé compte");
            Check.True(goals[3].Tiers[0].Status == TierStatus.Missed && goals[3].Tiers[0].Tick == 50,
                "deux villes desservies par deux trains sans voie commune ne sont pas reliées");
        });

        // --------------------------------------------------------- combinaison

        runner.Add("objectifs — plusieurs objectifs et paliers se combinent sans s'influencer", () =>
        {
            // Livraisons à deux paliers, liaison, et un objectif manqué, dans le même
            // scénario. Le journal de l'ensemble doit être exactement la réunion des
            // journaux de chacun joué seul, rangée par jour puis dans l'ordre des
            // données : aucun objectif ne lit l'état d'un autre.
            ObjectiveDef[] goals =
            [
                Goal("bl", ObjectiveKinds.Deliveries, g => { g.Cargo = "grain"; g.City = "b-ville"; },
                    Tier("dix", 10m), Tier("trente", 30m, 150), Tier("mille", 1000m, 120)),
                Goal("lien", ObjectiveKinds.Connect, g => g.Cities = ["b-ville", "a-ville"], Tier("vite", null, 10)),
                Goal("total", ObjectiveKinds.Deliveries, g => g.Cargo = "grain", Tier("cinq", 5m)),
            ];

            var together = Delivery(new ObjectivesDef { Enabled = true, Goals = goals.ToList() });
            together.Run(200);
            var journal = together.World.Objectives.Journal;
            Check.True(journal.Count == 5, $"cinq paliers conclus attendus, {journal.Count} obtenus");
            Check.True(journal.Any(r => r.TierId == "mille" && r.Outcome == TierStatus.Missed && r.Tick == 120),
                "le palier hors d'atteinte doit être manqué à son échéance");

            var separate = new List<ObjectiveRecord>();
            foreach (var goal in goals)
            {
                var alone = Delivery(new ObjectivesDef { Enabled = true, Goals = { goal } });
                alone.Run(200);
                separate.AddRange(alone.World.Objectives.Journal);
            }
            string Key(ObjectiveRecord r) => $"{r.Tick}:{r.ObjectiveId}/{r.TierId}:{r.Outcome}";
            var order = goals.Select(g => g.Id).ToList();
            var expected = separate
                .OrderBy(r => r.Tick)
                .ThenBy(r => order.IndexOf(r.ObjectiveId))
                .ThenBy(r => goals[order.IndexOf(r.ObjectiveId)].Tiers.FindIndex(t => t.Id == r.TierId))
                .Select(Key);
            Check.Equal(string.Join(";", expected), string.Join(";", journal.Select(Key)), "journal combiné");

            // Deux paliers sur une même mesure tombent dans l'ordre de leurs cibles.
            var bl = together.World.Objectives.Goals[0].Tiers;
            Check.True(bl[0].Tick < bl[1].Tick, "dix chargements avant trente");
        });

        // ------------------------------------------------------------ validation

        runner.Add("objectifs — les données incohérentes sont refusées au chargement", () =>
        {
            void Refused(string why, ObjectivesDef objectives, ScenarioDef? world = null)
            {
                var scenario = world ?? DeliveryScenario();
                scenario.Objectives = objectives;
                Check.Throws<InvalidDataException>(() => new Simulation(scenario), why);
            }
            ObjectivesDef One(ObjectiveDef goal) => new() { Enabled = true, Goals = { goal } };

            Refused("marchandise inconnue", Deliveries("acier", "", Tier("t", 1m)));
            Refused("ville de destination inconnue", Deliveries("grain", "atlantis", Tier("t", 1m)));
            Refused("ville à relier inconnue", One(Goal("l", ObjectiveKinds.Connect, g => g.Cities = ["a-ville", "atlantis"], Tier("t", null))));
            Refused("une liaison relie deux villes", One(Goal("l", ObjectiveKinds.Connect, g => g.Cities = ["a-ville"], Tier("t", null))));
            Refused("une ville ne se relie pas à elle-même", One(Goal("l", ObjectiveKinds.Connect, g => g.Cities = ["a-ville", "a-ville"], Tier("t", null))));
            Refused("une liaison n'a pas de cible", One(Goal("l", ObjectiveKinds.Connect, g => g.Cities = ["a-ville", "b-ville"], Tier("t", 3m))));
            Refused("sorte inconnue", One(Goal("x", "passagers", _ => { }, Tier("t", 1m))));
            Refused("bloc actif sans objectif", new ObjectivesDef { Enabled = true });
            Refused("objectif sans palier", One(Goal("x", ObjectiveKinds.Deliveries, g => g.Cargo = "grain")));
            Refused("cible absente", Deliveries("grain", "", Tier("t", null)));
            Refused("cible nulle", Deliveries("grain", "", Tier("t", 0m)));
            Refused("palier en double", Deliveries("grain", "", Tier("t", 1m), Tier("t", 2m)));
            Refused("objectif en double", new ObjectivesDef
            {
                Enabled = true,
                Goals = { Goal("x", ObjectiveKinds.Deliveries, g => g.Cargo = "grain", Tier("t", 1m)),
                          Goal("x", ObjectiveKinds.Deliveries, g => g.Cargo = "grain", Tier("t", 1m)) },
            });
            Refused("champ d'une autre sorte", One(Goal("x", ObjectiveKinds.Deliveries, g => { g.Cargo = "grain"; g.AverageTicks = 30; }, Tier("t", 1m))));
            Refused("échéance avant le premier jour", Deliveries("grain", "", Tier("t", 1m, 0)));

            // Une fortune sans finance : pas de repli silencieux sur la caisse de la
            // compagnie, un refus qui dit pourquoi.
            Refused("fortune sans module finance", One(Goal("f", ObjectiveKinds.Fortune, g => g.AverageTicks = 1, Tier("t", 1m))));
            var withFinance = ScenarioLoader.Load(FinancePath());
            Refused("fortune sans fenêtre de lecture", One(Goal("f", ObjectiveKinds.Fortune, _ => { }, Tier("t", 1m))), withFinance);
            Check.True(new Simulation(WithObjectives(ScenarioLoader.Load(FinancePath()),
                One(Goal("f", ObjectiveKinds.Fortune, g => g.AverageTicks = 1, Tier("t", 1m))))).World.Objectives.Enabled,
                "la même fortune, avec la finance et une fenêtre, est acceptée");

            // Le calendrier : une date exige une année de départ, un jour qui existe,
            // et un seul calendrier par scénario.
            ObjectiveTierDef Dated(int year, int month, int day)
                => new() { Id = "d", Target = 1m, Deadline = new DeadlineDef { Year = year, Month = month, Day = day } };
            Refused("échéance datée sans calendrier", Deliveries("grain", "", Dated(1871, 1, 1)));
            var calendar = Deliveries("grain", "", Dated(1870, 1, 31));
            calendar.StartYear = 1870;
            Refused("le 31 n'existe pas", calendar);
            var both = Deliveries("grain", "", new ObjectiveTierDef
            {
                Id = "d", Target = 1m, Deadline = new DeadlineDef { Tick = 10, Year = 1870, Month = 1, Day = 2 },
            });
            both.StartYear = 1870;
            Refused("tick et date à la fois", both);
            var conflict = ScenarioLoader.Load(CyclePath());
            conflict.Objectives.StartYear = 1871; // events.startYear vaut 1870
            Check.Throws<InvalidDataException>(() => new Simulation(conflict), "deux calendriers qui diffèrent");

            var ok = Deliveries("grain", "", Dated(1871, 2, 1));
            ok.StartYear = 1870;
            var accepted = new Simulation(WithObjectives(DeliveryScenario(), ok));
            Check.True(accepted.World.Objectives.Goals[0].Tiers[0].DeadlineTick == 390,
                "le 1er février 1871 est le tick 390 d'une partie ouverte le 1er janvier 1870");
        });

        // -------------------------------------------------- scénarios livrés

        runner.Add("objectifs — journal de heartland-cycle et de sierra, sous les deux solveurs", () =>
        {
            // Le journal figé des deux scénarios livrés. Il est hors des empreintes de
            // ReferenceTraceTests — elles restent la preuve que le module n'a rien
            // déplacé — et se lit en clair ici. Si la partie change (un module qui
            // touche l'économie ou la finance), ces dates bougent avec elle : les
            // recopier, et dire dans le commit pourquoi elles ont bougé.
            var expected = new Dictionary<(string, string), string>
            {
                [("heartland-cycle.json", "reference")] =
                    "372:fortune/demi-million:atteint;1175:vivres/mille:atteint;1917:charbon-northgate/trois-cents:atteint;2159:fortune/million:manqué",
                [("heartland-cycle.json", "anticipating")] =
                    "356:fortune/demi-million:atteint;1169:vivres/mille:atteint;1850:charbon-northgate/trois-cents:atteint;2159:fortune/million:manqué",
                [("sierra.json", "reference")] = "4:col/premier-mois:atteint:t1",
                [("sierra.json", "anticipating")] = "4:col/premier-mois:atteint:t1",
            };
            foreach (var ((file, solver), summary) in expected)
            {
                var sim = new Simulation(ScenarioLoader.Load(Path.Combine(Fixtures.RepoRoot(), "data", file)), Solver(solver));
                sim.Run(file == "sierra.json" ? 720 : CycleTests.Ticks);
                Check.Equal(summary, sim.World.Objectives.Summary(), $"journal des objectifs de {file} (solveur {solver})");

                // Un palier atteint le reste quand la mesure redescend : la fortune du
                // magnat fond après la panique de 1873, le demi-million reste acquis.
                if (file == "heartland-cycle.json" && solver == "reference")
                {
                    var fortune = sim.World.Objectives.Goals[0];
                    Check.True(fortune.Tiers[0].Status == TierStatus.Attained && fortune.Measure < 500_000,
                        "le demi-million doit rester atteint, fortune redescendue sous la cible");
                }
            }
        });

        runner.Add("objectifs — le journal public s'écrit en CSV, une ligne par palier conclu", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(SierraPath()));
            sim.Run(10);
            string[] lines = CsvRecorder.ObjectivesCsv(sim.World).TrimEnd('\n').Split('\n');
            Check.True(lines.Length == 2, $"une ligne attendue, {lines.Length - 1} obtenue(s)");
            Check.Equal("4,col,premier-mois,connect,attained,1,,t1", lines[1], "ligne de la liaison");
        });
    }

    // --------------------------------------------------------------- outils

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);

    private static ObjectiveTierDef Tier(string id, decimal? target, int? deadlineTick = null) => new()
    {
        Id = id,
        Target = target,
        Deadline = deadlineTick is int t ? new DeadlineDef { Tick = t } : null,
    };

    private static ObjectiveDef Goal(string id, string kind, Action<ObjectiveDef> tweak, params ObjectiveTierDef[] tiers)
    {
        var goal = new ObjectiveDef { Id = id, Kind = kind, Tiers = tiers.ToList() };
        tweak(goal);
        return goal;
    }

    private static ObjectivesDef Deliveries(string cargo, string city = "", params ObjectiveTierDef[] tiers)
        => new()
        {
            Enabled = true,
            Goals = { Goal("livraisons", ObjectiveKinds.Deliveries, g => { g.Cargo = cargo; g.City = city; },
                tiers.Length > 0 ? tiers : [Tier("t", 1_000_000m)]) },
        };

    private static ScenarioDef WithObjectives(ScenarioDef scenario, ObjectivesDef objectives)
    {
        scenario.Objectives = objectives;
        return scenario;
    }

    private static LineDef Line(string id, params (string City, double Km)[] stops) => new()
    {
        Id = id,
        Name = id,
        Stops = stops.Select(s => new StopDef { City = s.City, DistanceKm = s.Km }).ToList(),
    };

    /// <summary>a-ville produit du blé, b-ville en mange ; un train de dix chargements entre les deux, à 80 km.</summary>
    private static ScenarioDef DeliveryScenario()
    {
        var scenario = Fixtures.Bare("livraisons");
        scenario.Cities.Add(Fixtures.City("a-ville", production: 1.0, stock: 40));
        scenario.Cities.Add(Fixtures.City("b-ville", demand: 0.6));
        scenario.Lines.Add(Line("ligne", ("a-ville", 0), ("b-ville", 80)));
        scenario.Trains.Add(new TrainDef { Id = "t1", Line = "ligne", Capacity = 10, SpeedKmPerTick = 40, CostPerKm = 0.2 });
        return scenario;
    }

    private static Simulation Delivery(ObjectivesDef? objectives)
    {
        var scenario = DeliveryScenario();
        if (objectives is not null) scenario.Objectives = objectives;
        return new Simulation(scenario);
    }

    /// <summary>
    /// Deux lignes qui se touchent à b-ville : t1 entre a-ville (qui produit) et
    /// b-ville (qui mange peu), t2 entre b-ville et c-ville (qui mange beaucoup, et
    /// que seul t2 dessert).
    /// </summary>
    private static Simulation Resale(ObjectivesDef objectives)
    {
        var scenario = Fixtures.Bare("revente");
        scenario.Cities.Add(Fixtures.City("a-ville", production: 1.5, stock: 40));
        scenario.Cities.Add(Fixtures.City("b-ville", demand: 0.1));
        scenario.Cities.Add(Fixtures.City("c-ville", demand: 1.0));
        scenario.Lines.Add(Line("amont", ("a-ville", 0), ("b-ville", 60)));
        scenario.Lines.Add(Line("aval", ("b-ville", 0), ("c-ville", 60)));
        scenario.Trains.Add(new TrainDef { Id = "t1", Line = "amont", Capacity = 12, SpeedKmPerTick = 60, CostPerKm = 0.1 });
        scenario.Trains.Add(new TrainDef { Id = "t2", Line = "aval", Capacity = 12, SpeedKmPerTick = 60, CostPerKm = 0.1 });
        scenario.Objectives = objectives;
        return new Simulation(scenario);
    }

    /// <summary>
    /// Empreinte de toute la partie : trace des marchés, de la compagnie et des
    /// usines, état financier de fin de partie au centime, journaux des événements
    /// et de la conjoncture. Le journal des objectifs n'y entre pas : c'est ce qu'on
    /// compare avec et sans.
    /// </summary>
    private static (string, Simulation) Fingerprint(ScenarioDef scenario, string solver, int ticks)
    {
        var sim = new Simulation(scenario, Solver(solver));
        var recorder = new CsvRecorder();
        recorder.Record(sim.World);
        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            recorder.Record(sim.World);
        }
        var ci = CultureInfo.InvariantCulture;
        var finance = sim.World.Finance;
        string money = !finance.Enabled ? "" : string.Join(";",
            finance.Player!.Cash.ToString(ci), finance.Player.BookEquity.ToString(ci),
            finance.Player.SharePrice.ToString(ci), finance.Player.Debt.ToString(ci),
            finance.Player.OverdraftBalance.ToString(ci), finance.Player.DividendsPaidTotal.ToString(ci),
            finance.Magnate!.NetWorth(finance).ToString(ci), finance.Magnate.MarginCalls.ToString(ci),
            finance.Mergers.Count.ToString(ci),
            string.Join(",", finance.Player.Bonds.Select(b => b.AnnualRatePercent.ToString(ci))));
        return (recorder.TraceFingerprint(money + sim.World.Events.Summary() + sim.World.Cycle.Summary()), sim);
    }
}
