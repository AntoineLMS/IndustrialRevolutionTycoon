using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Telemetry;
using RailTycoon.Sim.Transport;

namespace RailTycoon.Tests;

/// <summary>
/// Le modèle de coût d'exploitation « mass » (<c>haulage.costModel</c>,
/// <c>Transport/TrainCost.cs</c>), défaut de tous les scénarios depuis le
/// 27 octobre 2026, éprouvé sur <c>data/sierra.json</c>, où le relief pèse.
/// <para>
/// Ce que ces tests protègent tient en une phrase : sous ce modèle, ce que le
/// transporteur croit qu'un chargement lui coûtera est exactement ce qu'il lui
/// coûte, wagon compris. Les tests de trajet le vérifient sur la simulation
/// elle-même — un train, un tick, aucune transaction possible —, pas sur les
/// fonctions de <see cref="TrainCost"/> prises isolément : c'est l'écart entre deux
/// chemins de code (<c>MoveTrain</c> et <c>HaulCostPerUnitAhead</c>) qui a laissé
/// le relief hors des décisions, et c'est cet écart qu'il faut attraper.
/// </para>
/// <para>
/// Et l'ancien modèle, <c>flat</c>, reste ce qu'il était : le test de neutralité
/// ci-dessous rejoue sous lui les empreintes d'avant le changement de défaut, au bit
/// près, avec des masses absurdes qu'il ne doit lire nulle part.
/// </para>
/// </summary>
internal static class MarginalCostTests
{
    private const int Ticks = 720;

    private static string SierraPath() => Path.Combine(Fixtures.RepoRoot(), "data", "sierra.json");

