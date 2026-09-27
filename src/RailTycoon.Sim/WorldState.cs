using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Finance;
using RailTycoon.Sim.Transport;

namespace RailTycoon.Sim;

/// <summary>
/// L'état complet du monde à un instant donné. Tous les modules (économie,
/// transport, plus tard finance et IA) reçoivent cet objet et n'ont pas d'autre
/// source de vérité.
/// <para>
/// Point d'attention permanent : <b>tout parcours de collection doit être
/// ordonné de façon stable</b>. Les listes le sont par construction, les
/// dictionnaires ne le sont pas. C'est pourquoi on passe toujours par
/// <see cref="MarketsOf"/> et <see cref="CargoOrder"/> plutôt que d'itérer
/// directement sur un dictionnaire : un ordre de parcours différent donne des
/// résultats différents et casse la reproductibilité.
/// </para>
/// </summary>
public sealed class WorldState
{
    public required ScenarioDef Def { get; init; }
    public required IPriceModel PriceModel { get; init; }
    public required DeterministicRandom Rng { get; init; }
    public required Company Company { get; init; }

    /// <summary>Villes dans l'ordre du scénario.</summary>
    public List<City> Cities { get; } = new();

    /// <summary>Identifiants de marchandises dans l'ordre du scénario. Ordre de parcours canonique.</summary>
    public List<string> CargoOrder { get; } = new();

    public List<RailLine> Lines { get; } = new();
    public List<Train> Trains { get; } = new();

    /// <summary>
    /// L'état financier : société, emprunts, actionnaires, concurrents. Vide tant
    /// qu'un scénario n'active pas le module, ce qui laisse les scénarios
    /// existants inchangés. Rempli par <see cref="IFinanceSolver.Initialize"/>.
    /// </summary>
    public FinanceState Finance { get; } = new();

    private readonly Dictionary<string, City> _citiesById = new();
    private readonly Dictionary<string, CargoDef> _cargosById = new();
    private readonly Dictionary<string, RecipeDef> _recipesById = new();

    public SimTick Tick { get; internal set; }

    public CargoDef Cargo(string id) => _cargosById[id];
    public City CityById(string id) => _citiesById[id];
    public RecipeDef Recipe(string id) => _recipesById[id];

    /// <summary>Les marchés d'une ville, dans l'ordre canonique des marchandises.</summary>
    public IEnumerable<Market> MarketsOf(City city)
    {
        foreach (string cargoId in CargoOrder)
            yield return city.Markets[cargoId];
    }

    internal void Register(CargoDef cargo)
    {
        _cargosById[cargo.Id] = cargo;
        CargoOrder.Add(cargo.Id);
    }

    internal void Register(RecipeDef recipe) => _recipesById[recipe.Id] = recipe;

    internal void Register(City city)
    {
        Cities.Add(city);
        _citiesById[city.Id] = city;
    }
}
