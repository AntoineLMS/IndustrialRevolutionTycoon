using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Events;

namespace RailTycoon.Sim.Cycle;

/// <summary>
/// Contrat du module cycle. Il occupe la phase 0c d'un tick, juste après les
/// événements (0b) et avant la production (1) : il lit les événements ouverts
/// aujourd'hui, fait avancer la conjoncture, et publie les conditions du jour —
/// l'ajustement des taux et le facteur du multiple de valorisation, que la finance
/// lit en phase 6, et le multiplicateur de demande, qu'il pose sur chaque marché
/// (<see cref="Market.CycleDemandFactor"/>) pour le solveur économique.
/// <para>
/// Règles que toute implémentation doit respecter :
/// </para>
/// <list type="number">
///   <item>Ne toucher à aucun stock, aucun prix, aucun argent. Le module écrit des
///   conditions, et la matière continue de passer par <see cref="Market.Produce"/>
///   et <see cref="Market.Consume"/> ; l'argent, par le grand livre de la
///   finance.</item>
///   <item>Inactif, ne rien écrire du tout : ni tirage, ni journal, ni
///   multiplicateur.</item>
///   <item>Tirer son aléa sur sa propre séquence, jamais sur celle du monde, de la
///   finance ou des événements.</item>
///   <item>Ne lire des événements que leur journal public, et ne rien y écrire. Le
///   couplage passe par un attribut de données (<see cref="EventCycleEffectDef"/>),
///   jamais par un cas particulier dans le code.</item>
///   <item>Rester exogène : aucune règle ne lit l'activité de l'économie ou de la
///   compagnie pour décider de la phase. Un cycle mû par l'économie elle-même a été
///   écarté (docs/VISION.md, « Le cycle économique ») : une telle boucle diverge ou
///   oscille facilement.</item>
/// </list>
/// </summary>
public interface ICycleSolver
{
    string Name { get; }
    void Initialize(WorldState world);
    void Step(WorldState world, SimTick tick);
}

/// <summary>
/// Implémentation de référence : des phases qui se succèdent en boucle, aux durées
/// tirées dans leurs bornes, que les événements peuvent forcer ou pousser.
/// <para>
/// <b>Un tirage par phase, et un seul.</b> La durée d'une phase est tirée à son
/// ouverture — qu'elle vienne de l'échéance de la précédente ou d'une bascule
/// forcée —, et nulle part ailleurs. La k-ième phase reçoit donc le k-ième nombre
/// de la séquence, quoi qu'aient fait les événements : deux réglages comparés
/// jouent le même calendrier de tirages, à la variable étudiée près. C'est la même
/// discipline que les quatre tirages quotidiens du module events.
/// </para>
/// </summary>
public sealed class ReferenceCycleSolver : ICycleSolver
{
    public string Name => "reference";

    /// <summary>Position dans le journal des événements : tout ce qui est au-delà est neuf.</summary>
    private int _eventCursor;

    /// <summary>
    /// Occurrence dont la poussée a fait échoir la phase aujourd'hui, pour que le
    /// journal nomme la cause d'une bascule. Vide si la phase échoit d'elle-même.
    /// </summary>
    private string _endedBy = "";

    /// <summary>Conditions au moment du dernier changement de phase : le point de départ de la transition.</summary>
    private decimal _fromRate;
    private decimal _fromMultiple = 1m;
    private double _fromDemand = 1.0;
    private decimal _fromContribution = 1m;
    private decimal _fromPatience = 1m;

    public void Initialize(WorldState world)
    {
        var def = world.Def.Cycle;
        var state = world.Cycle;
        state.Def = def;
        _eventCursor = 0;

        if (!def.Enabled)
        {
            state.Enabled = false;
            return;
        }

        Validate(world, def);

        state.Enabled = true;
        state.Rng = new DeterministicRandom(world.Def.Seed, def.RandomSequence);

        // La phase d'ouverture agit en plein dès le tick 0 : il n'y a pas de phase
        // précédente dont on viendrait. Aucun multiplicateur de demande n'est posé
        // avant le premier tick — les prix d'ouverture restent ceux du scénario nu,
        // comme avec les événements.
        var initial = PhaseById(def, def.InitialPhase)!;
        Open(state, initial, 0, CycleCause.Opening, "", "");
        Publish(state, initial);
        CaptureFrom(state);
    }

