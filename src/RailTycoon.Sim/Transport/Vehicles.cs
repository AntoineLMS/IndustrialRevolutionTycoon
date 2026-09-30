using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim.Transport;

/// <summary>
/// L'état d'une locomotive achetée — ou à acheter — par la compagnie : ce qui
/// distingue un train du module <c>vehicles</c> d'un train déclaré à la main.
/// </summary>
public sealed class Vehicle
{
    public required LocomotiveDef Locomotive { get; init; }

    /// <summary>La marchandise de la carte que cette machine brûle (<c>vehicles.fuels</c>).</summary>
    public required string FuelCargoId { get; init; }

    /// <summary>Tick d'achat. Avant, le train n'existe pas : ni échange, ni carburant, ni entretien.</summary>
    public required int PurchaseTick { get; init; }

    /// <summary>Vrai une fois la machine achetée et payée.</summary>
    public bool InService;

    /// <summary>
    /// Carburant brûlé depuis le dernier plein, en chargements de la marchandise
    /// carburant. Payé au prochain arrêt, au prix local, puis remis à zéro. Rien
    /// n'est prélevé sur aucun stock : c'est un coût indexé sur le prix.
    /// </summary>
    public double FuelOwedLoads;

    /// <summary>Cumuls, pour le harnais et la mesure.</summary>
    public double FuelLoadsBurned;
    public double FuelPaid;
    public double MaintenancePaid;

    /// <summary>Ce qui a été payé en carburant à chaque ville, et combien. Lu dans l'ordre des villes du monde.</summary>
    public readonly Dictionary<string, double> FuelPaidByCity = new();
    public readonly Dictionary<string, double> FuelLoadsByCity = new();

    /// <summary>
    /// Vitesse du tronçon en cours rapportée à la vitesse maximale : 1 à vide sur le
    /// plat si la puissance suffit, moins en charge, en rampe et à cause de la mise
    /// en vitesse. Calculée au départ de chaque tronçon, avec la charge qui en part.
    /// </summary>
    public double LegSpeedFactor = 1.0;

    /// <summary>Nombre de passes du tronçon en cours : plus d'une quand la rampe oblige à couper le train.</summary>
    public int LegPasses = 1;

    /// <summary>Tronçons commencés, et combien il a fallu couper en plusieurs passes.</summary>
    public int LegsStarted;
    public int LegsDoubled;
}

/// <summary>
/// Les règles du module <c>vehicles</c>, en un seul endroit : achat, entretien,
/// carburant, et les masses et tarifs qui en découlent. La dynamique du train
/// (vitesse, adhérence, mise en vitesse) est dans <see cref="TrainDynamics"/>.
/// <para>
/// <b>Comment le carburant s'articule avec le coût kilométrique.</b> Le
/// <c>costPerKm</c> d'un scénario sans véhicules est le coût complet d'un train,
/// carburant compris. Le module le <em>remplace</em>, pour un train tiré par une
/// machine du catalogue, par trois coûts qui ne se recouvrent pas :
/// </para>
/// <code>
/// autres (équipe, wagons…) = vehicles.otherCostPerKm, réparti par le modèle mass
///                            (tare / chargements, × relief), comme costPerKm l'était
/// carburant                = consommation × tonnes brutes × km × relief,
///                            payé en gare au prix local
/// entretien                = maintenancePerTick, dû chaque tick, qu'elle roule ou non
/// </code>
/// <para>
/// Le carburant est la part énergétique du modèle <c>mass</c> rendue explicite : il
/// est proportionnel à la même masse (tare + chargements) et multiplié par le même
/// facteur de relief. D'où la contrainte : le module exige <c>costModel = mass</c> —
/// sous <c>flat</c>, un train plein coûte ce que coûte un train vide, et le
/// carburant n'y aurait pas de coût marginal. La décision d'achat du transporteur
/// lit la dérivée exacte des deux parts qui dépendent de la charge
/// (<see cref="MarginalFuelCost"/>), au prix du moment dans chaque gare où ce
/// carburant sera payé.
/// </para>
/// </summary>
public static class VehicleRules
{
    /// <summary>
    /// Masse brute du train, en tonnes : locomotive et tender, tare des wagons, et
    /// chargement réellement à bord. Un chargement du jeu est un wagon plein de sa
    /// capacité nominale (<c>haulage.massCost</c>) : la masse de la charge vaut
    /// <c>chargements ÷ capacité × wagons × capacité d'un wagon</c>, soit
    /// <c>chargements × tonnesPerLoad</c> puisqu'un train compte un wagon par
    /// chargement de capacité.
    /// </summary>
    public static double GrossTonnes(HaulageDef haulage, Train train, double load)
        => LocomotiveTonnes(haulage, train)
           + train.Capacity * haulage.MassCost.WagonTareTonnes
           + load * haulage.MassCost.TonnesPerLoad;

