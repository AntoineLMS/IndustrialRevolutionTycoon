using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim.Transport;

/// <summary>
/// Les deux tarifs d'un train, par kilomètre facturable (kilomètre parcouru ×
/// facteur de relief du tronçon et du sens) : ce qu'il paie quoi qu'il porte, et ce
/// que chaque chargement y ajoute.
/// </summary>
public readonly record struct TrainCostRates(double FixedPerKm, double PerLoadKm);

/// <summary>
/// Le coût d'exploitation d'un train, en un seul endroit : ce que le transporteur
/// paie en roulant (<c>OpportunisticHaulageSolver.MoveTrain</c>) et ce qu'il croit
/// qu'un chargement lui coûtera quand il décide de l'acheter
/// (<c>HaulCostPerUnitAhead</c>) sortent des mêmes tarifs et du même facteur de
/// relief.
/// <para>
/// <b>Modèle <c>flat</c></b> (défaut) : <c>FixedPerKm = costPerKm</c>,
/// <c>PerLoadKm = 0</c>. Le train paie le même prix plein ou vide, et la décision
/// impute un coût moyen — le coût du train réparti sur une charge escomptée, sur la
/// distance à plat. Ce n'est <em>pas</em> la même formule que la facture, et c'est
/// ce que docs/FINDINGS.md (« Relief et économie ensemble ») a mesuré : le relief
/// est facturé mais jamais décidé. Le modèle reste le défaut parce que toutes les
/// empreintes de référence en dépendent ; en changer est une décision de l'équipe.
/// </para>
/// <para>
/// <b>Modèle <c>mass</c></b> : le coût kilométrique est proportionnel à la masse
/// remorquée. La tare — locomotive, tender et wagons vides — fait la part fixe ; la
/// masse des chargements fait la part variable ; les deux sont multipliées par le
/// facteur de relief, parce qu'une rampe se gravit avec toute la masse du train. Le
/// prix de la tonne-kilomètre est fixé pour qu'à
/// <see cref="MassCostDef.CalibrationLoadFactor"/> de sa capacité, le train coûte
/// exactement son <c>costPerKm</c> :
/// </para>
/// <code>
/// tare         = locomotiveTonnes + capacité × wagonTareTonnes
/// masse_calib  = tare + calibrationLoadFactor × capacité × tonnesPerLoad
/// FixedPerKm   = costPerKm × tare / masse_calib
/// PerLoadKm    = costPerKm × tonnesPerLoad / masse_calib
/// facture      = Σ tronçons  km × relief(tronçon, sens) × (FixedPerKm + PerLoadKm × charge)
/// décision     = PerLoadKm × Σ tronçons jusqu'à la destination  km × relief(tronçon, sens)
/// </code>
/// <para>
/// La décision est la dérivée exacte de la facture par rapport à la charge : un
/// chargement acheté ici et vendu à la destination visée ajoute à la facture
/// exactement ce que la décision lui a imputé. Refuser un chargement économise ce
/// qu'il coûte, ni plus ni moins, et le relief y entre par le même facteur que dans
/// la facture. Aucune charge escomptée n'intervient.
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
        // La locomotive du catalogue, sous le module vehicles ; celle du scénario
        // sinon — le même nombre qu'avant le module, donc les mêmes bits.
        double tare = VehicleRules.LocomotiveTonnes(def, train) + train.Capacity * mass.WagonTareTonnes;
        double calibrationMass = tare + mass.CalibrationLoadFactor * train.Capacity * mass.TonnesPerLoad;
        return new TrainCostRates(
            train.CostPerKm * tare / calibrationMass,
            train.CostPerKm * mass.TonnesPerLoad / calibrationMass);
    }

    /// <summary>
    /// La facture d'un déplacement : <paramref name="chargeableKm"/> est la somme des
    /// kilomètres pondérés par le relief, <paramref name="chargeableLoadKm"/> la même
    /// somme pondérée en plus par la charge portée sur chaque tronçon.
    /// <para>
    /// Sous le modèle <c>flat</c>, <c>PerLoadKm</c> vaut 0 et la facture vaut
    /// <c>chargeableKm × costPerKm</c> au bit près — ajouter 0,0 à un nombre fini ne
    /// change aucun bit —, ce qui laisse toutes les empreintes de référence en place.
    /// </para>
    /// </summary>
    public static double Charge(TrainCostRates rates, double chargeableKm, double chargeableLoadKm)
        => chargeableKm * rates.FixedPerKm + chargeableLoadKm * rates.PerLoadKm;

    /// <summary>
    /// Modèle <c>mass</c> : ce qu'un chargement de plus ajoute à la facture entre
    /// deux arrêts, dans le sens du parcours, relief compris.
    /// </summary>
    public static double MarginalCostPerLoad(TrainCostRates rates, RailLine line, int fromIndex, int toIndex)
        => rates.PerLoadKm * line.DistanceBetween(fromIndex, toIndex) * line.LegCostFactor(fromIndex, toIndex);

    /// <summary>
    /// Refuse au chargement un modèle inconnu ou des masses absurdes. Une faute de
    /// frappe sur <c>costModel</c> ferait sinon tourner le scénario sous le modèle
    /// <c>flat</c> sans le dire — un bloc silencieusement inerte, la famille de bugs
    /// que la règle 8 de docs/CONTRACTS.md existe pour empêcher.
    /// </summary>
    public static void Validate(HaulageDef def)
    {
        if (def.CostModel != FlatModel && def.CostModel != MassModel)
            throw new InvalidDataException(
                $"haulage.costModel inconnu : « {def.CostModel} » (attendu « {FlatModel} » ou « {MassModel} »).");
        if (!IsMassModel(def)) return;

        var mass = def.MassCost;
        if (mass.LocomotiveTonnes < 0 || mass.WagonTareTonnes < 0)
            throw new InvalidDataException("haulage.massCost : une tare ne peut pas être négative.");
        if (mass.TonnesPerLoad <= 0)
            throw new InvalidDataException("haulage.massCost.tonnesPerLoad doit être strictement positif.");
        if (mass.LocomotiveTonnes + mass.WagonTareTonnes <= 0)
            throw new InvalidDataException(
                "haulage.massCost : un train sans tare ne paierait rien à vide, et le modèle cesserait d'avoir une part fixe.");
        if (mass.CalibrationLoadFactor < 0 || mass.CalibrationLoadFactor > 1)
            throw new InvalidDataException("haulage.massCost.calibrationLoadFactor doit être entre 0 et 1.");
    }
}
