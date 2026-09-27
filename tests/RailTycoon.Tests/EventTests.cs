using RailTycoon.Sim;
using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Events;
using RailTycoon.Sim.Finance;
using RailTycoon.Sim.Telemetry;

namespace RailTycoon.Tests;

/// <summary>
/// Invariants du module events. Même organisation que <see cref="FinanceTests"/> :
/// un fichier à part, un seul appel dans <c>Program</c>, pour qu'un module qui
/// avance en parallèle des autres n'entre pas en conflit sur la liste des tests.
/// </summary>
internal static class EventTests
{
    /// <summary>
    /// Le scénario d'épreuve du module. <c>heartland.json</c> reste la trace de
    /// régression de l'économie, sans effets extérieurs ; celui-ci reprend la même
    /// économie au caractère près et n'ajoute que le bloc <c>events</c>.
    /// </summary>
    private static string EventsPath()
        => Path.Combine(Fixtures.RepoRoot(), "data", "heartland-events.json");

    private static IEconomySolver Solver(string name)
        => name == "reference" ? new ReferenceEconomySolver() : new AnticipatingEconomySolver();

    private static readonly string[] Solvers = ["reference", "anticipating"];

    public static void Register(TestRunner runner)
    {
        // ---------------------------------------------------------- neutralité

        runner.Add("événements — désactivés, heartland-events rejoue heartland au bit près", () =>
        {
            // La neutralité ne se prouve pas seulement par l'absence du bloc — les
            // traces de référence des scénarios existants le font déjà —, mais par
            // un bloc PLEIN et désactivé : cinq historiques et dix types aléatoires
            // présents, enabled = false. Rien ne doit s'ouvrir, rien ne doit se
            // tirer, aucun multiplicateur ne doit quitter 1. Sous les deux solveurs,
            // parce que chacun compose ses taux du jour à sa façon.
            foreach (string solver in Solvers)
            {
                var disabled = ScenarioLoader.Load(EventsPath());
                Check.True(disabled.Events.Historical.Count > 0 && disabled.Events.Random.Count > 0,
                    "le test perdrait son sens si le bloc était vide");
                disabled.Events.Enabled = false;

                Check.Equal(
                    TraceFingerprint(ScenarioLoader.Load(Fixtures.HeartlandPath()), solver),
                    TraceFingerprint(disabled, solver),
                    $"empreinte de heartland et de heartland-events désactivé (solveur {solver})");
            }
        });

        runner.Add("événements — le scénario de régression de l'économie n'en déclare pas", () =>
        {
            // Décision protégée, comme pour la finance : tous les chiffres de
            // docs/FINDINGS.md supposent heartland exempt d'effets extérieurs.
            var reference = ScenarioLoader.Load(Fixtures.HeartlandPath());
            Check.True(!reference.Events.Enabled, "heartland.json ne doit pas activer les événements");
            Check.True(reference.Events.Historical.Count == 0 && reference.Events.Random.Count == 0,
                "heartland.json ne doit porter aucun événement, même désactivé");

            var dedicated = ScenarioLoader.Load(EventsPath());
            Check.True(dedicated.Events.Enabled, "heartland-events.json doit activer le module");

            // Et les deux décrivent la même économie : seuls l'identité et le bloc
            // events ont le droit de différer. La comparaison porte sur le contenu
            // sérialisé, pas sur des comptes d'éléments — c'est ce qui a attrapé le
            // bloc anticipating manquant de heartland-finance.
            string Economy(ScenarioDef scenario)
            {
                scenario.Id = "";
                scenario.Name = "";
                scenario.Events = new EventsDef();
                return System.Text.Json.JsonSerializer.Serialize(scenario);
            }

            Check.True(Economy(reference) == Economy(dedicated),
                "heartland.json et heartland-events.json doivent décrire la même économie, " +
                "à l'identité et au bloc events près");
        });

        // ------------------------------------------------------- historiques

        runner.Add("événements — un événement historique agit à sa date, sur sa cible seule, puis s'éteint", () =>
        {
            foreach (string solver in Solvers)
            {
                var sim = new Simulation(Staged(), Solver(solver));
                var world = sim.World;
                var target = world.CityById("b-ville").Market("grain");

                for (int i = 0; i < 45; i++)
                {
                    sim.Step();
                    int t = world.Tick.Index;

                    double expected = t switch
                    {
                        // Montée de deux ticks : 1/3 puis 2/3 du plein effet, plateau,
                        // descente symétrique. Le premier et le dernier tick agissent
                        // déjà : une durée de 11 est bien de 11 ticks.
                        20 or 30 => 1.0 + 1.0 / 3.0,
                        21 or 29 => 1.0 + 2.0 / 3.0,
                        >= 22 and <= 28 => 2.0,
                        _ => 1.0,
                    };

                    Check.Near(expected, target.EventDemandFactor, 1e-12,
                        $"{solver}, tick {t} : multiplicateur de la cible");
                    Check.Near(target.NominalDemandRate * expected, target.BaseDemandRate, 1e-12,
                        $"{solver}, tick {t} : le solveur doit composer le multiplicateur dans le taux du jour");

                    foreach (var city in world.Cities)
                        foreach (var market in world.MarketsOf(city))
                        {
                            Check.True(market.EventProductionFactor == 1.0,
                                $"{solver}, tick {t} : {city.Id}/{market.CargoId} production touchée à tort");
                            if (market != target)
                                Check.True(market.EventDemandFactor == 1.0,
                                    $"{solver}, tick {t} : {city.Id}/{market.CargoId} demande touchée à tort");
                        }

                    if (t > 30)
                    {
                        Check.True(world.Events.Active.Count == 0 || world.Events.Active.All(e => e.EndTick >= t),
                            $"{solver}, tick {t} : un événement éteint reste actif");
                        Check.True(target.BaseDemandRate == target.NominalDemandRate,
                            $"{solver}, tick {t} : le taux doit revenir exactement au nominal");
                    }
                }

                Check.True(world.Events.Journal.Count == 1, "un seul événement attendu au journal");
                var record = world.Events.Journal[0];
                Check.True(record.StartTick == 20 && record.EndTick == 30,
                    $"journal : du tick {record.StartTick} au tick {record.EndTick}, attendu 20 à 30");
                Check.True(record.Origin == EventOrigin.Inspired, "l'origine déclarée doit être reprise au journal");
                Check.True(record.Targets.Count == 1 && record.Targets[0].CityId == "b-ville",
                    "la cible du journal doit être la seule ville visée");
            }
        });

        runner.Add("événements — la consommation de la cible suit le multiplicateur, la matière reste conservée", () =>
        {
            // Même scénario avec et sans l'événement : pendant le plateau, la ville
            // visée consomme davantage, les autres pas moins — l'événement change un
            // taux, il ne déplace pas de la marchandise à la main. Et le bilan matière
            // tient : rien ne naît ni ne meurt hors de Produce et Consume.
            var calm = Staged();
            calm.Events.Historical.Clear();
            calm.Events.Enabled = false;

            var withEvent = new Simulation(Staged());
            var without = new Simulation(calm);
            double initial = Invariants.InitialStockTotal(withEvent.World);
            double consumedWith = 0, consumedWithout = 0;

            for (int i = 0; i < 45; i++)
            {
                withEvent.Step();
                without.Step();
                int t = withEvent.World.Tick.Index;
                if (t >= 22 && t <= 28)
                {
                    consumedWith += withEvent.World.CityById("b-ville").Market("grain").ConsumedThisTick;
                    consumedWithout += without.World.CityById("b-ville").Market("grain").ConsumedThisTick;
                }
                var violations = Invariants.Check(withEvent.World, initial);
                Check.True(violations.Count == 0,
                    $"tick {t} : {(violations.Count > 0 ? violations[0].Rule + " — " + violations[0].Detail : "")}");
            }

            Check.Less(consumedWithout * 1.5, consumedWith,
                "pendant le plateau, la ville visée doit consommer nettement plus que sans l'événement");
        });

        runner.Add("événements — une date calendaire tombe au tick attendu", () =>
        {
            // Douze mois de trente jours, tick 0 = 1er janvier de startYear. Les
            // dates de heartland-events en dépendent : le grand incendie de Chicago,
            // le 8 octobre 1871, doit tomber au tick 360 + 9 × 30 + 7 = 637.
            var def = new EventsDef { StartYear = 1870 };
            Check.True(ReferenceEventSolver.StartTickOf(def, Dated(1870, 1, 1)) == 0, "1er janvier 1870");
            Check.True(ReferenceEventSolver.StartTickOf(def, Dated(1871, 10, 8)) == 637, "8 octobre 1871");
            Check.True(ReferenceEventSolver.StartTickOf(def, Dated(1873, 9, 18)) == 1337, "18 septembre 1873");

            var world = new Simulation(ScenarioLoader.Load(EventsPath())).World;
            Check.True(world.Events.Enabled, "heartland-events doit activer le module");
            var fire = world.Def.Events.Historical.Single(h => h.Id == "grand-incendie-1871");
            Check.True(ReferenceEventSolver.StartTickOf(world.Def.Events, fire) == 637,
                "le grand incendie doit tomber au tick 637");
        });

        runner.Add("événements — un événement historique sans source ou mal daté est refusé au chargement", () =>
        {
            Check.Throws<InvalidDataException>(() => new Simulation(Staged(h => { h.Basis = "historical"; h.Source = ""; })),
                "« historique » sans source doit être refusé : le déclarer « inspired » ou le sourcer");
            Check.Throws<InvalidDataException>(() => new Simulation(Staged(h => h.Basis = "legendary")),
                "une nature inconnue doit être refusée");
            Check.Throws<InvalidDataException>(() => new Simulation(Staged(h =>
                { h.StartTick = null; h.Year = 1870; h.Month = 1; h.Day = 31; })),
                "un 31 n'existe pas dans le calendrier de jeu");
            Check.Throws<InvalidDataException>(() => new Simulation(Staged(h => h.Cities = ["a-ville"])),
                "une ville qui ne consomme pas ce que l'événement touche doit être refusée");
            Check.Throws<InvalidDataException>(() => new Simulation(Staged(h => h.RampTicks = 6)),
                "une montée et une descente plus longues que l'événement doivent être refusées");
        });

        // ---------------------------------------------------------- aléatoires

        runner.Add("événements — intensités et durées aléatoires restent dans leurs bornes", () =>
        {
            // Fréquences multipliées par quatre pour exercer les bornes sur plus de
            // cent cinquante tirages, et le chevauchement des événements.
            var scenario = ScenarioLoader.Load(EventsPath());
            foreach (var type in scenario.Events.Random) type.OccurrencesPerYear *= 4;
            var sim = new Simulation(scenario);
            var events = scenario.Events;

            for (int i = 0; i < 720; i++)
            {
                sim.Step();
                foreach (var city in sim.World.Cities)
                    foreach (var market in sim.World.MarketsOf(city))
                    {
                        foreach (double f in new[] { market.EventDemandFactor, market.EventProductionFactor })
                            Check.True(f >= events.MinFactor && f <= events.MaxFactor,
                                $"tick {sim.World.Tick.Index} : {city.Id}/{market.CargoId} multiplicateur {f} hors des bornes de sécurité");
                    }
            }

            var drawn = sim.World.Events.Journal.Where(e => e.Origin == EventOrigin.Random).ToList();
            Check.True(drawn.Count >= 100, $"trop peu de tirages pour éprouver les bornes : {drawn.Count}");

            foreach (var e in drawn)
            {
                var type = events.Random.Single(t => t.Id == e.DefinitionId);
                Check.True(e.DurationTicks >= type.DurationMinTicks && e.DurationTicks <= type.DurationMaxTicks,
                    $"{e.InstanceId} : durée {e.DurationTicks} hors de [{type.DurationMinTicks}, {type.DurationMaxTicks}]");
                Check.True(e.RampTicks <= type.RampTicks && 2 * e.RampTicks < e.DurationTicks,
                    $"{e.InstanceId} : montée {e.RampTicks} pour une durée {e.DurationTicks}");
                Check.True(type.Months.Count == 0 || type.Months.Contains(new SimTick(e.StartTick).DayOfYear / 30 + 1),
                    $"{e.InstanceId} : commence hors de sa fenêtre de mois");
                Check.True(e.Targets.Count > 0, $"{e.InstanceId} : aucune cible");
                foreach (var target in e.Targets)
                {
                    var effect = type.Effects.Single(x => x.Cargo == target.Cargo && x.On == target.On);
                    Check.True(target.PeakFactor >= effect.FactorMin && target.PeakFactor <= effect.FactorMax,
                        $"{e.InstanceId} : multiplicateur {target.PeakFactor} hors de [{effect.FactorMin}, {effect.FactorMax}]");
                    Check.True(type.Cities.Count == 0 || type.Cities.Contains(target.CityId),
                        $"{e.InstanceId} : {target.CityId} n'est pas une cible admissible");
                }
            }

            // Pas d'empilement d'une même définition sur une même ville.
            foreach (var group in drawn.SelectMany(e => e.Targets.Select(t => (e, t.CityId)))
                         .GroupBy(x => (x.e.DefinitionId, x.CityId)))
            {
                var spans = group.Select(x => x.e).OrderBy(e => e.StartTick).ToList();
                for (int k = 1; k < spans.Count; k++)
                    Check.True(spans[k].StartTick > spans[k - 1].EndTick,
                        $"{spans[k].InstanceId} s'empile sur {spans[k - 1].InstanceId} à {group.Key.CityId}");
            }
        });

        runner.Add("événements — changer un réglage ne déplace pas le calendrier des aléas", () =>
        {
            // Chaque type tire quatre nombres par jour, qu'il se déclenche ou non :
            // les dates et les cibles ne dépendent pas des bornes d'intensité, et le
            // calendrier d'un type ne dépend pas de la fréquence d'un autre. C'est ce
            // qui rend les balayages de docs/FINDINGS.md lisibles — deux réglages
            // comparés jouent le même calendrier, à la variable étudiée près.
            string Calendar(Action<EventsDef> tweak, string? skipType = null)
            {
                var scenario = ScenarioLoader.Load(EventsPath());
                tweak(scenario.Events);
                var sim = new Simulation(scenario);
                sim.Run(720);
                return string.Join(";", sim.World.Events.Journal
                    .Where(e => e.DefinitionId != skipType)
                    .Select(e => $"{e.InstanceId}@{e.StartTick}-{e.EndTick}:{string.Join(",", e.Targets.Select(t => t.CityId))}"));
            }

            // Le premier type tire avant tous les autres : si le nombre de tirages
            // dépendait de ses déclenchements, tripler sa fréquence décalerait le
            // flux de tous les suivants.
            string first = ScenarioLoader.Load(EventsPath()).Events.Random[0].Id;
            Check.Equal(
                Calendar(_ => { }, first),
                Calendar(d => d.Random[0].OccurrencesPerYear *= 3, first),
                $"calendrier des autres types quand la fréquence de « {first} » triple");

            string shipped = Calendar(_ => { });
            string halved = Calendar(d =>
            {
                foreach (var effect in d.Random.SelectMany(t => t.Effects))
                {
                    effect.FactorMin = 1 + 0.5 * (effect.FactorMin - 1);
                    effect.FactorMax = 1 + 0.5 * (effect.FactorMax - 1);
                }
            });
            Check.True(shipped.Length > 0, "aucun événement tiré");
            Check.Equal(shipped, halved, "calendrier des aléas avec des intensités réduites de moitié");
        });

        runner.Add("événements — le catalogue aléatoire de heartland-events est équilibré", () =>
        {
            // Décision de conception protégée : un catalogue qui ne frappe que dans
            // un sens mange ou gonfle le surplus d'une marchandise, et le résultat du
            // transporteur le suit sans que la dispersion bouge. Le premier jet —
            // des afflux d'ouvriers sans épidémie, +2 % de demande de nourriture —
            // coûtait 41 000 de résultat net. Voir docs/FINDINGS.md.
            var biases = EventCatalogBalance.Compute(ScenarioLoader.Load(EventsPath()));
            Check.True(biases.Count >= 5, "le catalogue devrait toucher au moins cinq leviers");
            foreach (var b in biases)
            {
                Check.True(b.Up > 0 && b.Down < 0,
                    $"{b.Cargo}/{b.On} : il faut des hausses et des baisses ({b.Up:0.###} / {b.Down:0.###})");
                Check.Near(0, b.Bias, 0.002, $"{b.Cargo}/{b.On} : biais annuel attendu du catalogue");
            }

            // Et l'outil voit bien un catalogue à sens unique.
            var lopsided = ScenarioLoader.Load(EventsPath());
            lopsided.Events.Random.RemoveAll(t => t.Id == "epidemie");
            var food = EventCatalogBalance.Compute(lopsided).Single(b => b.Cargo == "food" && b.On == "demand");
            Check.Less(0.015, food.Bias, "sans les épidémies, la demande de nourriture doit ressortir biaisée à la hausse");
        });

        // ------------------------------------------------------- déterminisme

        runner.Add("événements — activer les événements ne décale pas le flux aléatoire de la finance", () =>
        {
            // Scénario de finance, plus un catalogue d'événements qui se déclenchent
            // et tirent leurs nombres chaque jour, mais à multiplicateur exactement 1 :
            // l'économie ne voit rien. Si le module tirait sur le flux de la finance
            // — ou sur celui du monde —, le bruit sur le résultat des concurrents
            // serait décalé, donc leurs cours, donc l'OPA : l'état financier de fin
            // de partie changerait au centime.
            var financePath = Path.Combine(Fixtures.RepoRoot(), "data", "heartland-finance.json");
            var plain = ScenarioLoader.Load(financePath);
            var withEvents = ScenarioLoader.Load(financePath);
            withEvents.Events = ScenarioLoader.Load(EventsPath()).Events;
            withEvents.Events.Historical.Clear();
            foreach (var effect in withEvents.Events.Random.SelectMany(t => t.Effects))
            {
                effect.FactorMin = 1.0;
                effect.FactorMax = 1.0;
            }

            var a = new Simulation(plain);
            var b = new Simulation(withEvents);
            var ra = new CsvRecorder();
            var rb = new CsvRecorder();
            for (int i = 0; i < 720; i++)
            {
                a.Step(); ra.Record(a.World);
                b.Step(); rb.Record(b.World);
            }

            Check.True(b.World.Events.Journal.Count >= 20,
                $"les événements doivent réellement se tirer pendant la course ({b.World.Events.Journal.Count})");
            Check.Equal(ra.TraceFingerprint(FinanceState(a.World)), rb.TraceFingerprint(FinanceState(b.World)),
                "traces et état financier de fin de partie, sans puis avec événements neutres");

            // Personne ne tire sur le flux du monde : son prochain nombre est encore
            // le premier de la séquence.
            Check.True(b.World.Rng.NextUInt() == new DeterministicRandom(withEvents.Seed).NextUInt(),
                "le module events a tiré sur le flux du monde");
        });

        runner.Add("événements — une séquence partagée avec la finance est refusée", () =>
        {
            var scenario = ScenarioLoader.Load(Path.Combine(Fixtures.RepoRoot(), "data", "heartland-finance.json"));
            scenario.Events = new EventsDef { Enabled = true, RandomSequence = scenario.Finance.RandomSequence };
            Check.Throws<InvalidDataException>(() => new Simulation(scenario),
                "deux modules sur la même séquence tireraient le même flux");
        });

        // ---------------------------------------------------------- invariants

        runner.Add("événements — 720 ticks sans violation d'invariant, sous les deux solveurs", () =>
        {
            foreach (string solver in Solvers)
            {
                var sim = new Simulation(ScenarioLoader.Load(EventsPath()), Solver(solver));
                double initial = Invariants.InitialStockTotal(sim.World);
                for (int i = 0; i < 720; i++)
                {
                    sim.Step();
                    var violations = Invariants.Check(sim.World, initial);
                    if (violations.Count > 0)
                        Check.True(false,
                            $"{solver}, tick {sim.World.Tick.Index} : {violations[0].Rule} — {violations[0].Detail}");
                }

                var journal = sim.World.Events.Journal;
                Check.True(journal.Count(e => e.Origin == EventOrigin.Historical) == 3,
                    $"{solver} : trois événements historiques attendus dans les 720 ticks (la panique de 1873 vient après)");
                Check.True(journal.Any(e => e.Origin == EventOrigin.Inspired), $"{solver} : la sécheresse doit s'être ouverte");
                Check.True(journal.Count(e => e.Origin == EventOrigin.Random) >= 20,
                    $"{solver} : trop peu d'aléas pour que le test éprouve quoi que ce soit");
            }
        });

        runner.Add("événements — le journal public s'écrit en CSV, une ligne par cible", () =>
        {
            var sim = new Simulation(ScenarioLoader.Load(EventsPath()));
            sim.Run(720);
            string[] lines = CsvRecorder.EventsCsv(sim.World).TrimEnd('\n').Split('\n');
            int targets = sim.World.Events.Journal.Sum(e => e.Targets.Count);
            Check.True(lines[0].StartsWith("instance,event,name,origin,start_tick,end_tick"), "en-tête du journal");
            Check.True(lines.Length == targets + 1, $"{lines.Length - 1} lignes pour {targets} cibles");
            Check.True(lines.Any(l => l.StartsWith("greve-anthracite-1871,") && l.Contains(",historical,369,530,")),
                "la grève de l'anthracite doit figurer au journal, du tick 369 au tick 530");

            var quiet = new Simulation(ScenarioLoader.Load(Fixtures.HeartlandPath()));
            quiet.Run(10);
            Check.True(CsvRecorder.EventsCsv(quiet.World).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 1,
                "sans module, le journal ne contient que son en-tête");
        });
    }

