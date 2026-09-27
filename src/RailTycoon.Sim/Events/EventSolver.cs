using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim.Events;

/// <summary>
/// Contrat du module events. Il occupe la phase 0b d'un tick, après la remise à
/// zéro de la télémétrie et avant la production primaire : il décide quels
/// événements agissent aujourd'hui et publie, sur chaque marché, les deux
/// multiplicateurs du jour (<see cref="Market.EventProductionFactor"/>,
/// <see cref="Market.EventDemandFactor"/>). Le solveur économique les lit en
/// composant ses taux du jour, quel qu'il soit.
/// <para>
/// Règles que toute implémentation doit respecter :
/// </para>
/// <list type="number">
///   <item>Ne toucher à aucun stock, aucun prix, aucun argent. Le module écrit deux
///   nombres par marché, et rien d'autre : la marchandise continue de naître et de
///   disparaître par <see cref="Market.Produce"/> et <see cref="Market.Consume"/>,
///   dans le solveur économique.</item>
///   <item>Inactif, ne rien écrire du tout. Les multiplicateurs restent à 1
///   exactement, ce qui laisse les taux — donc les traces — identiques au bit
///   près.</item>
///   <item>Tirer son aléa sur sa propre séquence de
///   <see cref="DeterministicRandom"/>, jamais sur celle du monde ou d'un autre
///   module.</item>
///   <item>Parcourir marchés et événements dans un ordre stable : villes dans
///   l'ordre du scénario, marchandises dans <see cref="WorldState.CargoOrder"/>,
///   événements dans l'ordre des données puis de déclenchement.</item>
/// </list>
/// </summary>
public interface IEventSolver
{
    string Name { get; }
    void Initialize(WorldState world);
    void Step(WorldState world, SimTick tick);
}

/// <summary>
/// Implémentation de référence : événements historiques à date fixe, et catalogue
/// d'événements aléatoires tirés chaque jour.
/// <para>
/// <b>Un nombre fixe de tirages par type et par jour.</b> Chaque type tire
/// quatre nombres par tick — déclenchement, cible, intensité, durée — qu'il se
/// déclenche ou non, et même hors de sa fenêtre de mois. Le flux ne dépend donc
/// que du nombre de types et du nombre de ticks, jamais de ce qui s'est passé. Ce
/// n'est pas une coquetterie, c'est ce qui rend les balayages de mesure lisibles :
/// changer les bornes d'intensité d'un type ne change <em>que</em> les
/// intensités, pas les dates ; et augmenter sa fréquence ajoute des occurrences
/// sans déplacer celles qui existaient, puisqu'un même tirage de déclenchement est
/// comparé à un seuil plus haut. Deux réglages comparés jouent donc le même
/// calendrier d'aléas, à la variable étudiée près.
/// </para>
/// </summary>
public sealed class ReferenceEventSolver : IEventSolver
{
    public string Name => "reference";

    /// <summary>Un événement historique, résolu en tick et en cibles au chargement.</summary>
    private sealed class Scheduled
    {
        public required HistoricalEventDef Def { get; init; }
        public required int StartTick { get; init; }
        public required List<EventTarget> Targets { get; init; }
        public bool Opened;
    }

    /// <summary>Un type aléatoire, avec ses villes admissibles résolues au chargement.</summary>
    private sealed class RandomType
    {
        public required RandomEventTypeDef Def { get; init; }
        public required List<City> Eligible { get; init; }
        public required double DailyProbability { get; init; }
        public int Occurrences;
    }

    private readonly List<Scheduled> _scheduled = new();
    private readonly List<RandomType> _random = new();