    public void Step(WorldState world, SimTick tick)
    {
        var state = world.Cycle;
        if (!state.Enabled) return;

        int now = tick.Index;
        _endedBy = "";

        // 1 — les événements ouverts depuis hier, dans l'ordre du journal
        // (historiques puis aléatoires, dans l'ordre des données). Une bascule
        // forcée s'applique tout de suite ; une poussée déplace la fin de la phase
        // en cours, qui peut alors échoir aujourd'hui même.
        var journal = world.Events.Journal;
        for (; _eventCursor < journal.Count; _eventCursor++)
        {
            var record = journal[_eventCursor];
            if (record.Cycle is not { } effect) continue;

            if (!string.IsNullOrWhiteSpace(effect.ForcePhase))
                Force(state, PhaseById(state.Def, effect.ForcePhase)!, now, record.InstanceId);
            else if (effect.PushTicks != 0)
                Push(state, now, effect.PushTicks, record.InstanceId);
        }

        // 2 — échéance. Une phase couvre [début, fin] ; le lendemain de sa fin, la
        // suivante s'ouvre. Une poussée qui a ramené la fin avant aujourd'hui fait
        // basculer aujourd'hui, et le journal en nomme la cause.
        if (state.PhaseEndTick < now)
        {
            var next = NextPhase(state.Def, state.Phase!);
            Open(state, next, now, _endedBy.Length > 0 ? CycleCause.Pushed : CycleCause.Elapsed,
                _endedBy, state.Phase!.Id);
        }

        // 3 — conditions du jour, transition comprise, puis publication sur les marchés.
        Publish(state, state.Phase!, now);
        foreach (var city in world.Cities)
            foreach (var market in world.MarketsOf(city))
                market.CycleDemandFactor = state.DemandFactor;
    }

    /// <summary>Ouvre une phase et tire sa durée : le seul tirage du module.</summary>
    private void Open(CycleState state, CyclePhaseDef phase, int start, CycleCause cause, string eventId, string previous)
    {
        if (state.Phase is not null) CaptureFrom(state);
        int duration = DrawDuration(state, phase);
        state.Phase = phase;
        state.PhaseStartTick = start;
        state.PhaseEndTick = start + duration - 1;
        state.Log(new CycleRecord
        {
            Tick = start,
            Kind = CycleRecordKind.Phase,
            PhaseId = phase.Id,
            PreviousPhaseId = previous,
            Cause = cause,
            EventInstanceId = eventId,
        });
    }

    /// <summary>
    /// Bascule forcée par un événement. Déjà dans la phase visée, la conjoncture y
    /// reste, et la phase est prolongée si le nouveau tirage finit plus tard : une
    /// panique en pleine crise l'aggrave, elle ne la fait pas recommencer à zéro.
    /// Le tirage a lieu dans les deux cas, pour que la séquence ne dépende pas de
    /// la phase où tombe l'événement.
    /// </summary>
    private void Force(CycleState state, CyclePhaseDef phase, int now, string eventId)
    {
        if (state.Phase == phase)
        {
            int duration = DrawDuration(state, phase);
            int end = now + duration - 1;
            int shift = Math.Max(0, end - state.PhaseEndTick);
            state.PhaseEndTick += shift;
            state.Log(new CycleRecord
            {
                Tick = now,
                Kind = CycleRecordKind.Shift,
                PhaseId = phase.Id,
                Cause = CycleCause.Forced,
                EventInstanceId = eventId,
                ShiftTicks = shift,
            });
            return;
        }
        Open(state, phase, now, CycleCause.Forced, eventId, state.Phase!.Id);
    }

