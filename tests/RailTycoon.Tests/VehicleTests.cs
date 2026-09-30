using RailTycoon.Sim.Core;
using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Finance;
using RailTycoon.Sim.Telemetry;
using RailTycoon.Sim.Transport;

namespace RailTycoon.Tests;

/// <summary>
/// Le module <c>vehicles</c> (<c>Transport/Vehicles.cs</c>) et son scénario,
/// <c>data/sierra-vehicules.json</c> : des locomotives du catalogue achetées, un
/// carburant payé en gare au prix local, un entretien dû chaque tick, une vitesse
/// tirée de la puissance, de l'adhérence et de la masse du train.
/// <para>
/// Chaque test a été vérifié par mutation : le défaut qu'il garde a été réintroduit
/// à la main, le test a échoué, le code a été restauré (docs/FINDINGS.md, « Les
/// véhicules », méthode).
/// </para>
/// </summary>
internal static class VehicleTests
{
    private const int Ticks = 720;

    private static string VehiclesPath() => Path.Combine(Fixtures.RepoRoot(), "data", "sierra-vehicules.json");
    private static string MarginalPath() => Path.Combine(Fixtures.RepoRoot(), "data", "sierra-marginal.json");
    private static ScenarioDef Vehicles() => ScenarioLoader.Load(VehiclesPath());

    private static LocomotiveDef Loco(ScenarioDef s, string id) => s.Vehicles.Catalog!.Locomotives.Single(l => l.Id == id);

