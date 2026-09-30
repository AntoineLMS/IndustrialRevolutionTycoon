using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Telemetry;
using RailTycoon.Sim.Transport;

namespace RailTycoon.Tests;

/// <summary>
/// Le modèle de coût d'exploitation « mass » (<c>haulage.costModel</c>,
/// <c>Transport/TrainCost.cs</c>) et son scénario, <c>data/sierra-marginal.json</c>.
/// <para>
/// Ce que ces tests protègent tient en une phrase : sous ce modèle, ce que le
/// transporteur croit qu'un chargement lui coûtera est exactement ce qu'il lui
/// coûte. Les tests de trajet le vérifient sur la simulation elle-même — un train,
/// un tick, aucune transaction possible —, pas sur les fonctions de
/// <see cref="TrainCost"/> prises isolément : c'est l'écart entre deux chemins de
/// code (<c>MoveTrain</c> et <c>HaulCostPerUnitAhead</c>) qui a laissé le relief
/// hors des décisions, et c'est cet écart qu'il faut attraper.
/// </para>
/// <para>
/// Et au réglage par défaut, rien ne bouge : c'est l'autre moitié du contrat, et les
/// empreintes de <see cref="ReferenceTraceTests"/> la vérifient déjà ; le test de
/// neutralité ci-dessous ajoute que les masses déclarées sous <c>flat</c> ne sont
/// lues nulle part.
/// </para>
/// </summary>
internal static class MarginalCostTests
{
    private const int Ticks = 720;

    private static string MarginalPath() => Path.Combine(Fixtures.RepoRoot(), "data", "sierra-marginal.json");
    private static string SierraPath() => Path.Combine(Fixtures.RepoRoot(), "data", "sierra.json");

