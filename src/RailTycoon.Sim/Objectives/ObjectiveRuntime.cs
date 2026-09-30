using System.Globalization;

namespace RailTycoon.Sim.Objectives;

/// <summary>État d'un palier.</summary>
public enum TierStatus
{
    /// <summary>Ni atteint, ni manqué : la mesure n'a pas franchi la cible, et l'échéance, s'il y en a une, n'est pas passée.</summary>
    InProgress,

    /// <summary>La mesure a franchi la cible au plus tard le jour de l'échéance. Définitif.</summary>
    Attained,

    /// <summary>L'échéance est passée sans que la mesure ait franchi la cible. Définitif.</summary>
    Missed,
}

/// <summary>
/// Une entrée du journal public des objectifs : un palier atteint ou manqué, le jour
/// où c'est arrivé, et la mesure ce jour-là.
/// <para>
/// Le journal ne dit que ce qui est arrivé. L'état des paliers en cours et la mesure
/// du jour se lisent dans <see cref="ObjectiveProgress"/> : c'est ce qu'un joueur
/// voit sur son tableau de bord, et un concurrent IA a le droit d'en lire autant — la
/// fortune d'un autre et ses livraisons sont publiques dans un journal financier.
/// </para>
/// </summary>
public sealed class ObjectiveRecord
{
    public required int Tick { get; init; }
    public required string ObjectiveId { get; init; }
    public required string TierId { get; init; }

    /// <summary><see cref="TierStatus.Attained"/> ou <see cref="TierStatus.Missed"/>.</summary>
    public required TierStatus Outcome { get; init; }

    /// <summary>
    /// La mesure ce jour-là : fortune (moyenne comprise), chargements livrés, ou 1
    /// pour une liaison faite et 0 sinon. Nulle si elle n'est pas encore lisible —
    /// une moyenne glissante qui n'a pas ses N soirs.
    /// </summary>
    public double? Measure { get; init; }

    /// <summary>Cible du palier ; nulle pour une liaison.</summary>
    public decimal? Target { get; init; }

    /// <summary>Pour une liaison atteinte : le train qui l'a faite. Vide sinon.</summary>
    public string Detail { get; init; } = "";
}

/// <summary>L'état d'un palier, tel que le tableau de bord l'affiche.</summary>
public sealed class TierProgress
{
    public required ObjectiveTierDef Def { get; init; }

    /// <summary>Dernier tick admis, échéance résolue ; nul sans échéance.</summary>
    public required int? DeadlineTick { get; init; }

    public TierStatus Status { get; internal set; } = TierStatus.InProgress;

    /// <summary>Jour où le palier a été atteint ou manqué ; −1 tant qu'il est en cours.</summary>
    public int Tick { get; internal set; } = -1;
}

/// <summary>Un objectif en cours de partie : sa mesure du jour et l'état de ses paliers.</summary>
public sealed class ObjectiveProgress
{
    public required ObjectiveDef Def { get; init; }
    public required IReadOnlyList<TierProgress> Tiers { get; init; }

    /// <summary>Mesure au soir du dernier tick évalué ; nulle tant qu'elle n'est pas lisible.</summary>
    public double? Measure { get; internal set; }

    /// <summary>Pour une liaison faite : le premier train qui l'a faite, dans l'ordre des trains.</summary>
    public string Detail { get; internal set; } = "";
}

/// <summary>
/// L'état du module objectives : la progression de chaque objectif et le journal
/// public. Inactif et vide tant qu'un scénario ne l'active pas.
/// </summary>
public sealed class ObjectivesState
{
    public bool Enabled { get; internal set; }
    public ObjectivesDef Def { get; internal set; } = new();

    /// <summary>
    /// Année du tick 0 pour lire une échéance en date : celle du bloc, ou à défaut
    /// celle du bloc events. 0 si le scénario n'a pas de calendrier.
    /// </summary>
    public int StartYear { get; internal set; }

    private readonly List<ObjectiveProgress> _goals = new();
    private readonly List<ObjectiveRecord> _journal = new();

    /// <summary>Les objectifs, dans l'ordre des données.</summary>
    public IReadOnlyList<ObjectiveProgress> Goals => _goals;

    /// <summary>Journal public : paliers atteints et manqués, dans l'ordre où c'est arrivé.</summary>
    public IReadOnlyList<ObjectiveRecord> Journal => _journal;

    internal void Add(ObjectiveProgress goal) => _goals.Add(goal);

    internal void Log(ObjectiveRecord record) => _journal.Add(record);

    internal void Reset()
    {
        _goals.Clear();
        _journal.Clear();
        Enabled = false;
        StartYear = 0;
    }

    /// <summary>
    /// Résumé du journal, lisible et stable, pour les tests qui figent le journal
    /// d'un scénario livré. Chaîne vide quand le module est inactif. Il n'entre pas
    /// dans les empreintes de <c>ReferenceTraceTests</c> : celles-ci restent la preuve
    /// que le module n'a rien déplacé.
    /// </summary>
    public string Summary()
    {
        if (!Enabled) return "";
        var ci = CultureInfo.InvariantCulture;
        return string.Join(";", _journal.Select(r =>
            $"{r.Tick.ToString(ci)}:{r.ObjectiveId}/{r.TierId}:" +
            (r.Outcome == TierStatus.Attained ? "atteint" : "manqué") +
            (r.Detail.Length > 0 ? ":" + r.Detail : "")));
    }
}
