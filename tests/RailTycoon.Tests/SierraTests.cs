using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Telemetry;

namespace RailTycoon.Tests;

/// <summary>
/// Le premier scénario où relief et économie tournent ensemble
/// (<c>data/sierra.json</c>). Ses tests vivent à part, comme ceux de la finance :
/// une liste de tests commune est le fichier qui produit un conflit de fusion à
/// chaque ligne ajoutée.
/// <para>
/// Ils vérifient trois choses de nature différente : que le scénario est
/// réalisable et sain (bilan, invariants, économie vivante) ; que son bloc
/// <c>anticipating</c> fait ce que son commentaire prétend ; et un <em>constat</em>
/// — le relief se paie mais n'entre dans aucune décision — qui est l'état actuel
/// du code, pas une propriété voulue. Voir docs/FINDINGS.md, « Relief et économie
/// ensemble ».
/// </para>
/// </summary>
internal static class SierraTests
{
    private const int Ticks = 720;
    private const int Warmup = 90;

    private static string SierraPath() => Path.Combine(Fixtures.RepoRoot(), "data", "sierra.json");

    public static void Register(TestRunner runner)
    {
        runner.Add("sierra — le scénario est équilibré offre/demande", () =>
        {
            foreach (var b in BalanceReport.Compute(ScenarioLoader.Load(SierraPath())))
                Check.True(b.Ratio >= 0.95 && b.Ratio <= 1.5,
                    $"{b.CargoName} : offre/demande = {b.Ratio:0.00} ({b.Verdict})");
        });

        runner.Add("sierra — la montagne se franchit au col, sans ouvrage d'art", () =>
        {
            // Le devis est une valeur de référence au même titre que ceux des cartes
            // d'essai : s'il bouge, le relief ou le géomètre a changé, et les
            // chiffres de FINDINGS.md avec lui. Le col est posé juste sous la rampe
            // maximale : un col qui exigerait des viaducs mesurerait le devis, pas
            // le couplage réseau ↔ économie qu'on étudie ici.
            var world = new Simulation(ScenarioLoader.Load(SierraPath())).World;
            var network = world.Network ?? throw new AssertionException("sierra doit déclarer un réseau");
            Check.Near(1_524_565.86, network.BuiltCost, 0.5, "devis total du réseau");

            double limit = world.Def.Network.Alignment.MaxGradePercent;
            foreach (var edge in network.Graph.Edges)
            {
                Check.True(edge.Construction.Structures.Count == 0,
                    $"{edge.Id} : {edge.Construction.Structures.Count} ouvrage(s), le col devait suffire");
                Check.True(edge.Construction.MaxGradePercent <= limit + 1e-6,
                    $"{edge.Id} : {edge.Construction.MaxGradePercent:0.###} % > {limit:0.###} %");
            }

            // Le relief doit réellement peser sur l'exploitation, et dans un seul
            // sens : on monte au col depuis l'est (961 m), on en descend vers l'est.
            var line = world.Lines.Single();
            int col = line.Stops.FindIndex(s => s.CityId == "coalpass");
            Check.True(line.LegCostFactor(col + 1, col) > 1.4,
                $"la rampe est doit coûter plus de 40 % de plus à la montée, facteur = {line.LegCostFactor(col + 1, col):0.000}");
            Check.Less(line.LegCostFactor(col, col + 1), 1.05,
                "la même rampe à la descente ne doit presque rien coûter de plus");
        });

        foreach (var solver in new[] { "reference", "anticipating" })
        {
            runner.Add($"sierra — {Ticks} ticks sans violation d'invariant, solveur {solver}", () =>
            {
                var sim = new Simulation(ScenarioLoader.Load(SierraPath()), Solver(solver));
                double initial = Invariants.InitialStockTotal(sim.World);
                for (int i = 0; i < Ticks; i++)
                {
                    sim.Step();
                    var violations = Invariants.Check(sim.World, initial);
                    if (violations.Count > 0)
                        Check.True(false,
                            $"tick {sim.World.Tick.Index} : {violations[0].Rule} — {violations[0].Detail}");
                }
            });

            runner.Add($"sierra — l'économie sur relief reste vivante, solveur {solver}", () =>
            {
                // Même exigence que pour heartland : un relief qui arrêterait les
                // usines ou collerait les prix au plafond n'aurait rien appris. La
                // scierie de Cedarton, à 355 km de sa forêt, est la seule autorisée
                // à l'arrêt — c'est le rayon économique, pas un défaut du scénario.
                var (sim, stats) = Run(ScenarioLoader.Load(SierraPath()), Solver(solver));
                var w = sim.World;

                var idle = new List<string>();
                foreach (var city in w.Cities)
                    foreach (var industry in city.Industries)
                        if (stats.MeanUtilization(city.Id, industry.Recipe.Id) <= 0.5)
                            idle.Add($"{city.Id}/{industry.Recipe.Id}");
                Check.True(idle.Count <= 1 && idle.All(i => i == "cedarton/sawmill"),
                    "seule la scierie de Cedarton peut rester à l'arrêt ; à l'arrêt : " + string.Join(", ", idle));

                foreach (string cargoId in w.CargoOrder)
                {
                    double ceiling = stats.MeanCeilingFraction(w, cargoId);
                    Check.True(ceiling <= 0.4,
                        $"{w.Cargo(cargoId).Name} : {ceiling * 100:0} % du temps au plafond — " +
                        "le prix ne porte plus d'information");
                }

                double km = w.Trains.Sum(t => t.TotalKmTravelled);
                double marginPerKm = w.Company.NetProfit / km;
                Check.True(marginPerKm > 0, $"le transport doit être rentable, marge = {marginPerKm:0.00}/km");
                Check.Less(marginPerKm, 3.0,
                    $"marge de {marginPerKm:0.00}/km invraisemblable — chercher une faille d'arbitrage");
            });
        }

        runner.Add("sierra — le bloc anticipating réglé bat la référence sans perdre de résultat", () =>
        {
            // Ce que revendique le commentaire du bloc : mesures du 27 septembre
            // 2026, trois trains, 720 ticks —
            //   référence   résultat 219 411   mobilité 0,384
            //   anticipant  résultat 242 254   mobilité 0,548
            // Les seuils gardent de la marge : ils protègent contre une régression,
            // ils ne consacrent pas ces chiffres exacts. Le bloc de heartland, recopié
            // tel quel, échouerait ici : 100 693 de résultat.
            var scenario = ScenarioLoader.Load(SierraPath());
            var (refSim, refStats) = Run(scenario, new ReferenceEconomySolver());
            var (sim, stats) = Run(scenario, new AnticipatingEconomySolver());

            double refMobility = refStats.MeanSpreadMobility(refSim.World);
            double mobility = stats.MeanSpreadMobility(sim.World);
            Check.True(mobility > refMobility * 1.25,
                $"mobilité {mobility:0.000} contre {refMobility:0.000} pour la référence");
            Check.True(sim.World.Company.NetProfit >= refSim.World.Company.NetProfit,
                $"résultat {sim.World.Company.NetProfit:0} contre {refSim.World.Company.NetProfit:0} pour la référence");
        });

        runner.Add("sierra — constat : le relief se paie, mais n'entre dans aucune décision", () =>
        {
            // Ce test fige un état du code, pas une intention. Le relief atteint le
            // transport par un seul canal — le coût kilométrique facturé — et ce
            // canal ne remonte dans aucune décision : le transporteur choisit son
            // fret sur des distances plates, et les trains roulent de toute façon.
            // Conséquence mesurée : la trace des marchés est la même au bit près,
            // que la montagne coûte ou non. Si ce test échoue parce que le relief
            // entre désormais dans les décisions, c'est une bonne nouvelle : mettre
            // à jour docs/FINDINGS.md (« Relief et économie ensemble »), qui chiffre
            // les options, et retirer ce test.
            var scenario = ScenarioLoader.Load(SierraPath());
            var flat = ScenarioLoader.Load(SierraPath());
            flat.Network.Traction.ClimbEquivalentKm = 0;

            var (withRelief, reliefMarkets) = Trace(scenario);
            var (without, flatMarkets) = Trace(flat);

            Check.Equal(flatMarkets, reliefMarkets,
                "la trace des marchés ne dépend pas du coût du relief (état actuel)");
            double surcharge = withRelief.World.Company.TotalOperatingCost / without.World.Company.TotalOperatingCost - 1;
            Check.True(surcharge > 0.05,
                $"le relief doit pourtant coûter à l'exploitation, surcoût = {surcharge * 100:0.0} %");
        });
    }

    private static IEconomySolver Solver(string name) => name switch
    {
        "reference" => new ReferenceEconomySolver(),
        "anticipating" => new AnticipatingEconomySolver(),
        _ => throw new ArgumentException($"solveur inconnu : {name}"),
    };

    /// <summary>Même protocole que les mesures de FINDINGS : 720 ticks, 90 de chauffe exclus.</summary>
    private static (Simulation Sim, RunStatistics Stats) Run(ScenarioDef scenario, IEconomySolver solver)
    {
        var sim = new Simulation(scenario, solver);
        var stats = new RunStatistics { WarmupTicks = Warmup };
        for (int i = 0; i < Ticks; i++)
        {
            sim.Step();
            stats.Sample(sim.World);
        }
        return (sim, stats);
    }

    private static (Simulation Sim, string Markets) Trace(ScenarioDef scenario)
    {
        var sim = new Simulation(scenario);
        var recorder = new CsvRecorder();
        recorder.Record(sim.World);
        for (int i = 0; i < Ticks; i++)
        {
            sim.Step();
            recorder.Record(sim.World);
        }
        return (sim, recorder.Fingerprint());
    }
}