    public static void Register(TestRunner runner)
    {
        runner.Add("coût marginal — sierra-marginal décrit l'économie de sierra au caractère près", () =>
        {
            // Même garde que heartland-finance et heartland-events : la comparaison
            // porte sur le contenu sérialisé, et seuls l'identité et le modèle de
            // coût ont le droit de différer. Sans elle, les écarts mesurés entre les
            // deux scénarios ne diraient rien du modèle de coût.
            var sierra = ScenarioLoader.Load(SierraPath());
            var marginal = ScenarioLoader.Load(MarginalPath());
            Check.Equal(TrainCost.FlatModel, sierra.Haulage.CostModel, "sierra.json reste sous le modèle par défaut");
            Check.Equal(TrainCost.MassModel, marginal.Haulage.CostModel, "sierra-marginal.json active le modèle mass");

            string Economy(ScenarioDef scenario)
            {
                scenario.Id = "";
                scenario.Name = "";
                scenario.Haulage.CostModel = TrainCost.FlatModel;
                scenario.Haulage.MassCost = new MassCostDef();
                return System.Text.Json.JsonSerializer.Serialize(scenario);
            }

            Check.True(Economy(sierra) == Economy(marginal),
                "sierra.json et sierra-marginal.json doivent décrire la même économie, " +
                "à l'identité et au modèle de coût près");
        });

        runner.Add("coût marginal — le coût facturé égale le coût décidé, dans les deux sens du col", () =>
        {
            // De Pinecrest à Cedarton on gravit le col par l'ouest ; en sens inverse,
            // par l'est, où la rampe est plus haute. Dans les deux cas, dix chargements
            // de plus doivent coûter à la facture dix fois ce que le transporteur leur
            // a imputé avant de partir — ni plus, ni moins.
            foreach (var (from, to) in new[] { ("pinecrest", "cedarton"), ("cedarton", "pinecrest"), ("westbrook", "farport") })
            {
                var empty = Trip(ScenarioLoader.Load(MarginalPath()), from, to, load: 0);
                var loaded = Trip(ScenarioLoader.Load(MarginalPath()), from, to, load: 10);

                double extra = loaded.Billed - empty.Billed;
                Check.True(loaded.Decided > 0, $"{from} → {to} : le coût décidé doit être positif");
                Check.Near(10 * loaded.Decided, extra, 1e-9 * loaded.Billed,
                    $"{from} → {to} : surcoût facturé de dix chargements contre dix fois le coût décidé");
            }

            // Le relief est bien dans la décision : le même trajet ne coûte pas pareil
            // dans les deux sens, et coûte davantage que sur le plat.
            var east = Trip(ScenarioLoader.Load(MarginalPath()), "pinecrest", "cedarton", load: 10);
            var west = Trip(ScenarioLoader.Load(MarginalPath()), "cedarton", "pinecrest", load: 10);
            var flat = ScenarioLoader.Load(MarginalPath());
            flat.Network.Traction.ClimbEquivalentKm = 0;
            var eastFlat = Trip(flat, "pinecrest", "cedarton", load: 10);
            Check.Less(eastFlat.Decided, east.Decided, "le col doit renchérir le coût décidé vers l'est");
            Check.Less(east.Decided, west.Decided, "la rampe est, plus haute, doit coûter davantage vers l'ouest");
        });

        runner.Add("coût marginal — sous le modèle flat, un chargement ne coûte rien de plus à porter", () =>
        {
            // Le constat de « Relief et économie ensemble », vu du trajet : sous le
            // modèle par défaut, la facture ne dépend pas de la charge, et le coût
            // décidé n'est pas celui qu'on paie. C'est ce que le modèle mass corrige ;
            // si ce test casse, le défaut a changé, et c'est une décision de l'équipe.
            var empty = Trip(ScenarioLoader.Load(SierraPath()), "pinecrest", "cedarton", load: 0);
            var loaded = Trip(ScenarioLoader.Load(SierraPath()), "pinecrest", "cedarton", load: 10);
            Check.Near(empty.Billed, loaded.Billed, 1e-9 * empty.Billed, "facture à vide contre facture chargée");
            Check.True(loaded.Decided > 0, "le transporteur impute pourtant un coût à chaque chargement");
        });

        runner.Add("coût marginal — un train vide paie la seule part fixe, relief compris", () =>
        {
            var scenario = ScenarioLoader.Load(MarginalPath());
            var mass = scenario.Haulage.MassCost;
            var trainDef = scenario.Trains[0];

            // Recalculée ici depuis les données, sans passer par TrainCost : une erreur
            // dans la formule des tarifs doit se voir, pas se retrouver des deux côtés.
            double tare = mass.LocomotiveTonnes + trainDef.Capacity * mass.WagonTareTonnes;
            double calibrationMass = tare + mass.CalibrationLoadFactor * trainDef.Capacity * mass.TonnesPerLoad;
            double fixedPerKm = trainDef.CostPerKm * tare / calibrationMass;
            Check.Less(fixedPerKm, trainDef.CostPerKm, "à vide, un train doit coûter moins que calibré");

            var trip = Trip(scenario, "pinecrest", "cedarton", load: 0);
            double chargeableKm = trip.Line.DistanceBetween(trip.From, trip.To) * trip.Line.LegCostFactor(trip.From, trip.To);
            Check.True(trip.Line.LegCostFactor(trip.From, trip.To) > 1.05, "le trajet doit franchir un vrai relief");
            Check.Near(fixedPerKm * chargeableKm, trip.Billed, 1e-9 * trip.Billed,
                "facture d'un train vide : la part fixe sur les kilomètres pondérés par le relief");
        });

        runner.Add("coût marginal — le relief pèse davantage sur un train chargé", () =>
        {
            double Surcharge(double load)
            {
                var flat = ScenarioLoader.Load(MarginalPath());
                flat.Network.Traction.ClimbEquivalentKm = 0;
                return Trip(ScenarioLoader.Load(MarginalPath()), "pinecrest", "cedarton", load).Billed
                     - Trip(flat, "pinecrest", "cedarton", load).Billed;
            }

            double empty = Surcharge(0);
            double full = Surcharge(24);
            Check.True(empty > 0, "le relief doit coûter même à vide : la tare se gravit aussi");
            Check.Less(empty, full, "le surcoût du relief doit croître avec la charge");

            // Et dans la proportion exacte des masses : 480 t contre 264 t.
            var scenario = ScenarioLoader.Load(MarginalPath());
            var mass = scenario.Haulage.MassCost;
            double capacity = scenario.Trains[0].Capacity;
            double tare = mass.LocomotiveTonnes + capacity * mass.WagonTareTonnes;
            double expectedRatio = (tare + 24 * mass.TonnesPerLoad) / tare;
            Check.Near(expectedRatio, full / empty, 1e-9, "rapport des surcoûts de relief, plein contre vide");
        });

        runner.Add("coût marginal — neutre au réglage par défaut : sous flat, les masses ne sont lues nulle part", () =>
        {
            // Les empreintes de ReferenceTraceTests prouvent déjà que le défaut n'a pas
            // bougé. Ceci prouve davantage : des masses absurdes, déclarées sous le
            // modèle flat, ne changent pas un bit de la partie.
            foreach (var (file, expected) in new[] { ("sierra.json", "44876808B521C9E4"), ("heartland.json", "B966B86D3F0AF83C") })
            {
                var scenario = ScenarioLoader.Load(Path.Combine(Fixtures.RepoRoot(), "data", file));
                scenario.Haulage.CostModel = TrainCost.FlatModel;
                scenario.Haulage.MassCost = new MassCostDef
                {
                    LocomotiveTonnes = 1_000, WagonTareTonnes = 0.5, TonnesPerLoad = 50, CalibrationLoadFactor = 0.05,
                };
                Check.Equal(expected, TraceFingerprint(scenario),
                    $"{file} sous le modèle flat, masses déclarées mais ignorées");
            }
        });

        runner.Add("coût marginal — un modèle inconnu ou des masses absurdes sont refusés au chargement", () =>
        {
            // Une faute de frappe sur costModel ferait sinon tourner le modèle flat
            // sans le dire : un bloc silencieusement inerte.
            var typo = ScenarioLoader.Load(MarginalPath());
            typo.Haulage.CostModel = "masse";
            Check.Throws<InvalidDataException>(() => new Simulation(typo), "costModel « masse »");

            var weightless = ScenarioLoader.Load(MarginalPath());
            weightless.Haulage.MassCost.TonnesPerLoad = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(weightless), "un chargement sans masse");

            var calibration = ScenarioLoader.Load(MarginalPath());
            calibration.Haulage.MassCost.CalibrationLoadFactor = 1.5;
            Check.Throws<InvalidDataException>(() => new Simulation(calibration), "calibration au-delà de la capacité");
        });

