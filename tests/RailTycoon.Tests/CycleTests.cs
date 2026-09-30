using System.Globalization;
using RailTycoon.Sim;
using RailTycoon.Sim.Core;
using RailTycoon.Sim.Cycle;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Events;
using RailTycoon.Sim.Finance;
using RailTycoon.Sim.Telemetry;

namespace RailTycoon.Tests;

/// <summary>
/// Invariants du module cycle. Même organisation que <see cref="FinanceTests"/> et
/// <see cref="EventTests"/> : un fichier à part, un seul appel dans <c>Program</c>.
/// </summary>
internal static class CycleTests
{
    /// <summary>
    /// Durée d'épreuve de heartland-cycle : six années de jeu. Assez pour contenir la
    /// panique de 1873 (tick 1 337), au moins un cycle complet — de la crise
    /// d'ouverture à la crise suivante —, et toute la crise que la panique ouvre
    /// (630 jours au plus) jusqu'à la reprise. 720 ticks n'y suffisent pas : la
    /// première crise peut à elle seule en durer 630.
    /// </summary>
    public const int Ticks = 2160;

    private static string CyclePath() => Path.Combine(Fixtures.RepoRoot(), "data", "heartland-cycle.json");
    private static string FinancePath() => Path.Combine(Fixtures.RepoRoot(), "data", "heartland-finance.json");
    private static string EventsPath() => Path.Combine(Fixtures.RepoRoot(), "data", "heartland-events.json");

    private static IEconomySolver Solver(string name)
        => name == "reference" ? new ReferenceEconomySolver() : new AnticipatingEconomySolver();

    private static readonly string[] Solvers = ["reference", "anticipating"];