    public void Initialize(WorldState world)
    {
        var def = world.Def.Events;
        var state = world.Events;
        state.Def = def;
        _scheduled.Clear();
        _random.Clear();

        if (!def.Enabled)
        {
            state.Enabled = false;
            return;
        }

        Validate(world, def);

        state.Enabled = true;
        state.Rng = new DeterministicRandom(world.Def.Seed, def.RandomSequence);

        foreach (var h in def.Historical)
        {
            var cities = ResolveCities(world, h.Id, h.Cities, h.Effects);
            var targets = new List<EventTarget>();
            foreach (var city in cities)
                foreach (var effect in h.Effects)
                    if (EventLever.Nominal(city.Market(effect.Cargo), effect.On) > 0)
                        targets.Add(new EventTarget(city.Id, effect.Cargo, effect.On, effect.Factor));

            if (targets.Count == 0)
                throw new InvalidDataException(
                    $"L'événement '{h.Id}' n'a prise sur aucune ville : aucune ne produit ni ne consomme ce qu'il touche.");
            _scheduled.Add(new Scheduled { Def = h, StartTick = StartTickOf(def, h), Targets = targets });
        }

        foreach (var r in def.Random)
        {
            var eligible = ResolveCities(world, r.Id, r.Cities, r.Effects);
            if (eligible.Count == 0)
                throw new InvalidDataException(
                    $"Le type '{r.Id}' n'a prise sur aucune ville : aucune ne produit ni ne consomme ce qu'il touche.");
            int windowDays = r.Months.Count == 0
                ? SimTick.TicksPerYear
                : r.Months.Distinct().Count() * (SimTick.TicksPerYear / 12);
            _random.Add(new RandomType
            {
                Def = r,
                Eligible = eligible,
                DailyProbability = Math.Min(1.0, r.OccurrencesPerYear / windowDays),
            });
        }
    }

    public void Step(WorldState world, SimTick tick)
    {
        var state = world.Events;
        if (!state.Enabled) return;

        int now = tick.Index;
        state.Expire(now);

        // Historiques d'abord, dans l'ordre des données. « Pas encore ouvert et
        // déjà commencé » plutôt que « commence aujourd'hui » : un événement daté
        // du tick 0 — que Step ne voit jamais, la partie commençant au tick 1 — ne
        // doit pas disparaître en silence. Il s'ouvre au premier tick joué, avec
        // sa date d'origine, donc déjà en partie écoulé.
        foreach (var s in _scheduled)
        {
            if (s.Opened || s.StartTick > now) continue;
            s.Opened = true;

            int end = s.StartTick + s.Def.DurationTicks - 1;
            if (end < now) continue; // entièrement passé avant le premier tick joué

            state.Open(new EventRecord
            {
                InstanceId = s.Def.Id,
                DefinitionId = s.Def.Id,
                Name = s.Def.Name,
                Origin = s.Def.Basis == "historical" ? EventOrigin.Historical : EventOrigin.Inspired,
                StartTick = s.StartTick,
                EndTick = end,
                RampTicks = s.Def.RampTicks,
                Source = s.Def.Source,
                Targets = s.Targets,
            });
        }

        DrawRandom(world, state, tick);
        PublishFactors(world, state, now);
    }

    /// <summary>
    /// Tirage du jour, type par type. Quatre nombres par type, toujours : voir la
    /// remarque de classe sur la stabilité du calendrier d'aléas.
    /// </summary>
    private void DrawRandom(WorldState world, EventsState state, SimTick tick)
    {
        var rng = state.Rng!;
        int month = tick.DayOfYear / (SimTick.TicksPerYear / 12) + 1;

        foreach (var type in _random)
        {
            double trigger = rng.NextDouble();
            double pick = rng.NextDouble();
            double severity = rng.NextDouble();
            double length = rng.NextDouble();

            var def = type.Def;
            if (trigger >= type.DailyProbability) continue;
            if (def.Months.Count > 0 && !def.Months.Contains(month)) continue;

            List<City> cities = def.Scope == "all"
                ? type.Eligible
                : [type.Eligible[Math.Min(type.Eligible.Count - 1, (int)(pick * type.Eligible.Count))]];

            // Pas d'empilement d'une même définition sur une même ville : une
            // deuxième grève ne tombe pas sur une mine déjà en grève. Sans cette
            // règle, les fréquences élevées composeraient les multiplicateurs
            // jusqu'aux bornes de sécurité, et on mesurerait les bornes au lieu de
            // l'événement.
            bool clash = false;
            foreach (var city in cities)
                if (state.IsRunning(def.Id, city.Id)) { clash = true; break; }
            if (clash) continue;

            int span = def.DurationMaxTicks - def.DurationMinTicks + 1;
            int duration = def.DurationMinTicks + Math.Min(span - 1, (int)(length * span));
            int ramp = Math.Min(def.RampTicks, (duration - 1) / 2);

            var targets = new List<EventTarget>();
            foreach (var city in cities)
                foreach (var effect in def.Effects)
                    if (EventLever.Nominal(city.Market(effect.Cargo), effect.On) > 0)
                        targets.Add(new EventTarget(city.Id, effect.Cargo, effect.On,
                            effect.FactorMin + severity * (effect.FactorMax - effect.FactorMin)));

            type.Occurrences++;
            state.Open(new EventRecord
            {
                InstanceId = $"{def.Id}#{type.Occurrences}",
                DefinitionId = def.Id,
                Name = def.Name,
                Origin = EventOrigin.Random,
                StartTick = tick.Index,
                EndTick = tick.Index + duration - 1,
                RampTicks = ramp,
                Targets = targets,
            });
        }
    }