    /// <summary>
    /// Vitesse maximale d'un train tiré par cette machine : celle du catalogue,
    /// bornée par <see cref="VehiclesDef.MaxTrainSpeedKmh"/> quand le scénario en
    /// déclare une.
    /// </summary>
    public static double TopSpeedKmh(VehiclesDef def, LocomotiveDef loco)
        => def.MaxTrainSpeedKmh > 0 ? Math.Min(loco.TopSpeedKmh, def.MaxTrainSpeedKmh) : loco.TopSpeedKmh;

    /// <summary>La masse de la locomotive de ce train : celle du catalogue, ou celle du scénario à défaut.</summary>
    public static double LocomotiveTonnes(HaulageDef haulage, Train train)
        => train.Vehicle?.Locomotive.MassTonnes ?? haulage.MassCost.LocomotiveTonnes;

    /// <summary>
    /// Consommation en chargements de carburant par kilomètre facturable (kilomètre
    /// × relief) : la part fixe (tare) et la part de chaque chargement. Nulles pour
    /// un train sans locomotive du catalogue.
    /// </summary>
    public static (double FixedPerKm, double PerLoadKm) FuelRates(HaulageDef haulage, Train train)
    {
        if (train.Vehicle is not { } vehicle) return (0, 0);
        double tonnesPerLoad = haulage.MassCost.TonnesPerLoad;
        // kg par tonne-kilomètre, puis chargements par tonne-kilomètre.
        double loadsPerTonneKm = vehicle.Locomotive.FuelKgPer1000TonneKm / 1000.0 / (tonnesPerLoad * 1000.0);
        double tare = LocomotiveTonnes(haulage, train) + train.Capacity * haulage.MassCost.WagonTareTonnes;
        return (loadsPerTonneKm * tare, loadsPerTonneKm * tonnesPerLoad);
    }

    /// <summary>
    /// Le prix du carburant dans une ville : celui de son marché, qu'il y ait du
    /// stock ou non — un coût indexé sur le prix, pas un prélèvement.
    /// <para>
    /// <b>Lecture retenue</b> pour le seul cas où le marché n'a pas de prix : ni
    /// demande, ni stock. Le modèle de prix y affiche le plancher (« personne n'en
    /// veut »), ce qui ferait faire le plein presque gratuitement là où il n'y a pas
    /// un gramme de charbon. Une ville sans carburant ni acheteur se paie donc au
    /// plafond du modèle de prix (<c>priceModel.maxMultiplier</c> × la référence) :
    /// c'est la « ville sans charbon » de docs/VISION.md. Une ville qui en a sans en
    /// consommer — le carreau d'une mine — garde son prix plancher : le charbon y
    /// abonde, et c'est là qu'une ligne à vapeur a intérêt à faire le plein.
    /// </para>
    /// </summary>
    public static double FuelPrice(WorldState world, City city, string cargoId)
    {
        var market = city.Market(cargoId);
        bool noMarket = market.BaseDemandRate + market.IndustryDemandRate <= 0 && market.Stock <= 0;
        return noMarket
            ? world.Cargo(cargoId).BasePrice * world.Def.PriceModel.MaxMultiplier
            : market.Price;
    }