    /// <summary>
    /// Poussée d'un événement : une bonne nouvelle (positive) allonge une phase
    /// favorable et abrège une défavorable, une mauvaise fait l'inverse. La fin ne
    /// recule jamais avant le début de la phase : une phase dure au moins un jour.
    /// </summary>
    private void Push(CycleState state, int now, int pushTicks, string eventId)
    {
        var phase = state.Phase!;
        int shift = phase.Favorable ? pushTicks : -pushTicks;
        int end = Math.Max(state.PhaseStartTick, state.PhaseEndTick + shift);
        shift = end - state.PhaseEndTick;
        if (state.PhaseEndTick >= now && end < now) _endedBy = eventId;
        else if (end >= now) _endedBy = "";
        state.PhaseEndTick = end;
        state.Log(new CycleRecord
        {
            Tick = now,
            Kind = CycleRecordKind.Shift,
            PhaseId = phase.Id,
            Cause = CycleCause.Pushed,
            EventInstanceId = eventId,
            ShiftTicks = shift,
        });
    }

    private static int DrawDuration(CycleState state, CyclePhaseDef phase)
    {
        double u = state.Rng!.NextDouble();
        int span = phase.MaxTicks - phase.MinTicks + 1;
        return phase.MinTicks + Math.Min(span - 1, (int)(u * span));
    }

    /// <summary>Mémorise les conditions publiées : une nouvelle phase part d'où en était la conjoncture, pas d'une valeur nominale.</summary>
    private void CaptureFrom(CycleState state)
    {
        _fromRate = state.RateAdjustmentPercent;
        _fromMultiple = state.EarningsMultipleFactor;
        _fromDemand = state.DemandFactor;
        _fromContribution = state.InvestorContributionFactor;
        _fromPatience = state.InvestorPatienceFactor;
    }

    /// <summary>Conditions pleines d'une phase, sans transition : celles de l'ouverture.</summary>
    private static void Publish(CycleState state, CyclePhaseDef phase)
    {
        state.RateAdjustmentPercent = phase.RateAdjustmentPercent;
        state.EarningsMultipleFactor = phase.EarningsMultipleFactor;
        state.DemandFactor = phase.DemandFactor;
        state.InvestorContributionFactor = phase.InvestorContributionFactor;
        state.InvestorPatienceFactor = phase.InvestorPatienceFactor;
    }

    /// <summary>
    /// Conditions du jour : de celles du dernier changement de phase vers celles de
    /// la phase en cours, en ligne droite sur <c>transitionTicks</c>. Le premier jour
    /// de la phase fait déjà un pas — 1/(transition+1) du chemin —, comme la montée
    /// d'un événement. Les grandeurs financières sont arrondies : l'ajustement de taux
    /// au point de base, les facteurs au dix-millième.
    /// </summary>
    private void Publish(CycleState state, CyclePhaseDef phase, int now)
    {
        int transition = state.Def.TransitionTicks;
        int elapsed = now - state.PhaseStartTick + 1;
        if (transition <= 0 || elapsed > transition)
        {
            Publish(state, phase);
            return;
        }

        decimal w = (decimal)elapsed / (transition + 1);
        double wd = (double)elapsed / (transition + 1);
        state.RateAdjustmentPercent = Math.Round(_fromRate + (phase.RateAdjustmentPercent - _fromRate) * w, 2, MidpointRounding.ToEven);
        state.EarningsMultipleFactor = Math.Round(_fromMultiple + (phase.EarningsMultipleFactor - _fromMultiple) * w, 4, MidpointRounding.ToEven);
        state.DemandFactor = _fromDemand + (phase.DemandFactor - _fromDemand) * wd;
        state.InvestorContributionFactor = Math.Round(_fromContribution + (phase.InvestorContributionFactor - _fromContribution) * w, 4, MidpointRounding.ToEven);
        state.InvestorPatienceFactor = Math.Round(_fromPatience + (phase.InvestorPatienceFactor - _fromPatience) * w, 4, MidpointRounding.ToEven);
    }

