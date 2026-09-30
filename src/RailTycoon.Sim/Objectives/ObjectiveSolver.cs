using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Objectives;

/// <summary>
/// Contrat du module objectives. Il occupe la phase 7 d'un tick, la dernière, après
/// la finance : il lit ce que la journée a produit — fortune du magnat, carnet de
/// route de la compagnie — et dit, pour chaque palier de chaque objectif du
/// scénario, s'il est atteint, en cours ou manqué.
/// <para>
/// Règles que toute implémentation doit respecter :
/// </para>
/// <list type="number">
///   <item><b>Observer, jamais agir.</b> Ne toucher à aucun stock, aucun prix, aucun
///   argent, aucune décision d'aucun module, et ne tirer aucun aléa. Une partie avec
///   objectifs et la même sans doivent être identiques au bit près ; un test le
///   vérifie sous les deux solveurs.</item>
///   <item>Inactif, ne rien écrire : ni progression, ni journal.</item>
///   <item>Un palier atteint ou manqué l'est pour de bon. La mesure peut redescendre
///   sous la cible — une fortune qui fond, une ville qui réexpédie ce qu'on lui a
///   livré — : le jour où elle l'a franchie reste acquis, comme une médaille.</item>
///   <item>Ne lire que ce qu'un joueur voit : la fortune du magnat, le carnet de route
///   de sa compagnie, le calendrier. Rien d'interne à un autre module.</item>
/// </list>
/// </summary>
public interface IObjectiveSolver
{
    string Name { get; }
    void Initialize(WorldState world);
    void Step(WorldState world, SimTick tick);
}

/// <summary>
/// Implémentation de référence : trois mesures, lues au soir de chaque tick.
/// <list type="bullet">
///   <item><b>Fortune</b> — <c>Tycoon.NetWorth</c>, au tick courant ou en moyenne
///   glissante sur <c>averageTicks</c> soirs, selon les données.</item>
///   <item><b>Livraisons</b> — ce que le rail a laissé dans une ville, vendu moins
///   racheté (<see cref="Transport.FreightLedger.NetDelivered"/>), pour une ville, ou
///   additionné sur toutes.</item>
///   <item><b>Liaison</b> — un même train, sur une même ligne, s'est arrêté dans les
///   deux villes.</item>
/// </list>
/// Les raisons de ces trois définitions sont dans docs/CONTRACTS.md, contrat
/// <c>objectives</c>.
/// </summary>
public sealed class ReferenceObjectiveSolver : IObjectiveSolver
{
    public string Name => "reference";

    /// <summary>Fenêtre glissante de fortune, par objectif (nulle pour les autres sortes).</summary>
    private readonly List<Queue<decimal>?> _windows = new();
    private readonly List<decimal> _windowSums = new();

    public void Initialize(WorldState world)
    {
        var def = world.Def.Objectives;
        var state = world.Objectives;
        state.Reset();
        state.Def = def;
        _windows.Clear();
        _windowSums.Clear();

        if (!def.Enabled) return;

        int startYear = Validate(world, def);

        state.Enabled = true;
        state.StartYear = startYear;
        foreach (var goal in def.Goals)
        {
            state.Add(new ObjectiveProgress
            {
                Def = goal,
                Tiers = goal.Tiers
                    .Select(t => new TierProgress { Def = t, DeadlineTick = DeadlineTickOf(t.Deadline, startYear) })
                    .ToList(),
            });
            _windows.Add(goal.Kind == ObjectiveKinds.Fortune ? new Queue<decimal>() : null);
            _windowSums.Add(0m);
        }
    }

    public void Step(WorldState world, SimTick tick)
    {
        var state = world.Objectives;
        if (!state.Enabled) return;

        int now = tick.Index;
        for (int i = 0; i < state.Goals.Count; i++)
        {
            var goal = state.Goals[i];
            var (measure, reached, detail) = goal.Def.Kind switch
            {
                ObjectiveKinds.Fortune => Fortune(world, i),
                ObjectiveKinds.Deliveries => Deliveries(world, goal.Def),
                _ => Connection(world, goal.Def),
            };
            goal.Measure = measure;
            if (detail.Length > 0 && goal.Detail.Length == 0) goal.Detail = detail;

            foreach (var tier in goal.Tiers)
            {
                if (tier.Status != TierStatus.InProgress) continue;

                // L'atteinte d'abord : l'échéance est incluse, un palier atteint le
                // jour même de son échéance est atteint.
                TierStatus outcome;
                if (reached(tier.Def.Target)) outcome = TierStatus.Attained;
                else if (tier.DeadlineTick is int deadline && now >= deadline) outcome = TierStatus.Missed;
                else continue;

                tier.Status = outcome;
                tier.Tick = now;
                state.Log(new ObjectiveRecord
                {
                    Tick = now,
                    ObjectiveId = goal.Def.Id,
                    TierId = tier.Def.Id,
                    Outcome = outcome,
                    Measure = measure,
                    Target = tier.Def.Target,
                    Detail = outcome == TierStatus.Attained ? detail : "",
                });
            }
        }
    }