        foreach (var solver in new[] { "reference", "anticipating" })
        {
            runner.Add($"coût marginal — sierra-marginal, {Ticks} ticks sans violation d'invariant, solveur {solver}", () =>
            {
                var sim = new Simulation(ScenarioLoader.Load(MarginalPath()), Solver(solver));
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

            runner.Add($"coût marginal — le relief entre dans les décisions et fait une géographie, solveur {solver}", () =>
            {
                // Le contraire du constat de SierraTests : sous le modèle mass, la trace
                // des marchés dépend du coût du relief. Et ce qu'elle en fait est une
                // géographie : la scierie de Cedarton, de l'autre côté du col, tourne
                // tant que le col coûte peu, et s'arrête quand il coûte cher. Mesuré sur
                // 40 trajectoires voisines (docs/FINDINGS.md, « Le coût marginal réel »)
                // : de 13 à 41 % d'utilisation jusqu'à 0,09, 0 % dans les 40 à 0,2.
                double Cedarton(double? climb, out string markets)
                {
                    var scenario = ScenarioLoader.Load(MarginalPath());
                    if (climb is double c) scenario.Network.Traction.ClimbEquivalentKm = c;
                    var sim = new Simulation(scenario, Solver(solver));
                    var stats = new RunStatistics { WarmupTicks = 90 };
                    var recorder = new CsvRecorder();
                    recorder.Record(sim.World);
                    for (int i = 0; i < Ticks; i++)
                    {
                        sim.Step();
                        recorder.Record(sim.World);
                        stats.Sample(sim.World);
                    }
                    markets = recorder.Fingerprint();
                    return stats.MeanUtilization("cedarton", "sawmill");
                }

                double asWritten = Cedarton(null, out string reliefMarkets);
                Cedarton(0, out string flatMarkets);
                double steep = Cedarton(0.2, out _);

                Check.True(reliefMarkets != flatMarkets,
                    "la trace des marchés doit dépendre du coût du relief sous le modèle mass");
                Check.True(asWritten > 0.10,
                    $"Cedarton doit tourner quand le col coûte peu, utilisation {asWritten * 100:0} %");
                Check.Less(steep, 0.01,
                    $"Cedarton doit s'arrêter quand le col coûte cher (0,2), utilisation {steep * 100:0.0} %");
            });
        }
    }

    private sealed record TripResult(double Billed, double Decided, RailLine Line, int From, int To);

    /// <summary>
    /// Un trajet sans échange possible, d'une gare à une autre, en un tick exactement.
    /// <para>
    /// L'économie est vidée — ni demande, ni production, ni stock, ni usine — sauf une
    /// demande de charbon à la destination, qui y met le prix au plafond et en fait le
    /// meilleur acheteur en aval : c'est donc vers elle que le transporteur chiffre
    /// son coût décidé. Le chargement est posé à la main avec un prix de revient
    /// prohibitif, pour que rien ne se vende en route. Un seul train, dont la vitesse
    /// est la distance à parcourir : il part au premier tick et s'arrête à la
    /// destination.
    /// </para>
    /// </summary>
    private static TripResult Trip(ScenarioDef scenario, string fromCity, string toCity, double load)
    {
        foreach (var city in scenario.Cities)
        {
            city.Demand.Clear();
            city.Production.Clear();
            city.InitialStock.Clear();
            city.Industries.Clear();
        }
        scenario.InitialCoverage = 0;
        scenario.Cities.Single(c => c.Id == toCity).Demand["coal"] = 0.1;

        var probe = new Simulation(scenario).World.Lines.Single();
        int from = probe.Stops.FindIndex(s => s.CityId == fromCity);
        int to = probe.Stops.FindIndex(s => s.CityId == toCity);

        var trainDef = scenario.Trains[0];
        trainDef.StartStop = from;
        trainDef.SpeedKmPerTick = probe.DistanceBetween(from, to);
        scenario.Trains = [trainDef];

        var sim = new Simulation(scenario);
        var world = sim.World;
        var train = world.Trains.Single();
        train.Direction = to > from ? 1 : -1;
        if (load > 0)
        {
            train.Cargo["coal"] = load;
            train.UnitCost["coal"] = 1e9;
        }

        double decided = ((OpportunisticHaulageSolver)sim.Haulage).HaulCostPerUnitAhead(world, train, "coal");
        double before = world.Company.TotalOperatingCost - world.Company.TotalTrackUpkeep;
        sim.Step();
        double billed = world.Company.TotalOperatingCost - world.Company.TotalTrackUpkeep - before;

        Check.True(train.StopIndex == to && train.DistanceToNextStop <= 1e-9,
            $"le train doit être arrivé à {toCity} (arrêt {train.StopIndex}, reste {train.DistanceToNextStop} km)");
        Check.Near(load, train.LoadedUnits, 1e-12, "aucun échange ne doit avoir eu lieu en route");
        return new TripResult(billed, decided, train.Line, from, to);
    }

    private static string TraceFingerprint(ScenarioDef scenario)
    {
        var sim = new Simulation(scenario, new ReferenceEconomySolver());
        var recorder = new CsvRecorder();
        recorder.Record(sim.World);
        for (int i = 0; i < Ticks; i++)
        {
            sim.Step();
            recorder.Record(sim.World);
        }
        return recorder.TraceFingerprint(sim.World.Events.Summary());
    }

    private static IEconomySolver Solver(string name) => name switch
    {
        "reference" => new ReferenceEconomySolver(),
        "anticipating" => new AnticipatingEconomySolver(),
        _ => throw new ArgumentException($"solveur inconnu : {name}"),
    };
}