    public static void Register(TestRunner runner)
    {
        // ------------------------------------------------------------ neutralité

        runner.Add("véhicules — neutre au défaut : un bloc désactivé ne change pas un bit de sierra-marginal", () =>
        {
            // Les empreintes de ReferenceTraceTests prouvent que les scénarios sans
            // bloc n'ont pas bougé. Ceci prouve davantage : le bloc complet de
            // sierra-vehicules, catalogue chargé et locomotives référencées, mais
            // désactivé, rend la partie de sierra-marginal au bit près.
            var scenario = Vehicles();
            scenario.Vehicles.Enabled = false;
            Check.Equal("3A328BE0FBB30F59", TraceFingerprint(scenario, new ReferenceEconomySolver()),
                "sierra-vehicules au bloc désactivé contre l'empreinte de sierra-marginal");
        });

        runner.Add("véhicules — sierra-vehicules décrit l'économie de sierra-marginal au caractère près", () =>
        {
            // Même garde que sierra-marginal contre sierra : seuls l'identité, le bloc
            // vehicles et la locomotive de chaque train ont le droit de différer.
            string Economy(ScenarioDef scenario)
            {
                scenario.Id = "";
                scenario.Name = "";
                scenario.Vehicles = new VehiclesDef();
                foreach (var train in scenario.Trains) train.Locomotive = null;
                return System.Text.Json.JsonSerializer.Serialize(scenario);
            }

            var vehicles = Vehicles();
            Check.True(vehicles.Vehicles.Enabled, "sierra-vehicules.json active le module");
            Check.True(Economy(ScenarioLoader.Load(MarginalPath())) == Economy(vehicles),
                "sierra-marginal.json et sierra-vehicules.json doivent décrire la même économie");
        });

        // --------------------------------------------------------- le catalogue

        runner.Add("véhicules — la vitesse, la masse et les coûts viennent du catalogue", () =>
        {
            var scenario = Vehicles();
            var loco = Loco(scenario, "prr_d5");
            var world = new Simulation(scenario).World;
            var train = world.Trains[0];

            Check.True(train.Vehicle is not null, "le train doit porter sa locomotive");
            Check.Near(loco.TopSpeedKmh * scenario.Vehicles.RunningHoursPerTick, train.SpeedKmPerTick, 1e-12,
                "vitesse nominale = vitesse du catalogue × heures de marche");
            Check.Near(scenario.Vehicles.OtherCostPerKm, train.CostPerKm, 1e-12,
                "le coût kilométrique est otherCostPerKm, pas le costPerKm du train");
            Check.True(Math.Abs(train.CostPerKm - scenario.Trains[0].CostPerKm) > 1e-6,
                "le costPerKm déclaré ne doit pas être lu");

            // La masse de la machine entre dans la tare du modèle mass.
            var mass = scenario.Haulage.MassCost;
            double tare = loco.MassTonnes + train.Capacity * mass.WagonTareTonnes;
            double calib = tare + mass.CalibrationLoadFactor * train.Capacity * mass.TonnesPerLoad;
            var rates = TrainCost.Rates(scenario.Haulage, train);
            Check.Near(train.CostPerKm * tare / calib, rates.FixedPerKm, 1e-15, "part fixe calculée sur la masse du catalogue");

            // Le carburant : kg pour mille tonnes-km, converti en chargements.
            var (fuelFixed, fuelPerLoad) = VehicleRules.FuelRates(scenario.Haulage, train);
            Check.Near(loco.FuelKgPer1000TonneKm / 1e6 / mass.TonnesPerLoad * tare, fuelFixed, 1e-15, "carburant par km à vide");
            Check.Near(loco.FuelKgPer1000TonneKm / 1e6, fuelPerLoad, 1e-15, "carburant par chargement-km");

            // Une autre machine change tout cela.
            var other = Vehicles();
            foreach (var t in other.Trains) t.Locomotive = "lv_consolidation";
            var consolidation = new Simulation(other).World.Trains[0];
            Check.Near(Loco(other, "lv_consolidation").TopSpeedKmh * other.Vehicles.RunningHoursPerTick,
                consolidation.SpeedKmPerTick, 1e-12, "la Consolidation roule à sa propre vitesse");
            Check.Less(rates.FixedPerKm, TrainCost.Rates(other.Haulage, consolidation).FixedPerKm,
                "une machine plus lourde alourdit la part fixe");

            // La vitesse limite des wagons, quand le scénario en déclare une, borne
            // celle du catalogue — et seulement quand elle est plus basse.
            var capped = Vehicles();
            capped.Vehicles.MaxTrainSpeedKmh = 50;
            foreach (var t in capped.Trains) t.Locomotive = "prr_d5";
            capped.Trains[1].Locomotive = "lv_consolidation";
            var cappedTrains = new Simulation(capped).World.Trains;
            Check.Near(50 * capped.Vehicles.RunningHoursPerTick, cappedTrains[0].SpeedKmPerTick, 1e-12,
                "la D5 (72 km/h) bornée à 50 km/h");
            Check.Near(40 * capped.Vehicles.RunningHoursPerTick, cappedTrains[1].SpeedKmPerTick, 1e-12,
                "la Consolidation (40 km/h) sous la limite garde sa vitesse");
        });

        // -------------------------------------------------------------- l'achat

        runner.Add("véhicules — l'achat débite la compagnie au prix du catalogue, hors résultat", () =>
        {
            var scenario = Vehicles();
            double price = Loco(scenario, "prr_d5").PurchaseCost;
            var sim = new Simulation(scenario);
            var co = sim.World.Company;

            Check.Near(3 * price, co.TotalVehiclePurchases, 1e-9, "trois locomotives payées à l'ouverture");
            Check.Near(scenario.StartingCash + co.NetProfit - 3 * price, co.Cash, 1e-6,
                "la caisse = mise + résultat − achats : l'achat n'est pas une charge");
            Check.True(sim.World.Trains.All(t => t.InService), "achetés à l'ouverture, les trains sont en service");
        });

        runner.Add("véhicules — un achat daté : le train n'existe pas avant, il est payé ce jour-là", () =>
        {
            var scenario = Vehicles();
            scenario.Trains[1].PurchaseTick = 30;
            double price = Loco(scenario, scenario.Trains[1].Locomotive!).PurchaseCost;
            var sim = new Simulation(scenario);
            var late = sim.World.Trains[1];

            Check.Near(2 * price, sim.World.Company.TotalVehiclePurchases, 1e-9, "deux locomotives seulement à l'ouverture");
            for (int i = 0; i < 29; i++) sim.Step();
            Check.True(!late.InService && late.TotalKmTravelled == 0 && late.Vehicle!.MaintenancePaid == 0 && late.LoadedUnits == 0,
                "avant son achat, le train ne roule, ne charge et ne coûte rien");
            sim.Step();
            Check.True(late.InService, "acheté au tick 30");
            Check.Near(3 * price, sim.World.Company.TotalVehiclePurchases, 1e-9, "la troisième payée au tick 30");
            sim.Step();
            Check.True(late.TotalKmTravelled > 0, "il roule dès le lendemain");
        });

        // ----------------------------------------------------------- le carburant

        runner.Add("véhicules — le carburant se paie au prix local, même sans stock, et aucun stock ne bouge", () =>
        {
            // L'économie est vidée ; seule Coalpass garde du charbon, sans en
            // consommer : son marché vaut le plancher. Partout ailleurs, ni stock ni
            // acheteur : c'est la ville sans charbon, qui se paie au plafond.
            var scenario = Emptied(Vehicles());
            scenario.Cities.Single(c => c.Id == "coalpass").InitialStock["coal"] = 500;
            var sim = new Simulation(scenario);
            var world = sim.World;
            var stocks = world.Cities.SelectMany(c => world.MarketsOf(c)).Select(m => m.Stock).ToArray();

            for (int i = 0; i < 10; i++) sim.Step();

            var coal = world.Cargo("coal");
            double ceiling = coal.BasePrice * scenario.PriceModel.MaxMultiplier;
            double floor = coal.BasePrice * scenario.PriceModel.MinMultiplier;
            int paidSomewhere = 0;
            foreach (var train in world.Trains)
            {
                var v = train.Vehicle!;
                foreach (var city in world.Cities)
                {
                    double loads = v.FuelLoadsByCity.GetValueOrDefault(city.Id);
                    if (loads <= 0) continue;
                    paidSomewhere++;
                    double expected = city.Id == "coalpass" ? floor : ceiling;
                    Check.Near(expected, v.FuelPaidByCity[city.Id] / loads, 1e-9,
                        $"prix payé à {city.Id} (stock {city.Market("coal").Stock})");
                }
            }
            Check.True(paidSomewhere > 5, "les trains doivent avoir fait le plein dans plusieurs villes");
            Check.True(world.Company.TotalFuel > 0, "le carburant doit avoir été payé");

            var after = world.Cities.SelectMany(c => world.MarketsOf(c)).Select(m => m.Stock).ToArray();
            for (int i = 0; i < stocks.Length; i++)
                Check.Near(stocks[i], after[i], 0, "le plein ne prélève aucun stock");
        });

        runner.Add("véhicules — le carburant facturé égale le carburant décidé, dans les deux sens du col", () =>
        {
            // Même promesse que le modèle mass, pour la part du carburant : dix
            // chargements de plus coûtent en carburant, payé gare après gare, dix fois
            // ce que le transporteur leur a imputé avant de partir.
            foreach (var (from, to) in new[] { ("pinecrest", "cedarton"), ("cedarton", "pinecrest") })
            {
                var (emptyPaid, _) = FuelTrip(from, to, load: 0);
                var (loadedPaid, decided) = FuelTrip(from, to, load: 10);
                Check.True(decided > 0, $"{from} → {to} : le carburant décidé doit être positif");
                Check.Near(10 * decided, loadedPaid - emptyPaid, 1e-9 * loadedPaid,
                    $"{from} → {to} : carburant de dix chargements contre dix fois le coût décidé");
            }
        });

        runner.Add("véhicules — un scénario qui ne déclare pas la marchandise carburant est refusé", () =>
        {
            var noFuel = Vehicles();
            noFuel.Vehicles.Fuels.Remove("coal");
            Check.Throws<InvalidDataException>(() => new Simulation(noFuel), "carburant coal non déclaré");

            var unknown = Vehicles();
            unknown.Vehicles.Fuels["coal"] = "anthracite";
            Check.Throws<InvalidDataException>(() => new Simulation(unknown), "carburant désignant une marchandise inconnue");

            // La sierra n'a pas de pétrole : une diesel y est refusée, faute de fioul.
            var diesel = Vehicles();
            diesel.Trains[0].Locomotive = "emd_f3";
            Check.Throws<InvalidDataException>(() => new Simulation(diesel), "diesel sans marchandise fioul");
        });

        runner.Add("véhicules — ce que le module refuse au chargement", () =>
        {
            var flat = Vehicles();
            flat.Haulage.CostModel = TrainCost.FlatModel;
            Check.Throws<InvalidDataException>(() => new Simulation(flat), "module vehicles sous le modèle flat");

            var unknown = Vehicles();
            unknown.Trains[0].Locomotive = "orient_express";
            Check.Throws<InvalidDataException>(() => new Simulation(unknown), "locomotive inconnue");

            var none = Vehicles();
            none.Trains[0].Locomotive = null;
            Check.Throws<InvalidDataException>(() => new Simulation(none), "train sans locomotive sous le module");

            var noCatalog = Vehicles();
            noCatalog.Vehicles.Catalog = null;
            Check.Throws<InvalidDataException>(() => new Simulation(noCatalog), "catalogue non chargé");

            var inert = ScenarioLoader.Load(MarginalPath());
            inert.Trains[0].PurchaseTick = 10;
            Check.Throws<InvalidDataException>(() => new Simulation(inert), "achat daté sans module");

            // Une machine dont l'adhérence ne la soulève pas elle-même sur la rampe du
            // col : aucun nombre de passes ne la ferait monter.
            var weak = Vehicles();
            Loco(weak, "prr_d5").TractiveEffortKn = 0.5;
            Check.Throws<InvalidDataException>(() => new Simulation(weak), "une machine qui ne gravit pas le col, même seule");
        });

        // ----------------------------------------------------------- l'entretien

        runner.Add("véhicules — l'entretien est dû chaque tick, train arrêté compris", () =>
        {
            var scenario = Vehicles();
            var sim = new Simulation(scenario);
            var world = sim.World;
            double perTick = world.Trains.Sum(t => t.Vehicle!.Locomotive.MaintenancePerTick);
            Check.True(perTick > 0, "les locomotives du scénario ont un entretien");

            sim.Step();
            Check.Near(perTick, world.Company.TotalVehicleMaintenance, 1e-9, "un tick d'entretien en roulant");

            // Trains à l'arrêt, comme sous administration judiciaire : l'entretien
            // court toujours, les kilomètres non.
            world.Company.Grounded = true;
            double km = world.Trains.Sum(t => t.TotalKmTravelled);
            double fuel = world.Company.TotalFuel;
            for (int i = 0; i < 5; i++) sim.Step();
            Check.Near(6 * perTick, world.Company.TotalVehicleMaintenance, 1e-9, "cinq ticks d'entretien à l'arrêt");
            Check.Near(km, world.Trains.Sum(t => t.TotalKmTravelled), 0, "à l'arrêt, aucun kilomètre");
            Check.Near(fuel, world.Company.TotalFuel, 0, "à l'arrêt, aucun carburant");
        });

        // ------------------------------------------------- puissance et dynamique

        runner.Add("véhicules — une locomotive plus puissante tient mieux la rampe", () =>
        {
            // Sur la simulation : le même train chargé, de Pinecrest vers le col, avec
            // la D5 telle qu'au catalogue puis avec deux fois sa puissance — adhérence
            // inchangée, et une charge qu'elle démarre d'une traite : seule la
            // puissance diffère. La plus puissante est plus loin au bout de deux ticks.
            double Progress(double scale)
            {
                var scenario = Emptied(Vehicles());
                var loco = Loco(scenario, "prr_d5");
                loco.PowerKw *= scale;
                scenario.Vehicles.RunningHoursPerTick = 1.0;
                var sim = new Simulation(scenario);
                var train = sim.World.Trains.Single();
                Check.Equal("pinecrest", train.Line.Stops[train.StopIndex].CityId, "départ de Pinecrest");
                train.Direction = 1;
                train.Cargo["coal"] = 5;
                train.UnitCost["coal"] = 1e9;
                sim.Step();
                sim.Step();
                Check.True(train.Vehicle!.LegsDoubled == 0, "la charge doit monter d'une traite");
                return train.TotalKmTravelled;
            }
            double weak = Progress(1.0), strong = Progress(2.0);
            Check.Less(weak, strong, "à charge égale sur la rampe, la machine plus puissante doit aller plus loin");

            // Sur la dynamique seule : en rampe, la vitesse de croisière d'un train
            // lourd est bornée par la puissance, pas par la vitesse maximale.
            var p = new TrainPhysics(48.1, 48, 296, 72, 0.004);
            var cruiseWeak = TrainDynamics.Leg(p, 300, 0.01, 65, acceleration: false);
            var cruiseStrong = TrainDynamics.Leg(p with { PowerKw = 592 }, 300, 0.01, 65, acceleration: false);
            Check.True(cruiseWeak.Passes == 1 && cruiseStrong.Passes == 1, "300 t sur 1 % : d'une traite");
            Check.Less(cruiseWeak.CruiseKmh, 72, "la D5 ne tient pas sa vitesse maximale en rampe, chargée");
            Check.Less(cruiseWeak.CruiseKmh, cruiseStrong.CruiseKmh, "deux fois la puissance, une croisière plus rapide");
        });

        runner.Add("véhicules — l'adhérence borne la charge démarrée : au-delà, le train monte en plusieurs passes", () =>
        {
            var p = new TrainPhysics(48.1, 48, 296, 72, 0.004);
            var slow = TrainDynamics.Leg(p, 480, 0.015, 65, acceleration: false);
            var fast = TrainDynamics.Leg(p with { TractiveEffortKn = 96 }, 480, 0.015, 65, acceleration: false);
            Check.True(slow.Passes > 1, $"la D5 doit couper un train plein sur 1,5 % ({slow.Passes} passe)");
            Check.True(fast.Passes == 1, $"deux fois son adhérence doit monter d'une traite ({fast.Passes} passes)");
            Check.Less(fast.Hours, slow.Hours, "couper le train coûte du temps");
            Check.Less(TrainDynamics.MaxStartingTonnes(p, 0.015), 480, "480 t dépassent ce que la D5 démarre sur 1,5 %");
        });

        runner.Add("véhicules — accélération : un train plus lourd met plus longtemps à atteindre sa vitesse", () =>
        {
            // Même tronçon plat de 50 km : le temps perdu à accélérer — temps réel
            // moins temps à vitesse de croisière — croît avec la masse, et décroît avec
            // la puissance.
            var p = new TrainPhysics(48.1, 48, 296, 72, 0.004);
            double Lost(TrainPhysics physics, double gross)
                => TrainDynamics.SinglePass(physics, gross, 0, 50, acceleration: true).Hours
                 - TrainDynamics.SinglePass(physics, gross, 0, 50, acceleration: false).Hours;

            double light = Lost(p, 264), heavy = Lost(p, 480);
            Check.True(light > 0, "la mise en vitesse doit coûter du temps");
            Check.Less(light, heavy, "un train chargé perd plus de temps à accélérer qu'un train vide");
            Check.Less(Lost(p with { PowerKw = 600, TractiveEffortKn = 96 }, 480), heavy,
                "une locomotive plus puissante accélère plus vite");

            // Le cas exact : une machine bridée à 18 km/h accélère toute la phase à
            // effort constant (5 m/s sous P / TE = 6,2 m/s), et le temps perdu vaut
            // v × M / (2 (TE − R)) — l'inertie de toute la masse, cargaison comprise.
            var slowTop = p with { TopSpeedKmh = 18 };
            foreach (double gross in new[] { 264.0, 480.0 })
            {
                double m = gross * 1000, r = m * 9.81 * 0.004, v = 5.0;
                double expectedLost = v * m / (2 * (48_000 - r)) / 3600.0;
                Check.Near(expectedLost, Lost(slowTop, gross), 1e-9 * expectedLost,
                    $"temps perdu à accélérer {gross} t à effort constant");
            }

            // Et dans la phase à puissance constante : sans résistance, le temps perdu
            // est proportionnel à la masse, cargaison comprise.
            var frictionless = p with { RollingResistance = 1e-9, TopSpeedKmh = 72 };
            Check.Near(480.0 / 264.0, Lost(frictionless, 480) / Lost(frictionless, 264), 1e-3,
                "sans résistance, le temps d'accélération croît comme la masse");

            // Un tronçon trop court pour atteindre la croisière : jamais plus rapide
            // que la vitesse maximale, et plus lent qu'à croisière constante.
            var shortLeg = TrainDynamics.SinglePass(p, 480, 0, 0.5, acceleration: true);
            Check.True(shortLeg.Hours > 0.5 / shortLeg.CruiseKmh, "un tronçon court se fait sous la vitesse de croisière");
        });

        runner.Add("véhicules — à charge nulle et sans relief, la vitesse est celle du catalogue", () =>
        {
            // La D5 seule, sans wagons : la puissance dépasse largement la résistance,
            // donc la croisière est la vitesse maximale ; sans accélération le tronçon
            // se fait exactement à cette vitesse, et avec, l'écart s'efface sur un long
            // tronçon.
            var p = new TrainPhysics(48.1, 48, 296, 72, 0.004);
            var cruise = TrainDynamics.Leg(p, 48.1, 0, 100, acceleration: false);
            Check.Near(72, cruise.CruiseKmh, 1e-9, "vitesse de croisière");
            Check.Near(100.0 / 72, cruise.Hours, 1e-12, "sans accélération, le tronçon à la vitesse du catalogue");
            var accelerated = TrainDynamics.Leg(p, 48.1, 0, 1000, acceleration: true);
            Check.Near(1000.0 / 72, accelerated.Hours, 0.005 * 1000.0 / 72, "avec accélération, à 0,5 % près sur 1 000 km");

            // Et dans la simulation : un train vide sur un relief nul roule à sa
            // vitesse nominale, au temps de mise en vitesse près.
            var scenario = Emptied(Vehicles());
            var terrain = scenario.Network.Terrain
                ?? throw new InvalidOperationException("sierra-vehicules doit déclarer un relief");
            terrain.Features.Clear();
            terrain.Noise = null;
            scenario.Vehicles.Acceleration = false;
            foreach (var t in scenario.Trains) t.Capacity = 0.001;
            var sim = new Simulation(scenario);
            sim.Step();
            var train = sim.World.Trains.Single();
            double nominal = Loco(scenario, "prr_d5").TopSpeedKmh * scenario.Vehicles.RunningHoursPerTick;
            Check.Near(nominal, train.TotalKmTravelled, 1e-6 * nominal,
                "un tick à vide sur le plat : la vitesse du catalogue × les heures de marche");
        });

        // ---------------------------------------------------------------- finance

        runner.Add("véhicules — avec la finance, les locomotives sont à l'actif et s'amortissent, au centime", () =>
        {
            var scenario = Vehicles();
            scenario.Finance = ScenarioLoader.Load(Path.Combine(Fixtures.RepoRoot(), "data", "heartland-finance.json")).Finance;
            // Pas d'OPA : une fusion apporterait le matériel de l'absorbée, et le
            // compte ci-dessous ne porterait plus sur les seules locomotives.
            scenario.Finance.Acquisition.Targets.Clear();
            scenario.Trains[2].PurchaseTick = 100;
            var sim = new Simulation(scenario);
            var world = sim.World;
            var player = world.Finance.Player!;
            double initial = Invariants.InitialStockTotal(world);

            decimal fixedStart = Money.Round(scenario.Finance.FixedAssetsAtStart);
            decimal price = Money.FromDouble(Loco(scenario, "prr_d5").PurchaseCost);
            Check.Equal((fixedStart + 2 * price).ToString(), player.Book[Accounts.FixedAssets].ToString(),
                "à l'ouverture, deux locomotives à l'actif au prix du catalogue");

            decimal depreciated = 0m;
            for (int i = 0; i < Ticks; i++)
            {
                decimal before = player.Book[Accounts.FixedAssets];
                decimal boughtBefore = world.Finance.ReflectedVehiclePurchases;
                sim.Step();
                decimal bought = world.Finance.ReflectedVehiclePurchases - boughtBefore;
                depreciated += before + bought - player.Book[Accounts.FixedAssets];

                var violations = Invariants.Check(world, initial);
                if (violations.Count > 0)
                    Check.True(false, $"tick {world.Tick.Index} : {violations[0].Rule} — {violations[0].Detail}");
            }

            Check.Equal((3 * price).ToString(), world.Finance.ReflectedVehiclePurchases.ToString(), "trois locomotives achetées");
            Check.Equal((fixedStart + 3 * price - depreciated).ToString(), player.Book[Accounts.FixedAssets].ToString(),
                "matériel = départ + achats − amortissements, au centime");

            // L'amortissement suit le matériel acheté : au taux du scénario, sur la
            // valeur d'origine de tout le parc.
            decimal pct = scenario.Finance.DepreciationAnnualPercent;
            decimal expected = 99 * Money.Round((fixedStart + 2 * price) * pct / 100m / 360)
                             + (Ticks - 99) * Money.Round((fixedStart + 3 * price) * pct / 100m / 360);
            Check.Equal(expected.ToString(), depreciated.ToString(), "amortissement cumulé, au centime");
        });

        // ----------------------------------------------------------- invariants

        foreach (var solver in new[] { "reference", "anticipating" })
        {
            runner.Add($"véhicules — sierra-vehicules, {Ticks} ticks sans violation d'invariant, solveur {solver}", () =>
            {
                var sim = new Simulation(Vehicles(), Solver(solver));
                double initial = Invariants.InitialStockTotal(sim.World);
                for (int i = 0; i < Ticks; i++)
                {
                    sim.Step();
                    var violations = Invariants.Check(sim.World, initial);
                    if (violations.Count > 0)
                        Check.True(false, $"tick {sim.World.Tick.Index} : {violations[0].Rule} — {violations[0].Detail}");
                }
                Check.True(sim.World.Company.TotalFuel > 0 && sim.World.Company.TotalVehicleMaintenance > 0,
                    "carburant et entretien doivent avoir été payés");
            });
        }

        // ------------------------------------------- le catalogue borné à l'époque
        // Décision du 30 septembre 2026 : sans borne, les machines de 1910 écrasaient
        // celles de 1870 (l'E6 faisait 3,4 fois la D5 sur la sierra de 1875), et
        // choisir sa locomotive revenait à prendre la plus récente.

        runner.Add("véhicules — une locomotive ne s'achète qu'à partir de son année de sortie", () =>
        {
            // sierra-vehicules commence en 1875 ; l'E6 Atlantic sort en 1910.
            var scenario = Vehicles();
            Check.Equal("1875", scenario.StartYear.ToString(), "année de départ de sierra-vehicules");
            var e6 = scenario.Vehicles.Catalog!.Locomotives.Single(l => l.Id == "prr_e6_atlantic");
            Check.Equal("1910", e6.Year.ToString(), "année de sortie de l'E6");

            scenario.Trains[0].Locomotive = e6.Id;
            Check.Throws<InvalidDataException>(() => new Simulation(scenario),
                "une machine de 1910 achetée à l'ouverture d'une partie de 1875 doit être refusée");

            // La veille de 1910 : toujours trop tôt. Le premier jour de 1910 : permis.
            scenario.Trains[0].PurchaseTick = (1910 - 1875) * SimTick.TicksPerYear - 1;
            Check.Throws<InvalidDataException>(() => new Simulation(scenario),
                "achetée en 1909, l'E6 n'existe pas encore");
            scenario.Trains[0].PurchaseTick = (1910 - 1875) * SimTick.TicksPerYear;
            var sim = new Simulation(scenario);
            Check.True(sim.World.Trains.Count == scenario.Trains.Count,
                "achetée le premier jour de 1910, l'E6 est permise");
        });

        runner.Add("véhicules — sans année de départ, le module refuse de borner à l'aveugle", () =>
        {
            var scenario = Vehicles();
            scenario.StartYear = 0;
            scenario.Events.StartYear = 0;
            scenario.Objectives.StartYear = 0;
            Check.Throws<InvalidDataException>(() => new Simulation(scenario),
                "un module vehicles actif sans startYear doit être refusé");
        });

        runner.Add("calendrier — un scénario n'a qu'une année de départ", () =>
        {
            // Les années des modules datés doivent confirmer celle du scénario...
            // Vérifié sur le calendrier lui-même, et non au travers de la simulation :
            // le module vehicles refuse aussi un scénario sans année retenue, et un test
            // qui passerait par lui obtiendrait son exception pour une autre raison.
            var contradicted = Vehicles();
            contradicted.Events.StartYear = contradicted.StartYear + 1;
            Check.Throws<InvalidDataException>(() => ScenarioCalendar.Resolve(contradicted),
                "events.startYear contredisant startYear doit être refusé");
            var objectives = Vehicles();
            objectives.Objectives.StartYear = objectives.StartYear - 1;
            Check.Throws<InvalidDataException>(() => ScenarioCalendar.Resolve(objectives),
                "objectives.startYear contredisant startYear doit être refusé");

            // ... et en héritent quand elles sont absentes.
            var inherited = Vehicles();
            Check.True(inherited.Events.StartYear == 0 && inherited.Objectives.StartYear == 0,
                "sierra-vehicules ne déclare l'année qu'au niveau du scénario");
            Check.Equal("1875", ScenarioCalendar.Resolve(inherited).ToString(), "année retenue");
            Check.True(inherited.Events.StartYear == 1875 && inherited.Objectives.StartYear == 1875,
                "les modules datés héritent de l'année du scénario");

            var negative = Vehicles();
            negative.StartYear = -1;
            Check.Throws<InvalidDataException>(() => ScenarioCalendar.Resolve(negative),
                "une année négative doit être refusée");
        });
    }