    /// <summary>
    /// Achète la locomotive d'un train au prix du catalogue. La compagnie paie même
    /// si sa caisse n'y suffit pas : c'est le scénario qui a déclaré la flotte, et la
    /// trésorerie négative qui en résulte se lit (sans finance) ou devient un
    /// découvert (avec).
    /// </summary>
    public static void Purchase(WorldState world, Train train)
    {
        if (train.Vehicle is not { InService: false } vehicle) return;
        world.Company.PayForVehicle(vehicle.Locomotive.PurchaseCost);
        vehicle.InService = true;
    }

    /// <summary>Entretien du jour, pour chaque locomotive en service, qu'elle roule ou non.</summary>
    public static void PayMaintenance(WorldState world)
    {
        foreach (var train in world.Trains)
        {
            if (train.Vehicle is not { InService: true } vehicle) continue;
            double amount = vehicle.Locomotive.MaintenancePerTick;
            if (amount <= 0) continue;
            world.Company.PayVehicleMaintenance(amount);
            vehicle.MaintenancePaid += amount;
        }
    }

    /// <summary>Le plein, à l'arrivée en gare : le carburant brûlé depuis le dernier arrêt, au prix local.</summary>
    public static void Refuel(WorldState world, Train train, City city)
    {
        if (train.Vehicle is not { } vehicle || vehicle.FuelOwedLoads <= 0) return;
        double loads = vehicle.FuelOwedLoads;
        double cost = loads * FuelPrice(world, city, vehicle.FuelCargoId);
        world.Company.PayFuel(cost);
        vehicle.FuelOwedLoads = 0;
        vehicle.FuelPaid += cost;
        vehicle.FuelPaidByCity[city.Id] = vehicle.FuelPaidByCity.GetValueOrDefault(city.Id) + cost;
        vehicle.FuelLoadsByCity[city.Id] = vehicle.FuelLoadsByCity.GetValueOrDefault(city.Id) + loads;
    }

    /// <summary>
    /// Ce qu'un chargement de plus coûtera en carburant d'ici à l'arrêt
    /// <paramref name="toIndex"/> : sur chaque tronçon, la consommation d'un
    /// chargement × kilomètres × relief, payée à l'arrêt qui termine le tronçon, au
    /// prix qu'il affiche aujourd'hui. C'est la dérivée exacte de ce que
    /// <see cref="Refuel"/> encaissera si les prix ne bougent pas en route.
    /// </summary>
    public static double MarginalFuelCost(WorldState world, Train train, int fromIndex, int toIndex)
    {
        if (train.Vehicle is not { } vehicle || fromIndex == toIndex) return 0;
        var (_, perLoadKm) = FuelRates(world.Def.Haulage, train);
        var line = train.Line;
        int step = toIndex > fromIndex ? 1 : -1;
        double total = 0;
        for (int i = fromIndex; i != toIndex; i += step)
        {
            int next = i + step;
            double chargeable = line.DistanceBetween(i, next) * line.LegCostFactor(i, next);
            var city = world.CityById(line.Stops[next].CityId);
            total += perLoadKm * chargeable * FuelPrice(world, city, vehicle.FuelCargoId);
        }
        return total;
    }

    /// <summary>
    /// Vitesse d'un tronçon rapportée à la vitesse maximale, pour la charge qui part
    /// de l'arrêt : le temps du tronçon selon <see cref="TrainDynamics"/>, converti en
    /// facteur de la vitesse nominale <c>topSpeedKmh × runningHoursPerTick</c>. Vaut
    /// 1 pour un train sans locomotive du catalogue — et multiplier ou diviser par 1
    /// ne change aucun bit, ce qui laisse les trains déclarés à la main tels quels.
    /// </summary>
    public static void StartLeg(WorldState world, Train train, int fromIndex, int toIndex)
    {
        if (train.Vehicle is not { } vehicle) return;
        var def = world.Def.Vehicles;
        var loco = vehicle.Locomotive;
        double distance = train.Line.DistanceBetween(fromIndex, toIndex);
        if (distance <= 0)
        {
            // Deux arrêts confondus : rien à parcourir, rien à ralentir.
            vehicle.LegSpeedFactor = 1.0;
            vehicle.LegPasses = 1;
            return;
        }
        double gradient = train.Line.LegClimbGradient(fromIndex, toIndex);
        double gross = GrossTonnes(world.Def.Haulage, train, train.LoadedUnits);

        var leg = TrainDynamics.Leg(Physics(def, world.Def.Haulage, train), gross, gradient, distance, def.Acceleration);
        vehicle.LegSpeedFactor = Math.Min(1.0, distance / (leg.Hours * TopSpeedKmh(def, loco)));
        vehicle.LegPasses = leg.Passes;
        vehicle.LegsStarted++;
        if (leg.Passes > 1) vehicle.LegsDoubled++;
    }

