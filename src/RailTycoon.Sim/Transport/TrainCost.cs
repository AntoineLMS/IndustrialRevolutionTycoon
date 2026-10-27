using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim.Transport;

/// <summary>
/// Les deux tarifs d'un train, par kilomètre facturable (kilomètre parcouru ×
/// facteur de relief du tronçon et du sens) : ce qu'il paie quoi qu'il porte, et ce
/// que chaque tonne de chargement, wagon compris, y ajoute.
/// </summary>
public readonly record struct TrainCostRates(double FixedPerKm, double PerTonneKm);

/// <summary>
/// Le coût d'exploitation d'un train, en un seul endroit : ce que le transporteur
/// paie en roulant (<c>OpportunisticHaulageSolver.MoveTrain</c>) et ce qu'il croit
/// qu'un chargement lui coûtera quand il décide de l'acheter
/// (<c>HaulCostPerUnitAhead</c>) sortent des mêmes tarifs et du même facteur de
/// relief.
/// <para>
/// <b>Modèle <c>mass</c></b> (défaut) : le coût kilométrique est proportionnel à la
/// masse remorquée. Le wagon suit le chargement — attelé à l'achat, dételé à la
/// vente —, si bien que la locomotive fait seule la part fixe, et que chaque
/// chargement pèse sa marchandise plus la tare de son wagon, qui dépend du type
/// déclaré par la marchandise. Les deux parts sont multipliées par le facteur de
/// relief, parce qu'une rampe se gravit avec toute la masse du train. Le prix de la
/// tonne-kilomètre est fixé pour qu'à <see cref="MassCostDef.CalibrationLoadFactor"/>
/// de sa capacité en wagons de <see cref="MassCostDef.CalibrationWagon"/>, le train
/// coûte exactement son <c>costPerKm</c> :
/// </para>
/// <code>
/// chargement(m) = tonnesPerLoad + tare(wagon de m)
/// masse_calib   = locomotiveTonnes + calibrationLoadFactor × capacité × chargement(wagon de calibration)
/// PerTonneKm    = costPerKm / masse_calib
/// FixedPerKm    = PerTonneKm × locomotiveTonnes
/// facture       = Σ tronçons  km × relief(tronçon, sens) × (FixedPerKm + PerTonneKm × Σ_m charge(m) × chargement(m))
/// décision(m)   = PerTonneKm × chargement(m) × Σ tronçons jusqu'à la destination  km × relief(tronçon, sens)
/// </code>
/// <para>
/// La décision est la dérivée exacte de la facture par rapport à la charge de la
/// marchandise : un chargement acheté ici et vendu à la destination visée ajoute à
/// la facture exactement ce que la décision lui a imputé. Refuser un chargement
/// économise ce qu'il coûte, ni plus ni moins, et le relief y entre par le même
/// facteur que dans la facture. Aucune charge escomptée n'intervient.
/// </para>
/// <para>
/// <b>Modèle <c>flat</c></b> (ancien défaut, opt-in) : <c>FixedPerKm = costPerKm</c>,
/// <c>PerTonneKm = 0</c>. Le train paie le même prix plein ou vide, et la décision
/// impute un coût moyen — le coût du train réparti sur une charge escomptée, sur la
/// distance à plat. Ce n'est <em>pas</em> la même formule que la facture : c'est ce
/// que docs/FINDINGS.md (« Relief et économie ensemble ») a mesuré, le relief y est
/// facturé mais jamais décidé. Conservé pour relire les mesures antérieures au
/// 27 octobre 2026.
/// </para>
/// </summary>
public static class TrainCost
{
    public const string FlatModel = "flat";
    public const string MassModel = "mass";

    public static bool IsMassModel(HaulageDef def) => def.CostModel == MassModel;

    /// <summary>Tarifs d'un train sous le modèle de coût du scénario.</summary>
    public static TrainCostRates Rates(HaulageDef def, Train train)
    {
        if (!IsMassModel(def)) return new TrainCostRates(train.CostPerKm, 0);

        var mass = def.MassCost;
        double calibrationLoad = mass.TonnesPerLoad + mass.WagonTypes[mass.CalibrationWagon].TareTonnes;
        double calibrationMass = mass.LocomotiveTonnes + mass.CalibrationLoadFactor * train.Capacity * calibrationLoad;
        double perTonneKm = train.CostPerKm / calibrationMass;
        return new TrainCostRates(perTonneKm * mass.LocomotiveTonnes, perTonneKm);
    }

    /// <summary>Masse d'un chargement de <paramref name="cargo"/> : la marchandise et son wagon.</summary>
    public static double LoadTonnes(HaulageDef def, CargoDef cargo)
        => def.MassCost.TonnesPerLoad + def.MassCost.WagonTypes[cargo.Wagon].TareTonnes;