    /// <summary>
    /// Un trajet sans échange possible, carburant seulement : l'économie est vidée,
    /// sauf une demande de charbon à la destination (le meilleur acheteur en aval),
    /// et une ville intermédiaire garde du charbon sans en consommer, pour que les
    /// prix du plein diffèrent d'une gare à l'autre. Renvoie le carburant payé aux
    /// gares du trajet, destination comprise, et le carburant décidé au départ.
    /// </summary>
    private static (double Paid, double Decided) FuelTrip(string fromCity, string toCity, double load)
    {
        var scenario = Emptied(Vehicles());
        scenario.Cities.Single(c => c.Id == toCity).Demand["coal"] = 0.1;
        scenario.Cities.Single(c => c.Id == "coalpass").InitialStock["coal"] = 500;
        // Le charbon de Coalpass ne doit pas monter à bord : aucun achat ne rapporte
        // assez. Le coût décidé, lui, ne dépend pas de ce seuil.
        scenario.Haulage.MinProfitPerUnit = 1e12;
        // Moins d'un tronçon par tick : arrivé à destination, le train ne peut pas
        // atteindre une autre gare dans le même tick et y payer un plein qui ne
        // relève pas du trajet mesuré.
        scenario.Vehicles.RunningHoursPerTick = 0.3;

        var probe = new Simulation(scenario).World.Lines.Single();
        int from = probe.Stops.FindIndex(s => s.CityId == fromCity);
        int to = probe.Stops.FindIndex(s => s.CityId == toCity);
        scenario.Trains[0].StartStop = from;

        var sim = new Simulation(scenario);
        var world = sim.World;
        var train = world.Trains.Single();
        train.Direction = to > from ? 1 : -1;
        if (load > 0)
        {
            train.Cargo["coal"] = load;
            train.UnitCost["coal"] = 1e9;
        }

        // Le coût que le transporteur impute avant d'acheter, moins sa part « mass »
        // (vérifiée par MarginalCostTests) : ce qui reste est le carburant décidé.
        double decided = ((OpportunisticHaulageSolver)sim.Haulage).HaulCostPerUnitAhead(world, train, "coal")
                       - TrainCost.MarginalCostPerLoad(TrainCost.Rates(scenario.Haulage, train), train.Line, from, to);
        // Jusqu'au plein à destination : c'est là que se paie le dernier tronçon.
        for (int i = 0; i < 2000 && train.Vehicle!.FuelLoadsByCity.GetValueOrDefault(toCity) == 0; i++)
            sim.Step();
        Check.True(train.Vehicle!.FuelLoadsByCity.GetValueOrDefault(toCity) > 0, $"le train doit avoir fait le plein à {toCity}");
        Check.Near(load, train.LoadedUnits, 1e-12, "aucun échange ne doit avoir eu lieu en route");

        int step = to > from ? 1 : -1;
        double paid = 0;
        for (int i = from + step; i != to + step; i += step)
            paid += train.Vehicle.FuelPaidByCity.GetValueOrDefault(train.Line.Stops[i].CityId);
        return (paid, decided);
    }

    /// <summary>Le scénario des véhicules, économie vidée et un seul train, au départ de Pinecrest.</summary>
    private static ScenarioDef Emptied(ScenarioDef scenario)
    {
        foreach (var city in scenario.Cities)
        {
            city.Demand.Clear();
            city.Production.Clear();
            city.InitialStock.Clear();
            city.Industries.Clear();
        }
        scenario.InitialCoverage = 0;
        var train = scenario.Trains[0];
        train.StartStop = 2;
        scenario.Trains = [train];
        return scenario;
    }

    private static string TraceFingerprint(ScenarioDef scenario, IEconomySolver solver)
    {
        var sim = new Simulation(scenario, solver);
        var recorder = new CsvRecorder();
        recorder.Record(sim.World);
        for (int i = 0; i < Ticks; i++)
        {
            sim.Step();
            recorder.Record(sim.World);
        }
        return recorder.TraceFingerprint(sim.World.Events.Summary() + sim.World.Cycle.Summary());
    }

    private static IEconomySolver Solver(string name) => name switch
    {
        "reference" => new ReferenceEconomySolver(),
        "anticipating" => new AnticipatingEconomySolver(),
        _ => throw new ArgumentException($"solveur inconnu : {name}"),
    };
}