    public static TrainPhysics Physics(VehiclesDef def, HaulageDef haulage, Train train)
    {
        var loco = train.Vehicle!.Locomotive;
        return new TrainPhysics(
            LocomotiveTonnes: LocomotiveTonnes(haulage, train),
            TractiveEffortKn: loco.TractiveEffortKn,
            PowerKw: loco.PowerKw,
            TopSpeedKmh: TopSpeedKmh(def, loco),
            RollingResistance: def.RollingResistance);
    }

    /// <summary>
    /// Validation du bloc au chargement. Tout ce qui ferait tourner le module de
    /// travers sans le dire est refusé ici : une locomotive inconnue, un carburant
    /// que la carte ne connaît pas, un modèle de coût qui n'a pas de coût marginal.
    /// </summary>
    public static void Validate(ScenarioDef s)
    {
        var def = s.Vehicles;
        if (!def.Enabled)
        {
            foreach (var train in s.Trains)
                if (train.PurchaseTick != 0)
                    throw new InvalidDataException(
                        $"Le train '{train.Id}' déclare purchaseTick = {train.PurchaseTick} sans module vehicles actif : " +
                        "il serait silencieusement ignoré.");
            return;
        }

        if (def.Catalog is null)
            throw new InvalidDataException(
                "vehicles.enabled sans catalogue chargé : passer par ScenarioLoader.Load, qui lit vehicles.catalogPath.");
        if (!TrainCost.IsMassModel(s.Haulage))
            throw new InvalidDataException(
                "Le module vehicles exige haulage.costModel = « mass » : le carburant est proportionnel à la masse " +
                "remorquée, et sous « flat » un chargement n'aurait pas de coût marginal.");
        if (def.OtherCostPerKm < 0)
            throw new InvalidDataException("vehicles.otherCostPerKm ne peut pas être négatif.");
        if (def.RunningHoursPerTick <= 0 || def.RunningHoursPerTick > 24)
            throw new InvalidDataException("vehicles.runningHoursPerTick doit être dans ]0, 24].");
        if (def.RollingResistance <= 0)
            throw new InvalidDataException("vehicles.rollingResistance doit être strictement positive.");
        if (def.MaxTrainSpeedKmh < 0)
            throw new InvalidDataException("vehicles.maxTrainSpeedKmh ne peut pas être négative (0 : pas de limite).");

        var cargoIds = s.Cargos.Select(c => c.Id).ToHashSet();
        foreach (var train in s.Trains)
        {
            if (string.IsNullOrWhiteSpace(train.Locomotive))
                throw new InvalidDataException(
                    $"Module vehicles actif : le train '{train.Id}' ne référence aucune locomotive du catalogue.");
            var loco = def.Catalog.Locomotives.FirstOrDefault(l => l.Id == train.Locomotive)
                ?? throw new InvalidDataException(
                    $"Le train '{train.Id}' référence la locomotive inconnue '{train.Locomotive}'.");
            if (!def.Fuels.TryGetValue(loco.FuelType, out string? fuelCargo))
                throw new InvalidDataException(
                    $"La locomotive '{loco.Id}' du train '{train.Id}' brûle « {loco.FuelType} », " +
                    $"et le scénario ne déclare pas la marchandise carburant correspondante (vehicles.fuels.{loco.FuelType}).");
            if (!cargoIds.Contains(fuelCargo))
                throw new InvalidDataException(
                    $"vehicles.fuels.{loco.FuelType} désigne la marchandise inconnue '{fuelCargo}'.");
            if (train.PurchaseTick < 0)
                throw new InvalidDataException($"Le train '{train.Id}' a un purchaseTick négatif.");
        }
    }