    public static void Register(TestRunner runner)
    {
        // ---------------------------------------------------------- neutralité

        runner.Add("cycle — sans bloc, rien ne bouge : ni tirage, ni demande, ni taux", () =>
        {
            // Les traces de référence prouvent déjà que les empreintes n'ont pas bougé.
            // Ce test regarde ce qu'elles ne voient pas : chaque multiplicateur de
            // demande reste à 1 exactement à chaque tick, et chaque obligation porte
            // le taux facial de son offre, sans décomposition.
            var sim = new Simulation(ScenarioLoader.Load(FinancePath()));
            Check.True(!sim.World.Cycle.Enabled, "heartland-finance ne déclare pas de cycle");
            for (int i = 0; i < 720; i++)
            {
                sim.Step();
                foreach (var city in sim.World.Cities)
                    foreach (var market in sim.World.MarketsOf(city))
                        Check.True(market.CycleDemandFactor == 1.0,
                            $"tick {sim.World.Tick.Index} : {city.Id}/{market.CargoId} demande touchée sans cycle");
            }
            var offers = sim.World.Def.Finance.BondOffers;
            var player = sim.World.Finance.Player!;
            var issued = player.Bonds.Where(b => offers.Any(o => o.Id == b.OfferId)).ToList();
            Check.True(issued.Count >= 3, "le test perdrait son sens sans emprunt");
            foreach (var bond in issued)
            {
                var offer = offers.Single(o => o.Id == bond.OfferId);
                Check.True(bond.AnnualRatePercent == offer.AnnualRatePercent,
                    $"{bond.OfferId} : taux {bond.AnnualRatePercent} sans cycle, attendu le facial {offer.AnnualRatePercent}");
            }
            Check.True(player.Bonds.All(b => b.Quote is null), "sans cycle, aucune obligation n'a de décomposition");
            Check.True(sim.World.Cycle.Journal.Count == 0 && sim.World.Cycle.Summary() == "",
                "sans cycle, le journal reste vide");
        });

        runner.Add("cycle — désactivé, heartland-cycle rejoue finance + événements au bit près, sous les deux solveurs", () =>
        {
            // Le bloc est PLEIN — quatre phases, un prix du crédit — et désactivé ; les
            // événements portent leurs attributs « cycle ». Rien ne doit se tirer ni se
            // publier : la partie est celle de la finance de heartland-finance et des
            // événements de heartland-events réunis, au centime près, panique comprise.
            foreach (string solver in Solvers)
            {
                var disabled = ScenarioLoader.Load(CyclePath());
                Check.True(disabled.Cycle.Phases.Count == 4, "le test perdrait son sens si le bloc était vide");
                disabled.Cycle.Enabled = false;

                var combined = ScenarioLoader.Load(FinancePath());
                combined.Events = ScenarioLoader.Load(EventsPath()).Events;

                Check.Equal(Fingerprint(combined, solver, Ticks), Fingerprint(disabled, solver, Ticks),
                    $"heartland-cycle désactivé et finance + événements (solveur {solver})");
            }
        });

        runner.Add("cycle — heartland-cycle reprend finance et événements au caractère près ; les autres n'en déclarent pas", () =>
        {
            // Seuls l'identité et le bloc cycle ont le droit de différer de la
            // réunion des deux scénarios d'épreuve. Comparaison du contenu sérialisé,
            // comme pour la finance et les événements : c'est ce qui a attrapé le bloc
            // anticipating manquant de heartland-finance. Le bloc objectives est mis à
            // part lui aussi : c'est un observateur pur, qui ne change rien à la partie
            // (ObjectiveTests le vérifie sur ce scénario même).
            string Content(ScenarioDef s)
            {
                s.Id = "";
                s.Name = "";
                s.Cycle = new CycleDef();
                s.Objectives = new RailTycoon.Sim.Objectives.ObjectivesDef();
                return System.Text.Json.JsonSerializer.Serialize(s);
            }

            var cycle = ScenarioLoader.Load(CyclePath());
            Check.True(cycle.Cycle.Enabled && cycle.Finance.Enabled && cycle.Events.Enabled,
                "heartland-cycle doit activer cycle, finance et événements");

            var fromFinance = ScenarioLoader.Load(FinancePath());
            fromFinance.Events = ScenarioLoader.Load(EventsPath()).Events;
            var fromEvents = ScenarioLoader.Load(EventsPath());
            fromEvents.Finance = ScenarioLoader.Load(FinancePath()).Finance;

            string expected = Content(cycle);
            Check.True(expected == Content(fromFinance),
                "heartland-cycle doit être heartland-finance plus les événements de heartland-events, et le bloc cycle");
            Check.True(expected == Content(fromEvents),
                "heartland-cycle doit être heartland-events plus la finance de heartland-finance, et le bloc cycle");

            // Et les scénarios d'épreuve des autres modules n'ont pas de cycle, même
            // désactivé : chacun éprouve une chose à la fois.
            foreach (string file in new[] { "heartland.json", "heartland-finance.json", "heartland-events.json" })
            {
                var other = ScenarioLoader.Load(Path.Combine(Fixtures.RepoRoot(), "data", file));
                Check.True(!other.Cycle.Enabled && other.Cycle.Phases.Count == 0, $"{file} ne doit porter aucun cycle");
            }
        });

        // --------------------------------------------------------- calendrier

        runner.Add("cycle — seul, les phases se succèdent dans l'ordre et durent leur tirage, dans leurs bornes", () =>
        {
            // Un cycle sans événements, sur un monde minuscule, pendant 30 ans : une
            // vingtaine de cycles complets. Chaque phase achevée a duré exactement ce
            // que son tirage lui donnait — rejoué ici sur la séquence du module, un
            // nombre par phase —, donc entre ses bornes, et la suivante est celle qui
            // la suit dans la liste.
            var scenario = Tiny(ScenarioLoader.Load(CyclePath()).Cycle);
            var sim = new Simulation(scenario);
            sim.Run(30 * SimTick.TicksPerYear);

            var journal = sim.World.Cycle.Journal;
            var phases = journal.Where(r => r.Kind == CycleRecordKind.Phase).ToList();
            Check.True(phases.Count >= 20, $"trop peu de phases pour éprouver les bornes : {phases.Count}");
            Check.True(journal.All(r => r.Kind == CycleRecordKind.Phase), "sans événements, aucune poussée");
            var def = scenario.Cycle;
            var drawn = ReplayDraws(journal, def, scenario.Seed);
            var seen = new HashSet<int>();
            for (int k = 1; k < phases.Count; k++)
            {
                var phase = def.Phases.Single(p => p.Id == phases[k - 1].PhaseId);
                int lasted = phases[k].Tick - phases[k - 1].Tick;
                Check.True(lasted == drawn[k - 1],
                    $"{phase.Id} ouverte au tick {phases[k - 1].Tick} a duré {lasted}, son tirage disait {drawn[k - 1]}");
                Check.True(lasted >= phase.MinTicks && lasted <= phase.MaxTicks,
                    $"{phase.Id} ouverte au tick {phases[k - 1].Tick} a duré {lasted}, hors de [{phase.MinTicks}, {phase.MaxTicks}]");
                seen.Add(lasted);
                int expected = (def.Phases.IndexOf(phase) + 1) % def.Phases.Count;
                Check.Equal(def.Phases[expected].Id, phases[k].PhaseId, $"phase qui suit {phase.Id}");
                Check.True(phases[k].Cause == CycleCause.Elapsed && phases[k].PreviousPhaseId == phase.Id,
                    $"tick {phases[k].Tick} : cause et phase quittée");
            }
            Check.True(seen.Count >= phases.Count / 2, "les durées doivent être tirées, pas constantes");
        });

        runner.Add("cycle — avec les événements, une phase dure son tirage plus les poussées publiées, au jour près", () =>
        {
            // Sur heartland-cycle, les poussées déplacent la fin des phases. Pour chaque
            // phase arrivée à échéance d'elle-même, sa durée vaut exactement son tirage
            // plus la somme des poussées publiées au journal pendant qu'elle courait :
            // le journal dit tout ce qui a déplacé la fin, rien d'autre ne l'a déplacée,
            // et la séquence du module a servi un nombre par phase ouverte ou forcée,
            // pas un de plus. Une phase qu'une poussée fait échoir a duré au moins son
            // tirage plus ses poussées, et finit le jour de la dernière.
            var scenario = Stripped(ScenarioLoader.Load(CyclePath()));
            var sim = new Simulation(scenario);
            sim.Run(Ticks);
            var journal = sim.World.Cycle.Journal;
            var def = sim.World.Def.Cycle;
            var drawn = ReplayDraws(journal, def, scenario.Seed);
            int checkedPhases = 0, shifted = 0;
            var starts = journal.Select((r, i) => (r, i)).Where(x => x.r.Kind == CycleRecordKind.Phase).ToList();
            for (int k = 1; k < starts.Count; k++)
            {
                var (open, i0) = starts[k - 1];
                var (close, i1) = starts[k];
                if (close.Cause == CycleCause.Forced) continue;
                var phase = def.Phases.Single(p => p.Id == open.PhaseId);
                int shifts = journal.Skip(i0 + 1).Take(i1 - i0 - 1).Sum(r => r.ShiftTicks);
                if (shifts != 0) shifted++;
                int lasted = close.Tick - open.Tick;
                if (close.Cause == CycleCause.Elapsed)
                    Check.True(lasted == drawn[i0] + shifts,
                        $"{phase.Id} du tick {open.Tick} au tick {close.Tick} : tirage {drawn[i0]} + poussées {shifts} ≠ {lasted}");
                else
                    Check.True(lasted >= drawn[i0] + shifts && journal[i1 - 1].Tick == close.Tick,
                        $"{phase.Id} hâtée au tick {close.Tick} : tirage {drawn[i0]} + poussées {shifts}, durée {lasted}");
                checkedPhases++;
            }
            Check.True(checkedPhases >= 3 && shifted >= 1,
                $"le test perdrait son sens : {checkedPhases} phases vérifiées, dont {shifted} poussées");
        });

        runner.Add("cycle — la panique de 1873 force la crise à sa date, quel que soit le calendrier tiré", () =>
        {
            // Huit calendriers différents : selon le tirage, la panique tombe en
            // expansion, en ralentissement ou en pleine crise. Dans tous les cas, au
            // tick 1 337, la conjoncture est en crise, et le journal l'attribue à la
            // panique — une bascule, ou une prolongation si la crise était déjà là.
            // Aucune bascule forcée avant.
            int switched = 0;
            for (ulong seq = 13; seq < 21; seq++)
            {
                var scenario = Stripped(ScenarioLoader.Load(CyclePath()));
                scenario.Cycle.RandomSequence = seq + 100;
                var sim = new Simulation(scenario);
                sim.Run(1337);
                var cycle = sim.World.Cycle;
                Check.Equal("crise", cycle.Phase!.Id, $"séquence {seq + 100}, phase au tick 1 337");
                var forced = cycle.Journal.Where(r => r.Cause == CycleCause.Forced).ToList();
                Check.True(forced.Count == 1 && forced[0].Tick == 1337 && forced[0].EventInstanceId == "panique-1873",
                    $"séquence {seq + 100} : une seule bascule forcée, au tick 1 337, par la panique " +
                    $"({string.Join(", ", forced.Select(r => $"{r.Tick}/{r.EventInstanceId}"))})");
                if (forced[0].Kind == CycleRecordKind.Phase)
                {
                    switched++;
                    Check.True(cycle.PhaseStartTick == 1337, "la crise forcée commence le jour de la panique");
                }
            }
            Check.True(switched >= 1, "au moins un calendrier doit faire basculer la conjoncture, pas seulement la prolonger");
        });

        runner.Add("cycle — un événement aléatoire marqué déplace la conjoncture, un non marqué ne la touche pas", () =>
        {
            // Deux phases de durée fixe (100 jours) et un type aléatoire qui se
            // déclenche chaque fois qu'il le peut : tous les dix jours. Non marqué,
            // le calendrier de la conjoncture est exactement celui d'un monde sans
            // événements. Marqué d'une bonne nouvelle de 3 jours, chaque occurrence
            // allonge la phase favorable et abrège la défavorable, et le journal
            // nomme chacune.
            string Calendar(EventCycleEffectDef? effect, bool withEvents)
            {
                var sim = new Simulation(Pulsed(effect, withEvents));
                sim.Run(400);
                return sim.World.Cycle.Summary();
            }

            string bare = Calendar(null, withEvents: false);
            Check.Equal(bare, Calendar(null, withEvents: true), "un type non marqué ne doit pas toucher la conjoncture");

            var marked = new Simulation(Pulsed(new EventCycleEffectDef { PushTicks = 3 }, withEvents: true));
            marked.Run(400);
            var journal = marked.World.Cycle.Journal;
            var shifts = journal.Where(r => r.Kind == CycleRecordKind.Shift).ToList();
            Check.True(shifts.Count >= 20, $"chaque occurrence doit pousser la conjoncture ({shifts.Count})");
            Check.True(shifts.All(r => r.EventInstanceId.StartsWith("pulsation#") && r.Cause == CycleCause.Pushed),
                "le journal doit nommer l'occurrence responsable");
            Check.True(shifts.Where(r => r.PhaseId == "haut").All(r => r.ShiftTicks == 3)
                       && shifts.Where(r => r.PhaseId == "bas").All(r => r.ShiftTicks == -3)
                       && shifts.Any(r => r.PhaseId == "bas"),
                "une bonne nouvelle allonge la phase favorable et abrège la défavorable");
            var firstChange = journal.First(r => r.Kind == CycleRecordKind.Phase && r.Cause != CycleCause.Opening);
            Check.True(firstChange.Tick > 100,
                $"la phase favorable allongée doit finir après son tirage de 100 jours (tick {firstChange.Tick})");
            Check.True(marked.World.Cycle.Summary() != bare, "le calendrier marqué doit différer");
        });

        // ------------------------------------------------------- déterminisme

        runner.Add("cycle — activer le cycle ne décale ni le flux des événements ni celui de la finance", () =>
        {
            // heartland-cycle, conjoncture active mais sans aucun effet : taux,
            // multiple, demande et prime neutres. Elle tire ses durées, lit le journal
            // des événements, bascule à la panique — et la partie doit être celle du
            // bloc désactivé, au centime, journal des événements compris. Si le module
            // tirait sur le flux des événements ou de la finance, le calendrier des
            // aléas ou le bruit des concurrents seraient décalés.
            var neutral = ScenarioLoader.Load(CyclePath());
            foreach (var p in neutral.Cycle.Phases)
            {
                p.RateAdjustmentPercent = 0m;
                p.EarningsMultipleFactor = 1m;
                p.DemandFactor = 1.0;
            }
            neutral.Cycle.Credit.RiskPremium.LeverageSlopePercent = 0m;
            neutral.Cycle.Credit.RiskPremium.LossSlopePercent = 0m;
            var disabled = ScenarioLoader.Load(CyclePath());
            disabled.Cycle.Enabled = false;

            var a = new Simulation(disabled);
            var b = new Simulation(neutral);
            var ra = new CsvRecorder();
            var rb = new CsvRecorder();
            for (int i = 0; i < 1440; i++)
            {
                a.Step(); ra.Record(a.World);
                b.Step(); rb.Record(b.World);
            }

            Check.True(b.World.Cycle.Journal.Count(r => r.Kind == CycleRecordKind.Phase) >= 3
                       && b.World.Cycle.Journal.Any(r => r.Cause == CycleCause.Forced),
                "la conjoncture doit réellement tirer, basculer et être forcée pendant la course");
            Check.Equal(ra.TraceFingerprint(FinanceState(a.World) + a.World.Events.Summary()),
                        rb.TraceFingerprint(FinanceState(b.World) + b.World.Events.Summary()),
                "traces, état financier et journal des événements, sans puis avec une conjoncture neutre");
            Check.True(b.World.Rng.NextUInt() == new DeterministicRandom(neutral.Seed).NextUInt(),
                "le module cycle a tiré sur le flux du monde");
            Check.True(b.World.Events.Rng!.NextUInt() == a.World.Events.Rng!.NextUInt()
                       && b.World.Finance.Rng!.NextUInt() == a.World.Finance.Rng!.NextUInt(),
                "les flux des événements et de la finance doivent en être au même point");
        });

        runner.Add("cycle — une séquence partagée avec la finance ou les événements est refusée", () =>
        {
            var s1 = ScenarioLoader.Load(CyclePath());
            s1.Cycle.RandomSequence = s1.Finance.RandomSequence;
            Check.Throws<InvalidDataException>(() => new Simulation(s1), "même séquence que la finance");
            var s2 = ScenarioLoader.Load(CyclePath());
            s2.Cycle.RandomSequence = s2.Events.RandomSequence;
            Check.Throws<InvalidDataException>(() => new Simulation(s2), "même séquence que les événements");
            var s3 = ScenarioLoader.Load(CyclePath());
            s3.Cycle.RandomSequence = 1;
            Check.Throws<InvalidDataException>(() => new Simulation(s3), "séquence du monde");
        });

        runner.Add("cycle — des données incohérentes sont refusées au chargement", () =>
        {
            Check.Throws<InvalidDataException>(() => new Simulation(With(s => s.Cycle.InitialPhase = "boom")),
                "une phase initiale inconnue");
            Check.Throws<InvalidDataException>(() => new Simulation(With(s => s.Cycle.Phases[0].MinTicks = 1000)),
                "des bornes inversées");
            Check.Throws<InvalidDataException>(() => new Simulation(With(s => s.Cycle.Phases[1].Id = s.Cycle.Phases[0].Id)),
                "une phase en double");
            Check.Throws<InvalidDataException>(() => new Simulation(With(s => s.Cycle.Phases[2].EarningsMultipleFactor = 0m)),
                "un multiple nul");
            Check.Throws<InvalidDataException>(() => new Simulation(With(s =>
                    s.Events.Historical.Single(h => h.Id == "panique-1873").Cycle!.ForcePhase = "krach")),
                "un événement qui force une phase inconnue ne ferait rien sans le dire");
            Check.Throws<InvalidDataException>(() => new Simulation(With(s =>
                    s.Events.Random.First(r => r.Cycle is not null).Cycle!.ForcePhase = "crise")),
                "un effet « cycle » qui force et pousse à la fois");
            Check.Throws<InvalidDataException>(() => new Simulation(With(s =>
                    s.Events.Random.First(r => r.Cycle is not null).Cycle!.PushTicks = 0)),
                "un effet « cycle » vide");
        });

        // ------------------------------------------------------------- effets

        runner.Add("cycle — une obligation émise en crise coûte plus cher qu'en expansion, et son taux se décompose", () =>
        {
            // La même partie, ouverte en crise ou en expansion : les trois emprunts
            // de la compagnie tombent dans les 76 premiers jours, donc dans la phase
            // d'ouverture, sans transition. Le taux de chacun est fixé à l'émission et
            // ne bouge plus.
            (List<Bond> Bonds, int Tick) Borrow(string phase)
            {
                var scenario = ScenarioLoader.Load(CyclePath());
                scenario.Cycle.InitialPhase = phase;
                var sim = new Simulation(scenario);
                sim.Run(120);
                var bonds = sim.World.Finance.Player!.Bonds.Where(b => b.Quote is not null).ToList();
                var rates = bonds.Select(b => b.AnnualRatePercent).ToList();
                sim.Run(600);
                Check.True(bonds.Select(b => b.AnnualRatePercent).SequenceEqual(rates),
                    "le taux d'une obligation est fixé à l'émission, il ne suit pas la conjoncture");
                return (bonds, sim.World.Tick.Index);
            }

            var crisis = Borrow("crise");
            var boom = Borrow("expansion");
            Check.True(crisis.Bonds.Count >= 3 && boom.Bonds.Count >= 3, "trois emprunts attendus dans chaque partie");

            var def = ScenarioLoader.Load(CyclePath()).Cycle;
            decimal crisisAdj = def.Phases.Single(p => p.Id == "crise").RateAdjustmentPercent;
            decimal boomAdj = def.Phases.Single(p => p.Id == "expansion").RateAdjustmentPercent;
            foreach (var (bonds, adj, phase) in new[] { (crisis.Bonds, crisisAdj, "crise"), (boom.Bonds, boomAdj, "expansion") })
                foreach (var bond in bonds)
                {
                    var q = bond.Quote!;
                    Check.True(q.PhaseId == phase && q.PhaseAdjustmentPercent == adj,
                        $"{bond.OfferId} : émise en {q.PhaseId} avec {q.PhaseAdjustmentPercent}, attendu {phase} et {adj}");
                    Check.True(q.RatePercent == q.FacialPercent + q.PhaseAdjustmentPercent + q.RiskPremiumPercent - q.LeaderBonusPercent
                               && q.RatePercent == bond.AnnualRatePercent && q.LeaderBonusPercent == 0m,
                        $"{bond.OfferId} : taux {q.RatePercent} ≠ facial + conjoncture + prime − bonus");
                }

            var a = crisis.Bonds.Single(b => b.OfferId == "serie-a");
            var b = boom.Bonds.Single(x => x.OfferId == "serie-a");
            Check.Less((double)b.AnnualRatePercent, (double)a.AnnualRatePercent,
                "la série A doit coûter plus cher émise en crise qu'en expansion");
            Check.True(a.AnnualRatePercent - b.AnnualRatePercent >= crisisAdj - boomAdj - 0.5m,
                $"l'écart doit venir de la conjoncture ({a.AnnualRatePercent} contre {b.AnnualRatePercent})");
        });

        runner.Add("cycle — prix du crédit : prime de levier, prime de perte, plafond, plancher", () =>
        {
            var sim = new Simulation(Stripped(ScenarioLoader.Load(CyclePath())));
            var cycle = sim.World.Cycle;
            var risk = cycle.Def.Credit.RiskPremium;
            decimal adj = cycle.RateAdjustmentPercent;

            // Levier sous le seuil, résultat positif : pas de prime.
            var q0 = cycle.QuoteBond(6m, 40_000m, 100_000m, 10_000m);
            Check.True(q0.RiskPremiumPercent == 0m && q0.RatePercent == 6m + adj, $"sans risque : {q0}");

            // Levier 1,5 : (1,5 − seuil) × pente.
            var q1 = cycle.QuoteBond(6m, 150_000m, 100_000m, 10_000m);
            Check.True(q1.RiskPremiumPercent == Math.Round((1.5m - risk.LeverageThreshold) * risk.LeverageSlopePercent, 2),
                $"prime de levier : {q1.RiskPremiumPercent}");

            // Perte de 10 % des capitaux propres : 0,1 × pente de perte.
            var q2 = cycle.QuoteBond(6m, 0m, 100_000m, -10_000m);
            Check.True(q2.RiskPremiumPercent == Math.Round(0.1m * risk.LossSlopePercent, 2), $"prime de perte : {q2.RiskPremiumPercent}");

            // Capitaux propres nuls, ou levier extrême : le plafond.
            Check.True(cycle.QuoteBond(6m, 1m, 0m, 0m).RiskPremiumPercent == risk.MaxPercent, "capitaux propres nuls");
            Check.True(cycle.QuoteBond(6m, 10_000_000m, 1m, 0m).RiskPremiumPercent == risk.MaxPercent, "levier extrême");

            // Le plancher.
            Check.True(cycle.QuoteBond(-50m, 0m, 100_000m, 0m).RatePercent == cycle.Def.Credit.MinRatePercent, "plancher du taux");

            // Le découvert suit la conjoncture, sans prime.
            Check.True(cycle.OverdraftRate(14m) == 14m + adj, "taux du découvert");

            // Module inactif : le facial, tel quel.
            var off = new Simulation(ScenarioLoader.Load(FinancePath())).World.Cycle;
            var q3 = off.QuoteBond(7.5m, 10_000_000m, 1m, -1m);
            Check.True(q3.RatePercent == 7.5m && q3.RiskPremiumPercent == 0m && off.OverdraftRate(14m) == 14m,
                "sans cycle, ni ajustement ni prime");
        });

        runner.Add("cycle — la bourse suit la phase : cours plus bas en crise, pour toute la cote, pertes comprises", () =>
        {
            // Même partie ouverte en expansion ou en crise, où SEUL le multiple
            // diffère : ni taux, ni demande, ni ordres de bourse, ni acquisitions — un
            // cours se lit alors comme la valorisation, pas comme la trace d'un ordre
            // ou d'une fusion. Au tick 300, les trois cours sont plus bas en crise :
            // la compagnie et Great Plains, bénéficiaires, et Cascade, déficitaire —
            // un marché pessimiste compte une perte plus lourde, il ne l'allège pas.
            decimal[] Prices(string phase)
            {
                var scenario = ScenarioLoader.Load(CyclePath());
                scenario.Cycle.InitialPhase = phase;
                foreach (var p in scenario.Cycle.Phases)
                {
                    p.RateAdjustmentPercent = 0m;
                    p.DemandFactor = 1.0;
                }
                scenario.Finance.Acquisition.Targets.Clear();
                scenario.Finance.Tycoon.TradeIntervalTicks = 0;
                var sim = new Simulation(scenario);
                sim.Run(300);
                Check.True(sim.World.Cycle.Phase!.Id == phase && sim.World.Cycle.EarningsMultipleFactor ==
                           scenario.Cycle.Phases.Single(p => p.Id == phase).EarningsMultipleFactor,
                    $"{phase} : facteur publié au tick 300");
                var finance = sim.World.Finance;
                Check.True(finance.Player!.EarningsEma > 0m && finance.CompanyById("cascade")!.EarningsEma < 0m,
                    "le test veut une compagnie bénéficiaire et une déficitaire");
                return finance.Companies.Select(c => c.SharePrice).ToArray();
            }

            var boom = Prices("expansion");
            var crisis = Prices("crise");
            Check.True(boom.Length == 3, "trois compagnies cotées");
            for (int i = 0; i < boom.Length; i++)
                Check.Less((double)crisis[i], (double)boom[i], $"cours n°{i} en crise contre en expansion");
        });

        runner.Add("cycle — la demande de la conjoncture se compose avec celle des événements, sans la réécrire", () =>
        {
            // Un événement double la demande de b-ville, la conjoncture la porte à
            // ×1,1 partout. Le taux du jour est le produit des deux, au bit près ; le
            // multiplicateur des événements reste celui des événements, le sien reste
            // le sien. Sous les deux solveurs.
            foreach (string solver in Solvers)
            {
                var sim = new Simulation(Composed(), Solver(solver));
                for (int i = 0; i < 40; i++)
                {
                    sim.Step();
                    int t = sim.World.Tick.Index;
                    var b = sim.World.CityById("b-ville").Market("grain");
                    var c = sim.World.CityById("c-ville").Market("grain");
                    Check.True(b.CycleDemandFactor == 1.1 && c.CycleDemandFactor == 1.1,
                        $"{solver}, tick {t} : la conjoncture pose ×1,1 sur tous les marchés");
                    double expectedEvent = t >= 20 && t <= 30 ? 2.0 : 1.0;
                    Check.True(b.EventDemandFactor == expectedEvent && c.EventDemandFactor == 1.0,
                        $"{solver}, tick {t} : le multiplicateur des événements ne doit pas contenir la conjoncture");
                    Check.True(b.BaseDemandRate == b.NominalDemandRate * b.EventDemandFactor * b.CycleDemandFactor
                               && c.BaseDemandRate == c.NominalDemandRate * c.EventDemandFactor * c.CycleDemandFactor,
                        $"{solver}, tick {t} : taux du jour = nominal × événements × conjoncture");
                }
            }
        });

        runner.Add("cycle — la demande et les poussées de heartland-cycle sont équilibrées", () =>
        {
            // Décision de conception protégée, la même que pour le catalogue des
            // événements : un cycle oscille autour du scénario. Une demande moyenne
            // déplacée mangerait ou gonflerait le surplus de la carte ; un catalogue
            // qui pousse plus souvent dans un sens allongerait les phases de ce sens.
            var scenario = ScenarioLoader.Load(CyclePath());
            Check.Near(0, CycleBalance.DemandBias(scenario.Cycle), 0.002, "demande moyenne sur un cycle");
            Check.Near(1, CycleBalance.MeanEarningsMultipleFactor(scenario.Cycle), 0.05, "multiple moyen sur un cycle");
            var (good, bad, net) = CycleBalance.PushBalance(scenario);
            Check.True(good > 0 && bad < 0, "il faut des bonnes et des mauvaises nouvelles");
            Check.Near(0, net, 2.0, "poussée nette des aléatoires, en jours par an");

            // Et l'outil voit un couplage à sens unique.
            scenario.Events.Random.Single(r => r.Id == "marasme-batiment").Cycle = null;
            Check.Less(30, CycleBalance.PushBalance(scenario).Net, "sans le marasme, les poussées penchent vers les bonnes nouvelles");
        });

        // ---------------------------------------------------------- invariants

        runner.Add($"cycle — {Ticks} ticks : bilans au centime et invariants, sous les deux solveurs", () =>
        {
            foreach (string solver in Solvers)
            {
                var sim = new Simulation(ScenarioLoader.Load(CyclePath()), Solver(solver));
                double initial = Invariants.InitialStockTotal(sim.World);
                var finance = sim.World.Finance;
                var phasesSeen = new HashSet<string>();
                for (int i = 0; i < Ticks; i++)
                {
                    sim.Step();
                    phasesSeen.Add(sim.World.Cycle.Phase!.Id);
                    var violations = Invariants.Check(sim.World, initial);
                    if (violations.Count > 0)
                        Check.True(false, $"{solver}, tick {sim.World.Tick.Index} : {violations[0].Rule} — {violations[0].Detail}");
                    foreach (var company in finance.Companies)
                        Check.True(company.Book.Residual == 0m,
                            $"{solver}, tick {sim.World.Tick.Index} : écart de bilan {company.Book.Residual} sur {company.Id}");
                    Check.True(finance.Magnate!.Book.Residual == 0m, $"{solver} : écart de bilan du magnat");
                }

                foreach (var company in finance.Companies)
                {
                    Check.True(company.PrincipalIssuedTotal == company.PrincipalRepaidTotal + company.OutstandingPrincipal(),
                        $"{solver}, {company.Id} : capital emprunté ≠ remboursé + restant dû");
                    Check.True(company.InterestAccruedTotal == company.InterestPaidTotal + company.UnpaidInterest,
                        $"{solver}, {company.Id} : intérêts dus ≠ payés + à payer");
                }

                // Une comptabilité vide s'équilibre parfaitement : on exige que la
                // conjoncture ait réellement tourné et touché la finance.
                var cycle = sim.World.Cycle;
                Check.True(phasesSeen.Count == 4, $"{solver} : les quatre phases doivent avoir eu cours ({string.Join(",", phasesSeen)})");
                Check.True(cycle.Journal.Any(r => r.Cause == CycleCause.Forced && r.EventInstanceId == "panique-1873"),
                    $"{solver} : la panique doit avoir forcé la crise");
                Check.True(cycle.Journal.Count(r => r.Kind == CycleRecordKind.Shift) >= 10, $"{solver} : trop peu de poussées");
                Check.True(finance.Player!.Bonds.Count(b => b.Quote is not null) >= 3, $"{solver} : emprunts sans décomposition");
                Check.True(finance.Player.InterestPaidTotal > 0m && finance.Player.DividendsPaidTotal > 0m,
                    $"{solver} : intérêts et dividendes doivent avoir été payés");
            }
        });

        runner.Add("cycle — le journal public s'écrit en CSV et ne dit pas quand la phase finit", () =>
        {
            var sim = new Simulation(Stripped(ScenarioLoader.Load(CyclePath())));
            sim.Run(Ticks);
            string[] lines = CsvRecorder.CycleCsv(sim.World).TrimEnd('\n').Split('\n');
            Check.Equal("tick,kind,phase,previous_phase,cause,event,shift_ticks", lines[0], "en-tête du journal");
            Check.True(lines.Length == sim.World.Cycle.Journal.Count + 1,
                $"{lines.Length - 1} lignes pour {sim.World.Cycle.Journal.Count} entrées");
            Check.True(lines[1] == "0,phase,crise,,opening,,0", $"ouverture : « {lines[1]} »");
            Check.True(lines.Any(l => l.StartsWith("1337,") && l.Contains(",forced,panique-1873,")),
                "la bascule de la panique doit figurer au journal");

            var quiet = new Simulation(ScenarioLoader.Load(Fixtures.HeartlandPath()));
            quiet.Run(10);
            Check.True(CsvRecorder.CycleCsv(quiet.World).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 1,
                "sans module, le journal ne contient que son en-tête");
        });
    }

