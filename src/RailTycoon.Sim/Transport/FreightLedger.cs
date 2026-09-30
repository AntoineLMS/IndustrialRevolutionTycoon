namespace RailTycoon.Sim.Transport;

/// <summary>
/// Le carnet de route d'une compagnie : ce que ses trains ont vendu et acheté, ville
/// par ville et marchandise par marchandise, et les gares où chacun de ses trains
/// s'est arrêté. Écrit par le transport à l'instant même de l'échange ou de
/// l'arrivée, lu par qui veut savoir ce que la compagnie a fait — aujourd'hui le
/// module <c>objectives</c>.
/// <para>
/// <b>Un observateur, pas un compte.</b> Rien de la simulation ne le lit pour
/// décider : ni le transporteur, ni l'économie, ni la finance. Il est tenu dans tous
/// les scénarios, objectifs ou non, et aucune empreinte de référence n'a bougé à
/// son arrivée — c'est la preuve qu'il n'agit sur rien. L'argent, lui, reste dans
/// <see cref="Economy.Company"/> ; le carnet ne compte que des chargements.
/// </para>
/// <para>
/// <b>Pourquoi dans le transport, et pas dans les marchés.</b> <c>Market.Deposit</c>
/// et <c>Market.Withdraw</c> ne sont appelés aujourd'hui que par le transporteur, et
/// l'on aurait pu cumuler les imports et exports d'un marché. Mais un marché ne sait
/// pas <em>qui</em> y a vendu : le jour où un concurrent fera rouler ses trains, ou
/// où une industrie fondée par une compagnie se servira au marché, le cumul d'un
/// marché mélangerait tout le monde. Un objectif appartient à un joueur ; le carnet
/// appartient à sa compagnie.
/// </para>
/// </summary>
public sealed class FreightLedger
{
    // Clé « marchandise → ville → chargements ». Jamais énumérés : toute lecture
    // passe par un identifiant, et un parcours se fait dans l'ordre canonique du
    // monde (WorldState.CargoOrder, WorldState.Cities), pas dans celui du
    // dictionnaire (docs/ARCHITECTURE.md, règle 3).
    private readonly Dictionary<string, Dictionary<string, double>> _sold = new();
    private readonly Dictionary<string, Dictionary<string, double>> _bought = new();

    // Clé « train et ligne → villes où il s'est arrêté ». La ligne fait partie de la
    // clé : le jour où un train changera d'ordres, ce qu'il a desservi sur son
    // ancienne ligne ne prouvera rien de la nouvelle.
    private readonly Dictionary<string, HashSet<string>> _visited = new();

    /// <summary>Chargements de <paramref name="cargoId"/> vendus par les trains de la compagnie dans <paramref name="cityId"/>.</summary>
    public double Sold(string cargoId, string cityId) => Read(_sold, cargoId, cityId);

    /// <summary>Chargements de <paramref name="cargoId"/> achetés par les trains de la compagnie dans <paramref name="cityId"/>.</summary>
    public double Bought(string cargoId, string cityId) => Read(_bought, cargoId, cityId);

    /// <summary>
    /// Ce que le rail a laissé dans une ville : vendu moins racheté, jamais négatif.
    /// C'est la définition de « livré » que retient le module <c>objectives</c> ; la
    /// raison est dans son contrat (docs/CONTRACTS.md) et dans docs/FINDINGS.md.
    /// </summary>
    public double NetDelivered(string cargoId, string cityId)
        => Math.Max(0.0, Sold(cargoId, cityId) - Bought(cargoId, cityId));

    /// <summary>Vrai si <paramref name="train"/>, sur sa ligne actuelle, s'est arrêté à la gare de <paramref name="cityId"/>.</summary>
    public bool HasVisited(Train train, string cityId)
        => _visited.TryGetValue(VisitKey(train), out var cities) && cities.Contains(cityId);

    internal void RecordSale(string cargoId, string cityId, double qty) => Add(_sold, cargoId, cityId, qty);

    internal void RecordPurchase(string cargoId, string cityId, double qty) => Add(_bought, cargoId, cityId, qty);

    internal void RecordArrival(Train train, string cityId)
    {
        string key = VisitKey(train);
        if (!_visited.TryGetValue(key, out var cities))
            _visited[key] = cities = new HashSet<string>();
        cities.Add(cityId);
    }

    private static string VisitKey(Train train) => train.Id + "\n" + train.Line.Id;

    private static double Read(Dictionary<string, Dictionary<string, double>> table, string cargoId, string cityId)
        => table.TryGetValue(cargoId, out var byCity) && byCity.TryGetValue(cityId, out double qty) ? qty : 0.0;

    private static void Add(Dictionary<string, Dictionary<string, double>> table, string cargoId, string cityId, double qty)
    {
        if (qty <= 0) return;
        if (!table.TryGetValue(cargoId, out var byCity))
            table[cargoId] = byCity = new Dictionary<string, double>();
        byCity[cityId] = (byCity.TryGetValue(cityId, out double current) ? current : 0.0) + qty;
    }
}