    /// <summary>Le véhicule d'un train, ou rien si le module est inactif.</summary>
    internal static Vehicle? Build(ScenarioDef s, TrainDef trainDef)
    {
        if (!s.Vehicles.Enabled) return null;
        var loco = s.Vehicles.Catalog!.Locomotives.First(l => l.Id == trainDef.Locomotive);
        return new Vehicle
        {
            Locomotive = loco,
            FuelCargoId = s.Vehicles.Fuels[loco.FuelType],
            PurchaseTick = trainDef.PurchaseTick,
        };
    }

    /// <summary>
    /// Vérifie, une fois les lignes construites, qu'aucune machine ne reste clouée au
    /// pied d'une rampe : seule, sans wagon, elle doit pouvoir démarrer sur la rampe
    /// moyenne de chaque tronçon de sa ligne, dans les deux sens. Sinon aucun nombre
    /// de passes ne la ferait monter, et le scénario est faux.
    /// </summary>
    internal static void CheckClimbable(WorldState world, Train train)
    {
        if (train.Vehicle is not { } vehicle) return;
        var physics = Physics(world.Def.Vehicles, world.Def.Haulage, train);
        var line = train.Line;
        for (int i = 0; i + 1 < line.Stops.Count; i++)
        {
            foreach (var (from, to) in new[] { (i, i + 1), (i + 1, i) })
            {
                double gross = VehicleRules.GrossTonnes(world.Def.Haulage, train, 0);
                double wagons = gross - physics.LocomotiveTonnes;
                double maxGross = TrainDynamics.MaxStartingTonnes(physics, line.LegClimbGradient(from, to));
                if (maxGross <= physics.LocomotiveTonnes + Math.Min(wagons, 1.0))
                    throw new InvalidDataException(
                        $"La locomotive '{vehicle.Locomotive.Id}' du train '{train.Id}' ne peut pas gravir le tronçon " +
                        $"{line.Stops[from].CityId} → {line.Stops[to].CityId} : son adhérence ne démarre pas même " +
                        "une tonne de wagons derrière elle, et aucun nombre de passes ne la ferait monter.");
            }
        }
    }
}

/// <summary>Ce que la dynamique d'un train connaît de lui : sa locomotive, et la résistance au roulement.</summary>
public readonly record struct TrainPhysics(
    double LocomotiveTonnes, double TractiveEffortKn, double PowerKw, double TopSpeedKmh, double RollingResistance);

/// <summary>Le temps d'un tronçon : en heures, en combien de passes, et à quelle vitesse de croisière.</summary>
public readonly record struct LegTime(double Hours, int Passes, double CruiseKmh);

/// <summary>
/// La dynamique d'un train sur un tronçon, calculée d'un coup au départ de chaque
/// gare : le pas de temps est d'un jour, on ne simule pas les secondes.
/// <para>
/// <b>Le modèle</b>, le plus simple qui tienne debout, avec
/// <c>M</c> la masse brute (locomotive, tender, wagons, chargement) :
/// </para>
/// <code>
/// résistance    R = M g (r + i)            r : roulement, i : rampe moyenne du tronçon
/// effort        F(v) = min(TE, P / v)      TE : adhérence au démarrage, P : puissance
/// croisière     v_c = min(v_max, P / R)
/// accélération  a(v) = (F(v) − R) / M      de 0 à 98 % de v_c (au plus v_max)
/// temps         t = t_accél + (d − x_accél) / v_c
/// </code>
/// <para>
/// L'accélération se calcule en deux morceaux : à effort constant (adhérence) tant
/// que <c>v &lt; P / TE</c>, exactement ; puis à puissance constante, par une
/// intégration en vitesse sur 200 pas (point milieu). L'approche de <c>P / R</c> est
/// asymptotique quand la puissance limite : la phase s'arrête à 98 % et le reste du
/// tronçon se fait à <c>v_c</c>. Un tronçon trop court pour atteindre la croisière
/// s'arrête en cours d'accélération. Le freinage n'est pas compté.
/// </para>
/// <para>
/// <b>Quand l'adhérence ne suffit pas</b> — <c>R &gt; TE</c> —, le train ne démarre pas
/// sur la rampe. L'équipe fait alors ce que faisaient les chemins de fer de 1870 :
/// elle coupe le train et monte en plusieurs passes (« doubling the hill »), la
/// locomotive redescendant haut-le-pied entre deux, à sa vitesse maximale. Parmi les
/// nombres de passes possibles, elle choisit celui qui arrive le plus tôt — ce qui
/// évite qu'un train à 99 % de son adhérence mette une semaine à s'arracher.
/// </para>
/// </summary>
public static class TrainDynamics
{
    /// <summary>Accélération de la pesanteur, m/s². Une constante physique, pas un réglage.</summary>
    private const double G = 9.81;