    // -------------------------------------------------------------- montages

    /// <summary>
    /// Rejoue les tirages du module sur sa séquence : un nombre par phase ouverte
    /// (ouverture, échéance, poussée, bascule forcée) et un par bascule forcée dans
    /// la phase déjà en cours, dans l'ordre du journal — la discipline qu'annonce
    /// <see cref="ReferenceCycleSolver"/>. Renvoie, pour chaque entrée du journal qui
    /// a tiré, la durée tirée (clé : son rang dans le journal).
    /// </summary>
    private static Dictionary<int, int> ReplayDraws(IReadOnlyList<CycleRecord> journal, CycleDef def, ulong seed)
    {
        var rng = new DeterministicRandom(seed, def.RandomSequence);
        var drawn = new Dictionary<int, int>();
        for (int i = 0; i < journal.Count; i++)
        {
            var r = journal[i];
            if (r.Kind == CycleRecordKind.Shift && r.Cause != CycleCause.Forced) continue;
            var phase = def.Phases.Single(p => p.Id == r.PhaseId);
            int span = phase.MaxTicks - phase.MinTicks + 1;
            drawn[i] = phase.MinTicks + Math.Min(span - 1, (int)(rng.NextDouble() * span));
        }
        return drawn;
    }

    /// <summary>heartland-cycle modifié en mémoire, pour les tests de validation.</summary>
    private static ScenarioDef With(Action<ScenarioDef> tweak)
    {
        var scenario = ScenarioLoader.Load(CyclePath());
        tweak(scenario);
        return scenario;
    }