    /// <summary>
    /// Compose les multiplicateurs du jour et les publie sur les marchés. Les
    /// événements superposés se multiplient — deux chocs indépendants se composent,
    /// ils ne s'additionnent pas — puis le produit est borné par les bornes de
    /// sécurité des données.
    /// </summary>
    private static void PublishFactors(WorldState world, EventsState state, int now)
    {
        foreach (var city in world.Cities)
            foreach (var market in world.MarketsOf(city))
            {
                market.EventProductionFactor = 1.0;
                market.EventDemandFactor = 1.0;
            }

        foreach (var record in state.Active)
            foreach (var target in record.Targets)
            {
                var market = world.CityById(target.CityId).Market(target.Cargo);
                double factor = record.FactorAt(target, now);
                if (target.On == EventLever.Production) market.EventProductionFactor *= factor;
                else market.EventDemandFactor *= factor;
            }

        double min = state.Def.MinFactor, max = state.Def.MaxFactor;
        foreach (var city in world.Cities)
            foreach (var market in world.MarketsOf(city))
            {
                market.EventProductionFactor = Maths.Clamp(market.EventProductionFactor, min, max);
                market.EventDemandFactor = Maths.Clamp(market.EventDemandFactor, min, max);
            }
    }

    /// <summary>Tick de début d'un événement historique : donné tel quel, ou calculé depuis sa date.</summary>
    public static int StartTickOf(EventsDef def, HistoricalEventDef h)
    {
        if (h.StartTick is int tick) return tick;
        int monthDays = SimTick.TicksPerYear / 12;
        return (h.Year!.Value - def.StartYear) * SimTick.TicksPerYear
             + (h.Month!.Value - 1) * monthDays
             + (h.Day!.Value - 1);
    }

    /// <summary>
    /// Villes visées : la liste explicite, ou à défaut toutes celles où au moins un
    /// effet a prise. Dans l'ordre du scénario dans les deux cas, pour que le tirage
    /// d'une cible désigne la même ville d'une exécution à l'autre.
    /// </summary>
    private static List<City> ResolveCities(
        WorldState world, string id, List<string> declared, List<EventEffectDef> effects)
    {
        bool Grips(City city) => effects.Any(e => EventLever.Nominal(city.Market(e.Cargo), e.On) > 0);

        if (declared.Count == 0)
            return world.Cities.Where(Grips).ToList();

        var result = new List<City>();
        foreach (var city in world.Cities)
        {
            if (!declared.Contains(city.Id)) continue;
            // Un événement qui vise une ville sur laquelle il n'a aucune prise ne
            // fait rien, sans le dire. C'est une erreur de données, et elle se
            // signale au chargement plutôt qu'en fin de campagne de mesure.
            if (!Grips(city))
                throw new InvalidDataException(
                    $"L'événement '{id}' vise '{city.Id}', qui ne produit ni ne consomme ce qu'il touche.");
            result.Add(city);
        }
        return result;
    }

    /// <summary>
    /// Validation au chargement. On échoue fort et tôt, comme
    /// <c>WorldBuilder.Validate</c> : un événement mal daté ou mal ciblé tourne
    /// sans erreur et ne fait rien, ce qui est la pire façon d'échouer.
    /// </summary>
    private static void Validate(WorldState world, EventsDef def)
    {
        if (def.RandomSequence == 1)
            throw new InvalidDataException(
                "events.randomSequence ne peut pas valoir 1 : c'est la séquence du monde.");
        if (world.Def.Finance.Enabled && def.RandomSequence == world.Def.Finance.RandomSequence)
            throw new InvalidDataException(
                "events.randomSequence doit différer de finance.randomSequence : les deux modules tireraient le même flux.");
        if (!(def.MinFactor > 0) || def.MaxFactor < 1.0 || def.MinFactor > 1.0)
            throw new InvalidDataException(
                "events : il faut 0 < minFactor ≤ 1 ≤ maxFactor.");

        var ids = new HashSet<string>();
        var cityIds = world.Cities.Select(c => c.Id).ToHashSet();

        void CheckCommon(string id, List<string> cities, List<EventEffectDef> effects)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("Un événement n'a pas d'identifiant.");
            if (!ids.Add(id))
                throw new InvalidDataException($"Événement en double : '{id}'.");
            if (effects.Count == 0)
                throw new InvalidDataException($"L'événement '{id}' n'a aucun effet.");
            foreach (string city in cities)
                if (!cityIds.Contains(city))
                    throw new InvalidDataException($"L'événement '{id}' vise la ville inconnue '{city}'.");
            foreach (var e in effects)
            {
                if (!world.CargoOrder.Contains(e.Cargo))
                    throw new InvalidDataException($"L'événement '{id}' touche la marchandise inconnue '{e.Cargo}'.");
                if (e.On != EventLever.Production && e.On != EventLever.Demand)
                    throw new InvalidDataException(
                        $"L'événement '{id}' : « on » vaut '{e.On}', attendu 'production' ou 'demand'.");
            }
        }