    private static CyclePhaseDef? PhaseById(CycleDef def, string id)
    {
        foreach (var phase in def.Phases)
            if (phase.Id == id) return phase;
        return null;
    }

    private static CyclePhaseDef NextPhase(CycleDef def, CyclePhaseDef phase)
        => def.Phases[(def.Phases.IndexOf(phase) + 1) % def.Phases.Count];

    /// <summary>
    /// Validation au chargement. On échoue fort et tôt : une phase aux bornes
    /// inversées ou un événement qui force une phase inconnue tournerait sans erreur
    /// et ne ferait rien, ce qui est la pire façon d'échouer.
    /// </summary>
    private static void Validate(WorldState world, CycleDef def)
    {
        var s = world.Def;
        if (def.RandomSequence == 1)
            throw new InvalidDataException("cycle.randomSequence ne peut pas valoir 1 : c'est la séquence du monde.");
        if (s.Finance.Enabled && def.RandomSequence == s.Finance.RandomSequence)
            throw new InvalidDataException(
                "cycle.randomSequence doit différer de finance.randomSequence : les deux modules tireraient le même flux.");
        if (s.Events.Enabled && def.RandomSequence == s.Events.RandomSequence)
            throw new InvalidDataException(
                "cycle.randomSequence doit différer de events.randomSequence : les deux modules tireraient le même flux.");

        if (def.Phases.Count < 2)
            throw new InvalidDataException("cycle : il faut au moins deux phases pour faire un cycle.");
        if (def.TransitionTicks < 0)
            throw new InvalidDataException("cycle.transitionTicks ne peut pas être négatif.");

        var ids = new HashSet<string>();
        foreach (var p in def.Phases)
        {
            if (string.IsNullOrWhiteSpace(p.Id))
                throw new InvalidDataException("cycle : une phase n'a pas d'identifiant.");
            if (!ids.Add(p.Id))
                throw new InvalidDataException($"cycle : phase en double, '{p.Id}'.");
            if (p.MinTicks < 1 || p.MaxTicks < p.MinTicks)
                throw new InvalidDataException($"cycle : phase '{p.Id}', il faut 1 ≤ minTicks ≤ maxTicks.");
            if (p.EarningsMultipleFactor <= 0m)
                throw new InvalidDataException($"cycle : phase '{p.Id}', earningsMultipleFactor doit être strictement positif.");
            if (!(p.DemandFactor > 0) || double.IsInfinity(p.DemandFactor))
                throw new InvalidDataException($"cycle : phase '{p.Id}', demandFactor doit être strictement positif.");
            if (p.InvestorContributionFactor < 0m || p.InvestorPatienceFactor < 0m)
                throw new InvalidDataException($"cycle : phase '{p.Id}', facteurs des investisseurs négatifs.");
        }

        if (PhaseById(def, def.InitialPhase) is null)
            throw new InvalidDataException($"cycle.initialPhase vaut '{def.InitialPhase}', qui n'est pas une phase déclarée.");

        var risk = def.Credit.RiskPremium;
        if (def.Credit.MinRatePercent < 0m || risk.LeverageSlopePercent < 0m || risk.LossSlopePercent < 0m
            || risk.MaxPercent < 0m || risk.LeverageThreshold < 0m)
            throw new InvalidDataException("cycle.credit : plancher, pentes, seuil et plafond de la prime doivent être positifs ou nuls.");

        // Un événement qui force une phase inconnue ne ferait rien, sans le dire.
        void CheckEvent(string id, EventCycleEffectDef? effect)
        {
            if (effect is null || string.IsNullOrWhiteSpace(effect.ForcePhase)) return;
            if (PhaseById(def, effect.ForcePhase) is null)
                throw new InvalidDataException(
                    $"L'événement '{id}' force la phase '{effect.ForcePhase}', qui n'est pas une phase du bloc cycle.");
        }

        if (s.Events.Enabled)
        {
            foreach (var h in s.Events.Historical) CheckEvent(h.Id, h.Cycle);
            foreach (var r in s.Events.Random) CheckEvent(r.Id, r.Cycle);
        }
    }
}