    /// <summary>
    /// heartland-cycle sans trains ni finance : le calendrier de la conjoncture ne
    /// dépend ni de l'un ni de l'autre — il est exogène —, et les tests qui ne
    /// regardent que lui tournent ainsi cinq fois plus vite. Sans objectifs non plus :
    /// une fortune à amasser sans module finance est refusée au chargement.
    /// </summary>
    private static ScenarioDef Stripped(ScenarioDef scenario)
    {
        scenario.Trains.Clear();
        scenario.Finance = new FinanceDef();
        scenario.Objectives = new RailTycoon.Sim.Objectives.ObjectivesDef();
        return scenario;
    }

    /// <summary>Un monde minuscule — deux villes, du blé — qui porte la conjoncture donnée, sans événements.</summary>
    private static ScenarioDef Tiny(CycleDef cycle)
    {
        var scenario = Fixtures.Bare("conjoncture");
        scenario.Cities.Add(Fixtures.City("a-ville", production: 1.0, stock: 40));
        scenario.Cities.Add(Fixtures.City("b-ville", demand: 0.4, stock: 30));
        scenario.Cycle = cycle;
        return scenario;
    }

    /// <summary>
    /// Deux phases de 100 jours exactement, « haut » (favorable) puis « bas », et,
    /// si demandé, un type aléatoire « pulsation » qui se déclenche dès qu'il le peut
    /// (360 fois par an, dix jours chacun, pas d'empilement : une occurrence tous les
    /// dix jours), sans effet sur le marché, portant l'effet de conjoncture donné.
    /// </summary>
    private static ScenarioDef Pulsed(EventCycleEffectDef? effect, bool withEvents)
    {
        var cycle = new CycleDef
        {
            Enabled = true,
            InitialPhase = "haut",
            Phases =
            {
                new CyclePhaseDef { Id = "haut", Favorable = true, MinTicks = 100, MaxTicks = 100 },
                new CyclePhaseDef { Id = "bas", Favorable = false, MinTicks = 100, MaxTicks = 100 },
            },
        };
        var scenario = Tiny(cycle);
        if (withEvents)
            scenario.Events = new EventsDef
            {
                Enabled = true,
                Random =
                {
                    new RandomEventTypeDef
                    {
                        Id = "pulsation", Name = "Pulsation", OccurrencesPerYear = 360,
                        DurationMinTicks = 10, DurationMaxTicks = 10,
                        Effects = { new EventEffectDef { Cargo = "grain", On = "demand", FactorMin = 1.0, FactorMax = 1.0 } },
                        Cycle = effect,
                    },
                },
            };
        return scenario;
    }