    /// <summary>Fraction de la vitesse limite par la puissance où s'arrête la phase d'accélération.</summary>
    private const double CruiseApproach = 0.98;

    private const int PowerSteps = 200;

    /// <summary>Masse brute maximale, en tonnes, que la machine peut démarrer sur une rampe donnée.</summary>
    public static double MaxStartingTonnes(TrainPhysics p, double gradient)
        => p.TractiveEffortKn * 1000.0 / (G * (p.RollingResistance + gradient)) / 1000.0;

    public static LegTime Leg(TrainPhysics p, double grossTonnes, double gradient, double distanceKm, bool acceleration)
    {
        double maxGross = MaxStartingTonnes(p, gradient);
        double trailing = grossTonnes - p.LocomotiveTonnes;

        int minPasses = 1;
        if (grossTonnes > maxGross)
        {
            double perPass = maxGross - p.LocomotiveTonnes;
            // Strictement sous l'adhérence : un train exactement à la limite ne
            // démarrerait jamais.
            minPasses = (int)Math.Floor(trailing / perPass) + 1;
        }

        LegTime best = default;
        for (int passes = minPasses; passes < minPasses + 4; passes++)
        {
            double part = p.LocomotiveTonnes + trailing / passes;
            var single = SinglePass(p, part, gradient, distanceKm, acceleration);
            double hours = passes * single.Hours + (passes - 1) * distanceKm / p.TopSpeedKmh;
            if (passes == minPasses || hours < best.Hours)
                best = new LegTime(hours, passes, single.CruiseKmh);
        }
        return best;
    }

    /// <summary>Une passe, avec une masse que l'adhérence peut démarrer.</summary>
    public static LegTime SinglePass(TrainPhysics p, double grossTonnes, double gradient, double distanceKm, bool acceleration)
    {
        double m = grossTonnes * 1000.0;
        double resistance = m * G * (p.RollingResistance + gradient);
        double tractive = p.TractiveEffortKn * 1000.0;
        double power = p.PowerKw * 1000.0;
        double vmax = p.TopSpeedKmh / 3.6;
        double vPower = power / resistance;
        double cruise = Math.Min(vmax, vPower);
        double d = distanceKm * 1000.0;

        if (!acceleration)
            return new LegTime(d / cruise / 3600.0, 1, cruise * 3.6);

        double target = Math.Min(vmax, CruiseApproach * vPower);
        double t = 0, x = 0;

        // Phase 1 — à effort constant, jusqu'à ce que la puissance prenne le relais.
        double v1 = Math.Min(power / tractive, target);
        double a = Math.Max((tractive - resistance) / m, 1e-9);
        double x1 = v1 * v1 / (2 * a);
        if (x1 >= d) return new LegTime(Math.Sqrt(2 * d / a) / 3600.0, 1, cruise * 3.6);
        t += v1 / a;
        x += x1;

        // Phase 2 — à puissance constante, intégrée en vitesse.
        if (target > v1)
        {
            double dv = (target - v1) / PowerSteps;
            for (int i = 0; i < PowerSteps; i++)
            {
                double v = v1 + (i + 0.5) * dv;
                double acc = (Math.Min(tractive, power / v) - resistance) / m;
                double dt = dv / acc;
                double dx = v * dt;
                if (x + dx >= d) return new LegTime((t + (d - x) / v) / 3600.0, 1, cruise * 3.6);
                t += dt;
                x += dx;
            }
        }

        t += (d - x) / cruise;
        return new LegTime(t / 3600.0, 1, cruise * 3.6);
    }
}