        foreach (var h in def.Historical)
        {
            CheckCommon(h.Id, h.Cities, h.Effects);

            if (h.Basis != "historical" && h.Basis != "inspired")
                throw new InvalidDataException(
                    $"L'événement '{h.Id}' : « basis » vaut '{h.Basis}', attendu 'historical' ou 'inspired'.");
            // Le mot « historique » engage : il se paie d'une source.
            if (h.Basis == "historical" && string.IsNullOrWhiteSpace(h.Source))
                throw new InvalidDataException(
                    $"L'événement '{h.Id}' se dit historique sans source : le sourcer dans docs/SOURCES.md, ou le déclarer 'inspired'.");

            bool byTick = h.StartTick is not null;
            bool byDate = h.Year is not null || h.Month is not null || h.Day is not null;
            if (byTick == byDate)
                throw new InvalidDataException(
                    $"L'événement '{h.Id}' doit être daté par startTick ou par year/month/day, et pas les deux.");
            if (byTick && h.StartTick < 0)
                throw new InvalidDataException($"L'événement '{h.Id}' commence avant le tick 0.");
            if (byDate)
            {
                if (h.Year is null || h.Month is null || h.Day is null)
                    throw new InvalidDataException($"L'événement '{h.Id}' : date incomplète.");
                if (def.StartYear <= 0)
                    throw new InvalidDataException(
                        $"L'événement '{h.Id}' est daté, mais events.startYear n'est pas renseigné.");
                if (h.Month < 1 || h.Month > 12 || h.Day < 1 || h.Day > SimTick.TicksPerYear / 12)
                    throw new InvalidDataException(
                        $"L'événement '{h.Id}' : {h.Day}/{h.Month} n'existe pas dans le calendrier de jeu (12 mois de 30 jours).");
                if (StartTickOf(def, h) < 0)
                    throw new InvalidDataException($"L'événement '{h.Id}' est daté avant le début de la partie.");
            }

            if (h.DurationTicks <= 0)
                throw new InvalidDataException($"L'événement '{h.Id}' a une durée nulle.");
            if (h.RampTicks < 0 || 2 * h.RampTicks >= h.DurationTicks)
                throw new InvalidDataException(
                    $"L'événement '{h.Id}' : montée et descente ({h.RampTicks} ticks chacune) ne tiennent pas dans sa durée ({h.DurationTicks}).");
            foreach (var e in h.Effects)
                if (!(e.Factor > 0))
                    throw new InvalidDataException($"L'événement '{h.Id}' : multiplicateur nul ou négatif.");
        }

        foreach (var r in def.Random)
        {
            CheckCommon(r.Id, r.Cities, r.Effects);

            if (r.Scope != "one" && r.Scope != "all")
                throw new InvalidDataException(
                    $"Le type '{r.Id}' : « scope » vaut '{r.Scope}', attendu 'one' ou 'all'.");
            if (r.OccurrencesPerYear < 0)
                throw new InvalidDataException($"Le type '{r.Id}' a une fréquence négative.");
            if (r.DurationMinTicks <= 0 || r.DurationMaxTicks < r.DurationMinTicks)
                throw new InvalidDataException(
                    $"Le type '{r.Id}' : il faut 0 < durationMinTicks ≤ durationMaxTicks.");
            if (r.RampTicks < 0)
                throw new InvalidDataException($"Le type '{r.Id}' a une montée négative.");
            foreach (int m in r.Months)
                if (m < 1 || m > 12)
                    throw new InvalidDataException($"Le type '{r.Id}' cite le mois {m}.");
            foreach (var e in r.Effects)
                if (!(e.FactorMin > 0) || e.FactorMax < e.FactorMin)
                    throw new InvalidDataException(
                        $"Le type '{r.Id}' : il faut 0 < factorMin ≤ factorMax.");
        }
    }
}