    /// <summary>
    /// La fortune du magnat, moyenne glissante comprise, comparée en <c>decimal</c> :
    /// c'est une grandeur de la finance, et un seuil de 500 000 doit se franchir au
    /// centime, pas à l'arrondi d'un double près.
    /// </summary>
    private (double?, Func<decimal?, bool>, string) Fortune(WorldState world, int index)
    {
        var finance = world.Finance;
        decimal worth = finance.Magnate!.NetWorth(finance);
        int window = world.Objectives.Goals[index].Def.AverageTicks!.Value;

        var values = _windows[index]!;
        values.Enqueue(worth);
        _windowSums[index] += worth;
        if (values.Count > window) _windowSums[index] -= values.Dequeue();

        if (values.Count < window) return (null, _ => false, "");
        decimal average = window == 1 ? worth : _windowSums[index] / window;
        return ((double)average, target => average >= target!.Value, "");
    }

    private static (double?, Func<decimal?, bool>, string) Deliveries(WorldState world, ObjectiveDef def)
    {
        var ledger = world.Company.Freight;
        double delivered = 0;
        if (def.City.Length > 0)
            delivered = ledger.NetDelivered(def.Cargo, def.City);
        else
            foreach (var city in world.Cities) // ordre des données : la somme se fait toujours dans le même ordre
                delivered += ledger.NetDelivered(def.Cargo, city.Id);
        return (delivered, target => delivered >= (double)target!.Value, "");
    }

    private static (double?, Func<decimal?, bool>, string) Connection(WorldState world, ObjectiveDef def)
    {
        var ledger = world.Company.Freight;
        foreach (var train in world.Trains)
            if (ledger.HasVisited(train, def.Cities[0]) && ledger.HasVisited(train, def.Cities[1]))
                return (1.0, _ => true, train.Id);
        return (0.0, _ => false, "");
    }

    /// <summary>Le dernier tick admis d'une échéance, ou nul s'il n'y en a pas.</summary>
    public static int? DeadlineTickOf(DeadlineDef? deadline, int startYear)
    {
        if (deadline is null) return null;
        if (deadline.Tick is int tick) return tick;
        int monthDays = SimTick.TicksPerYear / 12;
        return (deadline.Year!.Value - startYear) * SimTick.TicksPerYear
             + (deadline.Month!.Value - 1) * monthDays
             + (deadline.Day!.Value - 1);
    }

