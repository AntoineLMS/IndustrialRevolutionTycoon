using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim.Events;

/// <summary>Origine d'un événement du journal.</summary>
public enum EventOrigin
{
    /// <summary>Daté et sourcé dans docs/SOURCES.md.</summary>
    Historical,

    /// <summary>Inspiré d'un fait réel dont l'effet n'est pas documenté.</summary>
    Inspired,

    /// <summary>Tiré du catalogue aléatoire.</summary>
    Random,
}

/// <summary>Un couple (ville, marchandise, levier) touché par un événement, avec son multiplicateur au plus fort.</summary>
public sealed record EventTarget(string CityId, string Cargo, string On, double PeakFactor);

/// <summary>
/// Une entrée du journal : un événement <em>déclenché</em>, jamais un événement à
/// venir.
/// <para>
/// Le journal est une information publique. Un joueur doit pouvoir lire « grève à
/// Coalburg depuis le 10 janvier, jusqu'au 21 juin, production réduite de
/// moitié », et un futur concurrent IA a le droit d'en lire autant — pas
/// davantage : il ne voit ni les événements historiques qui n'ont pas encore
/// commencé, ni les tirages à venir. Le contrat du module <c>ai</c> interdit
/// l'accès à une information que le joueur n'a pas ; ce journal est la frontière.
/// </para>
/// <para>
/// La fin est connue dès le début : un événement annonce sa durée. C'est un choix
/// de lisibilité — une grève dont personne ne sait quand elle finira est plus
/// réaliste, mais une décision de transport se prend sur un horizon, et c'est cet
/// horizon qu'on veut rendre jouable. Voir docs/FINDINGS.md, décisions ouvertes.
/// </para>
/// </summary>
public sealed class EventRecord
{
    /// <summary>Identifiant d'occurrence, unique dans la partie : l'identifiant de définition, suivi d'un numéro pour un type aléatoire.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Identifiant de la définition, historique ou type aléatoire.</summary>
    public required string DefinitionId { get; init; }

    public required string Name { get; init; }
    public required EventOrigin Origin { get; init; }

    /// <summary>Premier tick où l'événement agit.</summary>
    public required int StartTick { get; init; }

    /// <summary>Dernier tick où il agit, inclus.</summary>
    public required int EndTick { get; init; }

    public required int RampTicks { get; init; }

    /// <summary>Renvoi vers la source, pour un événement historique ou inspiré.</summary>
    public string Source { get; init; } = "";

    public required IReadOnlyList<EventTarget> Targets { get; init; }

    public int DurationTicks => EndTick - StartTick + 1;

    public bool IsActiveAt(int tick) => tick >= StartTick && tick <= EndTick;

    /// <summary>
    /// Enveloppe de l'événement au tick donné, dans [0, 1] : montée linéaire,
    /// plateau, descente linéaire. 0 hors de la fenêtre d'activité.
    /// <para>
    /// La montée ne commence pas à zéro : au premier tick l'enveloppe vaut
    /// 1/(montée+1), au dernier de même. Sans cela le premier et le dernier tick
    /// d'un événement ne changeraient rien, et sa durée affichée mentirait de deux
    /// ticks.
    /// </para>
    /// </summary>
    public double Envelope(int tick)
    {
        if (!IsActiveAt(tick)) return 0.0;
        if (RampTicks <= 0) return 1.0;

        double steps = RampTicks + 1.0;
        double rising = (tick - StartTick + 1) / steps;
        double falling = (EndTick - tick + 1) / steps;
        return Math.Min(1.0, Math.Min(rising, falling));
    }

    /// <summary>Multiplicateur d'une cible au tick donné : 1 hors de l'événement, le plein effet sur le plateau.</summary>
    public double FactorAt(EventTarget target, int tick)
        => 1.0 + (target.PeakFactor - 1.0) * Envelope(tick);
}

/// <summary>
/// L'état du module events. Vide et inactif tant qu'un scénario ne l'active pas,
/// ce qui laisse les scénarios existants inchangés.
/// </summary>
public sealed class EventsState
{
    public bool Enabled { get; internal set; }
    public EventsDef Def { get; internal set; } = new();

    /// <summary>
    /// Aléa du module, sur une séquence qui lui est propre. Partager le flux de
    /// <see cref="WorldState.Rng"/> ou de la finance ferait qu'activer les
    /// événements décalerait les tirages des autres modules, et changerait leurs
    /// traces sans qu'aucune de leurs règles n'ait bougé.
    /// </summary>
    public DeterministicRandom? Rng { get; internal set; }

    private readonly List<EventRecord> _journal = new();
    private readonly List<EventRecord> _active = new();

    /// <summary>Tous les événements déclenchés depuis le début de la partie, dans l'ordre de déclenchement.</summary>
    public IReadOnlyList<EventRecord> Journal => _journal;

    /// <summary>Les événements qui agissent au tick courant, dans l'ordre de déclenchement.</summary>
    public IReadOnlyList<EventRecord> Active => _active;

    internal void Open(EventRecord record)
    {
        _journal.Add(record);
        _active.Add(record);
    }

    /// <summary>Retire les événements dont le dernier tick est passé.</summary>
    internal void Expire(int tick) => _active.RemoveAll(e => e.EndTick < tick);

    /// <summary>Vrai si une occurrence de cette définition agit déjà sur cette ville.</summary>
    internal bool IsRunning(string definitionId, string cityId)
    {
        foreach (var record in _active)
        {
            if (record.DefinitionId != definitionId) continue;
            foreach (var target in record.Targets)
                if (target.CityId == cityId) return true;
        }
        return false;
    }

    /// <summary>
    /// Résumé du journal, pour l'empreinte des traces de référence. Chaîne vide
    /// quand le module est inactif : l'empreinte des scénarios sans événements
    /// reste celle qu'elle était avant l'arrivée du module.
    /// </summary>
    public string Summary()
    {
        if (!Enabled) return "";
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return string.Join(";", _journal.Select(e =>
            $"{e.InstanceId}@{e.StartTick}-{e.EndTick}:" +
            string.Join(",", e.Targets.Select(t =>
                $"{t.CityId}/{t.Cargo}/{t.On}x{t.PeakFactor.ToString("R", ci)}"))));
    }
}

/// <summary>Les deux leviers d'un effet, et leur lecture dans les données.</summary>
public static class EventLever
{
    public const string Production = "production";
    public const string Demand = "demand";

    /// <summary>Taux nominal du levier sur ce marché : une ville sans taux nominal n'offre aucune prise à l'effet.</summary>
    public static double Nominal(Market market, string on)
        => on == Production ? market.NominalProductionRate : market.NominalDemandRate;
}