    /// <summary>
    /// Trois villes ; un événement historique double la demande de b-ville du tick 20
    /// au tick 30 ; la conjoncture tient une seule phase à ×1,1 de demande.
    /// </summary>
    private static ScenarioDef Composed()
    {
        var scenario = Fixtures.Bare("composition");
        scenario.Cities.Add(Fixtures.City("a-ville", production: 1.0, stock: 40));
        scenario.Cities.Add(Fixtures.City("b-ville", demand: 0.4, stock: 30));
        scenario.Cities.Add(Fixtures.City("c-ville", demand: 0.3, stock: 30));
        scenario.Events = new EventsDef
        {
            Enabled = true,
            Historical =
            {
                new HistoricalEventDef
                {
                    Id = "essai", Name = "Essai", Basis = "inspired", StartTick = 20, DurationTicks = 11,
                    Cities = ["b-ville"],
                    Effects = { new EventEffectDef { Cargo = "grain", On = "demand", Factor = 2.0 } },
                },
            },
        };
        scenario.Cycle = new CycleDef
        {
            Enabled = true,
            InitialPhase = "seule",
            Phases =
            {
                new CyclePhaseDef { Id = "seule", Favorable = true, MinTicks = 1000, MaxTicks = 1000, DemandFactor = 1.1 },
                new CyclePhaseDef { Id = "autre", Favorable = false, MinTicks = 1000, MaxTicks = 1000, DemandFactor = 1.1 },
            },
        };
        return scenario;
    }