    /// <summary>
    /// Validation au chargement, et elle est stricte : un objectif sur une
    /// marchandise ou une ville inconnue, ou une fortune sans magnat, ne serait
    /// jamais atteint et ne dirait jamais pourquoi — la pire façon d'échouer. Renvoie
    /// l'année du calendrier retenue.
    /// </summary>
    private static int Validate(WorldState world, ObjectivesDef def)
    {
        var s = world.Def;
        if (def.Goals.Count == 0)
            throw new InvalidDataException("objectives : le bloc est actif mais ne déclare aucun objectif.");
        if (def.StartYear < 0)
            throw new InvalidDataException("objectives.startYear ne peut pas être négatif.");
        if (def.StartYear > 0 && s.Events.StartYear > 0 && def.StartYear != s.Events.StartYear)
            throw new InvalidDataException(
                $"objectives.startYear ({def.StartYear}) diffère de events.startYear ({s.Events.StartYear}) : " +
                "un scénario n'a qu'un calendrier.");
        int startYear = def.StartYear > 0 ? def.StartYear : s.Events.StartYear;

        var cities = new HashSet<string>(world.Cities.Select(c => c.Id));
        var cargos = new HashSet<string>(world.CargoOrder);
        var ids = new HashSet<string>();

        foreach (var goal in def.Goals)
        {
            if (string.IsNullOrWhiteSpace(goal.Id))
                throw new InvalidDataException("objectives : un objectif n'a pas d'identifiant.");
            if (!ids.Add(goal.Id))
                throw new InvalidDataException($"objectives : objectif en double, '{goal.Id}'.");
            string where = $"objectives : objectif '{goal.Id}'";

            bool fortune = goal.Kind == ObjectiveKinds.Fortune;
            bool deliveries = goal.Kind == ObjectiveKinds.Deliveries;
            bool connect = goal.Kind == ObjectiveKinds.Connect;
            if (!fortune && !deliveries && !connect)
                throw new InvalidDataException(
                    $"{where}, sorte '{goal.Kind}' inconnue (fortune, deliveries, connect).");

            // Chaque champ n'a de sens que pour une sorte : un champ posé ailleurs est
            // une confusion dans les données, pas un réglage inerte.
            if (!fortune && goal.AverageTicks is not null)
                throw new InvalidDataException($"{where} : averageTicks ne vaut que pour une fortune.");
            if (!deliveries && (goal.Cargo.Length > 0 || goal.City.Length > 0))
                throw new InvalidDataException($"{where} : cargo et city ne valent que pour des livraisons.");
            if (!connect && goal.Cities.Count > 0)
                throw new InvalidDataException($"{where} : cities ne vaut que pour une liaison.");

            if (fortune)
            {
                // Décision : pas de repli sur la trésorerie de la compagnie. La vision
                // distingue les deux fortunes (docs/VISION.md, « Le joueur est l'homme
                // d'affaires ») ; sans module finance il n'y a pas d'homme d'affaires,
                // et mesurer sa caisse de société sous le nom de fortune serait
                // exactement la confusion qu'elle interdit.
                if (!s.Finance.Enabled)
                    throw new InvalidDataException(
                        $"{where} : une fortune personnelle se lit sur le magnat, qui n'existe qu'avec le module " +
                        "finance. Sans lui, la simulation n'a qu'une trésorerie de compagnie, qui n'est pas la " +
                        "fortune du joueur.");
                if (goal.AverageTicks is not int window || window < 1)
                    throw new InvalidDataException(
                        $"{where} : averageTicks est obligatoire et vaut au moins 1 (1 = fortune du jour, " +
                        "N = moyenne des N derniers jours) ; voir docs/FINDINGS.md, « Les objectifs ».");
            }
            else if (deliveries)
            {
                if (!cargos.Contains(goal.Cargo))
                    throw new InvalidDataException($"{where} : marchandise inconnue '{goal.Cargo}'.");
                if (goal.City.Length > 0 && !cities.Contains(goal.City))
                    throw new InvalidDataException($"{where} : ville inconnue '{goal.City}'.");
            }
            else
            {
                if (goal.Cities.Count != 2)
                    throw new InvalidDataException($"{where} : une liaison relie exactement deux villes.");
                foreach (string city in goal.Cities)
                    if (!cities.Contains(city))
                        throw new InvalidDataException($"{where} : ville inconnue '{city}'.");
                if (goal.Cities[0] == goal.Cities[1])
                    throw new InvalidDataException($"{where} : une ville ne se relie pas à elle-même.");
            }

            if (goal.Tiers.Count == 0)
                throw new InvalidDataException($"{where} : il faut au moins un palier.");
            var tierIds = new HashSet<string>();
            foreach (var tier in goal.Tiers)
            {
                if (string.IsNullOrWhiteSpace(tier.Id))
                    throw new InvalidDataException($"{where} : un palier n'a pas d'identifiant.");
                if (!tierIds.Add(tier.Id))
                    throw new InvalidDataException($"{where} : palier en double, '{tier.Id}'.");
                string tierWhere = $"{where}, palier '{tier.Id}'";

                if (connect && tier.Target is not null)
                    throw new InvalidDataException($"{tierWhere} : une liaison n'a pas de cible chiffrée.");
                if (!connect && (tier.Target is not decimal target || target <= 0m))
                    throw new InvalidDataException($"{tierWhere} : il faut une cible strictement positive.");

                if (tier.Deadline is not { } d) continue;
                bool byTick = d.Tick is not null;
                bool byDate = d.Year is not null || d.Month is not null || d.Day is not null;
                if (byTick == byDate)
                    throw new InvalidDataException($"{tierWhere} : une échéance se donne par tick ou par date, pas les deux ni aucun.");
                if (byDate)
                {
                    if (d.Year is null || d.Month is null || d.Day is null)
                        throw new InvalidDataException($"{tierWhere} : une date d'échéance a une année, un mois et un jour.");
                    if (startYear <= 0)
                        throw new InvalidDataException(
                            $"{tierWhere} : échéance datée, mais le scénario n'a pas de calendrier " +
                            "(objectives.startYear ou events.startYear).");
                    if (d.Month < 1 || d.Month > 12 || d.Day < 1 || d.Day > SimTick.TicksPerYear / 12)
                        throw new InvalidDataException(
                            $"{tierWhere} : {d.Day}/{d.Month} n'existe pas dans le calendrier de jeu (12 mois de 30 jours).");
                }
                // Le premier soir évalué est celui du tick 1 : une échéance antérieure
                // serait manquée avant d'avoir pu être tenue.
                if (DeadlineTickOf(d, startYear) < 1)
                    throw new InvalidDataException($"{tierWhere} : l'échéance tombe avant le premier jour de la partie.");
            }
        }
        return startYear;
    }
}