    public static void Register(TestRunner runner)
    {
        runner.Add("coût marginal — mass est le défaut, et chaque marchandise livrée déclare son wagon", () =>
        {
            Check.Equal(TrainCost.MassModel, new HaulageDef().CostModel, "modèle de coût par défaut");

            // Le wagon par défaut existe pour les scénarios écrits en code ; un
            // scénario livré dit le sien, parce que c'est lui qui fait le rayon de la
            // marchandise. Même discipline que les blocs de module : rien d'implicite.
            var dataDir = Path.Combine(Fixtures.RepoRoot(), "data");
            var silent = new List<string>();
            foreach (var path in Directory.GetFiles(dataDir, "*.json")
                         .Where(path => Path.GetFileName(path) != "locomotives.json")
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path),
                    new System.Text.Json.JsonDocumentOptions
                    {
                        CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                    });
                foreach (var cargo in doc.RootElement.GetProperty("cargos").EnumerateArray())
                    if (!cargo.TryGetProperty("wagon", out _))
                        silent.Add($"{Path.GetFileName(path)} : {cargo.GetProperty("id").GetString()}");
            }
            Check.True(silent.Count == 0, "marchandises sans wagon déclaré — " + string.Join(", ", silent));
        });

        runner.Add("coût marginal — le coût facturé égale le coût décidé, dans les deux sens du col", () =>
        {
            // De Pinecrest à Cedarton on gravit le col par l'ouest ; en sens inverse,
            // par l'est, où la rampe est plus haute. Dans les deux cas, dix chargements
            // de plus doivent coûter à la facture dix fois ce que le transporteur leur
            // a imputé avant de partir — ni plus, ni moins —, quel que soit le wagon.
            foreach (string cargo in new[] { "coal", "boards", "grain" })
            foreach (var (from, to) in new[] { ("pinecrest", "cedarton"), ("cedarton", "pinecrest"), ("westbrook", "farport") })
            {
                var empty = Trip(ScenarioLoader.Load(SierraPath()), from, to, cargo, load: 0);
                var loaded = Trip(ScenarioLoader.Load(SierraPath()), from, to, cargo, load: 10);

                double extra = loaded.Billed - empty.Billed;
                Check.True(loaded.Decided > 0, $"{cargo}, {from} → {to} : le coût décidé doit être positif");
                Check.Near(10 * loaded.Decided, extra, 1e-9 * loaded.Billed,
                    $"{cargo}, {from} → {to} : surcoût facturé de dix chargements contre dix fois le coût décidé");
            }

            // Le relief est bien dans la décision : le même trajet ne coûte pas pareil
            // dans les deux sens, et coûte davantage que sur le plat.
            var east = Trip(ScenarioLoader.Load(SierraPath()), "pinecrest", "cedarton", "coal", load: 10);
            var west = Trip(ScenarioLoader.Load(SierraPath()), "cedarton", "pinecrest", "coal", load: 10);
            var flat = ScenarioLoader.Load(SierraPath());
            flat.Network.Traction.ClimbEquivalentKm = 0;
            var eastFlat = Trip(flat, "pinecrest", "cedarton", "coal", load: 10);
            Check.Less(eastFlat.Decided, east.Decided, "le col doit renchérir le coût décidé vers l'est");
            Check.Less(east.Decided, west.Decided, "la rampe est, plus haute, doit coûter davantage vers l'ouest");
        });

        runner.Add("coût marginal — à trajet égal, un chargement coûte sa marchandise plus la tare de son wagon", () =>
        {
            // Le grain voyage en wagon couvert, le charbon en tombereau, les planches
            // sur wagon plat : même marchandise par chargement, trois tares. Le coût
            // décidé doit suivre la masse totale, exactement, et rien d'autre.
            var scenario = ScenarioLoader.Load(SierraPath());
            var mass = scenario.Haulage.MassCost;
            double Tonnes(string cargo)
                => mass.TonnesPerLoad + mass.WagonTypes[scenario.Cargos.Single(c => c.Id == cargo).Wagon].TareTonnes;

            var grain = Trip(ScenarioLoader.Load(SierraPath()), "pinecrest", "cedarton", "grain", load: 0);
            foreach (string cargo in new[] { "coal", "boards" })
            {
                var trip = Trip(ScenarioLoader.Load(SierraPath()), "pinecrest", "cedarton", cargo, load: 0);
                Check.Less(trip.Decided, grain.Decided, $"{cargo} : un wagon plus léger que le couvert doit coûter moins");
                Check.Near(Tonnes(cargo) / Tonnes("grain"), trip.Decided / grain.Decided, 1e-12,
                    $"{cargo} : rapport des coûts décidés contre rapport des masses");
            }
        });

        runner.Add("coût marginal — sous le modèle flat, un chargement ne coûte rien de plus à porter", () =>
        {
            // Le constat de « Relief et économie ensemble », vu du trajet : sous
            // l'ancien modèle, la facture ne dépend pas de la charge, et le coût décidé
            // n'est pas celui qu'on paie. C'est ce que le modèle mass corrige.
            ScenarioDef Flat()
            {
                var scenario = ScenarioLoader.Load(SierraPath());
                scenario.Haulage.CostModel = TrainCost.FlatModel;
                return scenario;
            }
            var empty = Trip(Flat(), "pinecrest", "cedarton", "coal", load: 0);
            var loaded = Trip(Flat(), "pinecrest", "cedarton", "coal", load: 10);
            Check.Near(empty.Billed, loaded.Billed, 1e-9 * empty.Billed, "facture à vide contre facture chargée");
            Check.True(loaded.Decided > 0, "le transporteur impute pourtant un coût à chaque chargement");
        });

        runner.Add("coût marginal — un train vide paie sa seule locomotive, relief compris", () =>
        {
            var scenario = ScenarioLoader.Load(SierraPath());
            var mass = scenario.Haulage.MassCost;
            var trainDef = scenario.Trains[0];

            // Recalculée ici depuis les données, sans passer par TrainCost : une erreur
            // dans la formule des tarifs doit se voir, pas se retrouver des deux côtés.
            // Le wagon suit le chargement : à vide, il n'y a que la locomotive.
            double calibrationLoad = mass.TonnesPerLoad + mass.WagonTypes[mass.CalibrationWagon].TareTonnes;
            double calibrationMass = mass.LocomotiveTonnes + mass.CalibrationLoadFactor * trainDef.Capacity * calibrationLoad;
            double fixedPerKm = trainDef.CostPerKm * mass.LocomotiveTonnes / calibrationMass;
            Check.Less(fixedPerKm, trainDef.CostPerKm, "à vide, un train doit coûter moins que calibré");

            var trip = Trip(scenario, "pinecrest", "cedarton", "coal", load: 0);
            double chargeableKm = trip.Line.DistanceBetween(trip.From, trip.To) * trip.Line.LegCostFactor(trip.From, trip.To);
            Check.True(trip.Line.LegCostFactor(trip.From, trip.To) > 1.05, "le trajet doit franchir un vrai relief");
            Check.Near(fixedPerKm * chargeableKm, trip.Billed, 1e-9 * trip.Billed,
                "facture d'un train vide : la locomotive sur les kilomètres pondérés par le relief");
        });

        runner.Add("coût marginal — le relief pèse davantage sur un train chargé", () =>
        {
            double Surcharge(double load)
            {
                var flat = ScenarioLoader.Load(SierraPath());
                flat.Network.Traction.ClimbEquivalentKm = 0;
                return Trip(ScenarioLoader.Load(SierraPath()), "pinecrest", "cedarton", "coal", load).Billed
                     - Trip(flat, "pinecrest", "cedarton", "coal", load).Billed;
            }

            double empty = Surcharge(0);
            double full = Surcharge(24);
            Check.True(empty > 0, "le relief doit coûter même à vide : la locomotive se gravit aussi");
            Check.Less(empty, full, "le surcoût du relief doit croître avec la charge");

            // Et dans la proportion exacte des masses : la locomotive seule, contre la
            // locomotive et vingt-quatre tombereaux pleins.
            var scenario = ScenarioLoader.Load(SierraPath());
            var mass = scenario.Haulage.MassCost;
            double coalLoad = mass.TonnesPerLoad + mass.WagonTypes["gondola"].TareTonnes;
            double expectedRatio = (mass.LocomotiveTonnes + 24 * coalLoad) / mass.LocomotiveTonnes;
            Check.Near(expectedRatio, full / empty, 1e-9, "rapport des surcoûts de relief, plein contre vide");
        });

        runner.Add("coût marginal — sous flat, les masses ne sont lues nulle part", () =>
        {
            // Sous le modèle flat, des masses absurdes — jusqu'à des wagons que les
            // marchandises ne déclarent pas — ne changent pas un bit de la partie. Les
            // empreintes d'avant le changement de défaut ne se rejouent plus : les
            // installations ont chacune leur marché depuis le 27 octobre 2026, ce
            // qu'aucun réglage ne défait ; la comparaison se fait donc entre deux
            // parties sous flat, masses par défaut et masses absurdes.
            foreach (string file in new[] { "sierra.json", "heartland.json" })
            {
                ScenarioDef Flat()
                {
                    var scenario = ScenarioLoader.Load(Path.Combine(Fixtures.RepoRoot(), "data", file));
                    scenario.Haulage.CostModel = TrainCost.FlatModel;
                    return scenario;
                }
                var absurd = Flat();
                absurd.Haulage.MassCost = new MassCostDef
                {
                    LocomotiveTonnes = 1_000, TonnesPerLoad = 50, CalibrationLoadFactor = 0.05,
                    WagonTypes = { ["box"] = new WagonTypeDef { TareTonnes = 0.5 } },
                };
                absurd.Haulage.MassCost.WagonTypes.Remove("gondola");
                Check.Equal(TraceFingerprint(Flat()), TraceFingerprint(absurd),
                    $"{file} sous le modèle flat, masses absurdes contre masses par défaut");
            }
        });

        runner.Add("coût marginal — un modèle inconnu, des masses absurdes ou un wagon inconnu sont refusés", () =>
        {
            // Une faute de frappe sur costModel ferait sinon tourner un autre modèle
            // sans le dire, et un wagon inconnu échouerait au premier chargement.
            var typo = ScenarioLoader.Load(SierraPath());
            typo.Haulage.CostModel = "masse";
            Check.Throws<InvalidDataException>(() => new Simulation(typo), "costModel « masse »");

            var weightless = ScenarioLoader.Load(SierraPath());
            weightless.Haulage.MassCost.TonnesPerLoad = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(weightless), "un chargement sans masse");

            var noLocomotive = ScenarioLoader.Load(SierraPath());
            noLocomotive.Haulage.MassCost.LocomotiveTonnes = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(noLocomotive), "un train sans locomotive");

            var calibration = ScenarioLoader.Load(SierraPath());
            calibration.Haulage.MassCost.CalibrationLoadFactor = 1.5;
            Check.Throws<InvalidDataException>(() => new Simulation(calibration), "calibration au-delà de la capacité");

            var wagon = ScenarioLoader.Load(SierraPath());
            wagon.Cargos.Single(c => c.Id == "coal").Wagon = "hopper";
            Check.Throws<InvalidDataException>(() => new Simulation(wagon), "le charbon dans un wagon inconnu");

            var negative = ScenarioLoader.Load(SierraPath());
            negative.Haulage.MassCost.WagonTypes["flat"].TareTonnes = -1;
            Check.Throws<InvalidDataException>(() => new Simulation(negative), "une tare négative");

            var calibrationWagon = ScenarioLoader.Load(SierraPath());
            calibrationWagon.Haulage.MassCost.CalibrationWagon = "reefer";
            Check.Throws<InvalidDataException>(() => new Simulation(calibrationWagon), "un wagon de calibration inconnu");
        });

        foreach (var solver in new[] { "reference", "anticipating" })
        {
            runner.Add($"coût marginal — le relief entre dans les décisions, solveur {solver}", () =>
            {
                // Le contraire du constat de l'ancien modèle : la trace des marchés
                // dépend du coût du relief. Ce qu'elle n'en fait pas, mesuré sur 40
                // trajectoires voisines (docs/FINDINGS.md, « Le coût marginal réel par
                // défaut ») : des bassins de part et d'autre du col. Le wagon suit le
                // chargement, si bien que presque tout le coût d'un train est marginal,
                // et le rayon des grumes est plus court que les 355 km qui séparent
                // Pinecrest de Cedarton : la scierie de l'autre côté du col ne tourne à
                // aucune traction, relief nul compris. Si elle se remet à tourner, le
                // rayon a changé, et les chiffres de FINDINGS avec lui.
                double Cedarton(double? climb, out string markets)
                {
                    var scenario = ScenarioLoader.Load(SierraPath());
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
                double level = Cedarton(0, out string flatMarkets);
                double steep = Cedarton(0.2, out _);

                Check.True(reliefMarkets != flatMarkets,
                    "la trace des marchés doit dépendre du coût du relief sous le modèle mass");
                foreach (var (label, utilization) in new[] { ("relief nul", level), ("0,03", asWritten), ("0,2", steep) })
                    Check.Less(utilization, 0.01,
                        $"Cedarton, traction {label} : utilisation {utilization * 100:0.0} %, " +
                        "le rayon des grumes ne devait pas atteindre l'autre côté du col");
            });
        }
    }

    private sealed record TripResult(double Billed, double Decided, RailLine Line, int From, int To);

    /// <summary>
    /// Un trajet sans échange possible, d'une gare à une autre, en un tick exactement.
    /// <para>
    /// L'économie est vidée — ni demande, ni production, ni stock, ni usine — sauf une
    /// demande de <paramref name="cargoId"/> à la destination, qui y met le prix au
    /// plafond et en fait le meilleur acheteur en aval : c'est donc vers elle que le
    /// transporteur chiffre son coût décidé. Le chargement est posé à la main avec un prix de revient
    /// prohibitif, pour que rien ne se vende en route. Un seul train, dont la vitesse
    /// est la distance à parcourir : il part au premier tick et s'arrête à la
    /// destination.
    /// </para>
    /// </summary>
    private static TripResult Trip(ScenarioDef scenario, string fromCity, string toCity, string cargoId, double load)
    {
        foreach (var city in scenario.Cities)
        {
            city.Demand.Clear();
            city.Production.Clear();
            city.InitialStock.Clear();
            city.Industries.Clear();
        }
        scenario.InitialCoverage = 0;
        scenario.Cities.Single(c => c.Id == toCity).Demand[cargoId] = 0.1;

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
            train.Cargo[cargoId] = load;
            train.UnitCost[cargoId] = 1e9;
        }

        double decided = ((OpportunisticHaulageSolver)sim.Haulage).HaulCostPerUnitAhead(world, train, cargoId);
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