    /// <summary>
    /// Trois villes, du blé : a-ville le produit, b-ville et c-ville le consomment.
    /// Un seul événement, inspiré, qui double la demande de b-ville du tick 20 au
    /// tick 30, avec deux ticks de montée et de descente.
    /// </summary>
    private static ScenarioDef Staged(Action<HistoricalEventDef>? tweak = null)
    {
        var scenario = Fixtures.Bare("evenement-date");
        scenario.Cities.Add(Fixtures.City("a-ville", production: 1.0, stock: 40));
        scenario.Cities.Add(Fixtures.City("b-ville", demand: 0.4, stock: 30));
        scenario.Cities.Add(Fixtures.City("c-ville", demand: 0.3, stock: 30));

        var h = new HistoricalEventDef
        {
            Id = "essai",
            Name = "Essai",
            Basis = "inspired",
            StartTick = 20,
            DurationTicks = 11,
            RampTicks = 2,
            Cities = ["b-ville"],
            Effects = { new EventEffectDef { Cargo = "grain", On = "demand", Factor = 2.0 } },
        };
        tweak?.Invoke(h);
        scenario.Events = new EventsDef { Enabled = true, Historical = { h } };
        return scenario;
    }

    private static HistoricalEventDef Dated(int year, int month, int day)
        => new() { Year = year, Month = month, Day = day };

    private static string TraceFingerprint(ScenarioDef scenario, string solver)
    {
        var sim = new Simulation(scenario, Solver(solver));
        var recorder = new CsvRecorder();
        recorder.Record(sim.World);
        for (int i = 0; i < 720; i++)
        {
            sim.Step();
            recorder.Record(sim.World);
        }
        return recorder.TraceFingerprint(sim.World.Events.Summary());
    }

    private static string FinanceState(WorldState world)
    {
        var finance = world.Finance;
        var player = finance.Player!;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return string.Join(";",
            player.Cash.ToString(ci), player.BookEquity.ToString(ci), player.SharePrice.ToString(ci),
            player.Debt.ToString(ci), finance.Magnate!.NetWorth(finance).ToString(ci),
            finance.Mergers.Count.ToString(ci),
            string.Join(",", finance.Companies.Select(c => c.OperatingResultTotal.ToString(ci))));
    }
}