    /// <summary>
    /// Masse que portent les wagons attelés au train, en tonnes : chaque chargement,
    /// marchandise et wagon. Nulle sous le modèle <c>flat</c>, qui ne lit aucune
    /// masse. Parcourue dans l'ordre canonique des marchandises : une somme de
    /// <c>double</c> dépend de son ordre.
    /// </summary>
    public static double LoadedTonnes(WorldState world, Train train)
    {
        var def = world.Def.Haulage;
        if (!IsMassModel(def)) return 0;

        double tonnes = 0;
        foreach (string cargoId in world.CargoOrder)
            if (train.Cargo.TryGetValue(cargoId, out double held) && held > 0)
                tonnes += held * LoadTonnes(def, world.Cargo(cargoId));
        return tonnes;
    }

    /// <summary>
    /// La facture d'un déplacement : <paramref name="chargeableKm"/> est la somme des
    /// kilomètres pondérés par le relief, <paramref name="chargeableTonneKm"/> la même
    /// somme pondérée en plus par la masse des chargements portée sur chaque tronçon.
    /// <para>
    /// Sous le modèle <c>flat</c>, <c>PerTonneKm</c> vaut 0 et la facture vaut
    /// <c>chargeableKm × costPerKm</c> au bit près — ajouter 0,0 à un nombre fini ne
    /// change aucun bit —, ce qui garde les mesures antérieures reproductibles.
    /// </para>
    /// </summary>
    public static double Charge(TrainCostRates rates, double chargeableKm, double chargeableTonneKm)
        => chargeableKm * rates.FixedPerKm + chargeableTonneKm * rates.PerTonneKm;

    /// <summary>
    /// Modèle <c>mass</c> : ce qu'un chargement de <paramref name="cargo"/> de plus
    /// ajoute à la facture entre deux arrêts, dans le sens du parcours, relief compris.
    /// </summary>
    public static double MarginalCostPerLoad(
        TrainCostRates rates, HaulageDef def, CargoDef cargo, RailLine line, int fromIndex, int toIndex)
        => rates.PerTonneKm * LoadTonnes(def, cargo)
         * line.DistanceBetween(fromIndex, toIndex) * line.LegCostFactor(fromIndex, toIndex);

    /// <summary>
    /// Refuse au chargement un modèle inconnu, des masses absurdes ou un wagon
    /// inconnu. Une faute de frappe sur <c>costModel</c> ferait sinon tourner un
    /// autre modèle sans le dire, et une faute sur un wagon ferait échouer la partie
    /// au premier chargement de la marchandise — un bloc silencieusement inerte, ou
    /// une exception obscure au millième tick, la famille de bugs que la règle 8 de
    /// docs/CONTRACTS.md existe pour empêcher.
    /// </summary>
    public static void Validate(ScenarioDef scenario)
    {
        var def = scenario.Haulage;
        if (def.CostModel != FlatModel && def.CostModel != MassModel)
            throw new InvalidDataException(
                $"haulage.costModel inconnu : « {def.CostModel} » (attendu « {MassModel} » ou « {FlatModel} »).");
        if (!IsMassModel(def)) return;

        var mass = def.MassCost;
        if (mass.LocomotiveTonnes <= 0)
            throw new InvalidDataException(
                "haulage.massCost.locomotiveTonnes doit être strictement positif : la locomotive fait seule " +
                "la part fixe, et un train sans elle ne paierait rien à vide.");
        if (mass.TonnesPerLoad <= 0)
            throw new InvalidDataException("haulage.massCost.tonnesPerLoad doit être strictement positif.");
        if (mass.CalibrationLoadFactor < 0 || mass.CalibrationLoadFactor > 1)
            throw new InvalidDataException("haulage.massCost.calibrationLoadFactor doit être entre 0 et 1.");
        if (mass.WagonTypes.Count == 0)
            throw new InvalidDataException("haulage.massCost.wagonTypes ne déclare aucun type de wagon.");
        foreach (var (id, wagon) in mass.WagonTypes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (wagon.TareTonnes < 0)
                throw new InvalidDataException($"haulage.massCost.wagonTypes.{id} : une tare ne peut pas être négative.");
        if (!mass.WagonTypes.ContainsKey(mass.CalibrationWagon))
            throw new InvalidDataException(
                $"haulage.massCost.calibrationWagon : wagon inconnu « {mass.CalibrationWagon} ».");
        foreach (var cargo in scenario.Cargos)
            if (!mass.WagonTypes.ContainsKey(cargo.Wagon))
                throw new InvalidDataException(
                    $"La marchandise '{cargo.Id}' est portée par un wagon inconnu « {cargo.Wagon} » " +
                    $"(types déclarés : {string.Join(", ", mass.WagonTypes.Keys.OrderBy(k => k, StringComparer.Ordinal))}).");
    }
}