    private static string Fingerprint(ScenarioDef scenario, string solver, int ticks)
    {
        var sim = new Simulation(scenario, Solver(solver));
        var recorder = new CsvRecorder();
        recorder.Record(sim.World);
        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            recorder.Record(sim.World);
        }
        return recorder.TraceFingerprint(FinanceState(sim.World) + sim.World.Events.Summary() + sim.World.Cycle.Summary());
    }

    private static string FinanceState(WorldState world)
    {
        var finance = world.Finance;
        if (!finance.Enabled) return "";
        var player = finance.Player!;
        var ci = CultureInfo.InvariantCulture;
        return string.Join(";",
            player.Cash.ToString(ci), player.BookEquity.ToString(ci), player.SharePrice.ToString(ci),
            player.Debt.ToString(ci), player.OverdraftBalance.ToString(ci), player.InterestPaidTotal.ToString(ci),
            player.DividendsPaidTotal.ToString(ci), finance.Magnate!.NetWorth(finance).ToString(ci),
            finance.Magnate.MarginCalls.ToString(ci), finance.Mergers.Count.ToString(ci),
            string.Join(",", player.Bonds.Select(b => b.AnnualRatePercent.ToString(ci))),
            string.Join(",", finance.Companies.Select(c => c.OperatingResultTotal.ToString(ci))));
    }
}
