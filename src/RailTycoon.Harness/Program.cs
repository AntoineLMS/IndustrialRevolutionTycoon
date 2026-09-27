using System.Globalization;
using RailTycoon.Sim;
using RailTycoon.Sim.Telemetry;
using RailTycoon.Sim.Finance;

namespace RailTycoon.Harness;

/// <summary>
/// Harnais en ligne de commande. La simulation tourne ici sans moteur, sans
/// fenêtre et sans image : c'est ce qui permet de l'itérer en quelques secondes
/// et de la brancher dans une intégration continue.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var opts = Options.Parse(args);
        if (opts.ShowHelp)
        {
            Options.PrintUsage();
            return 0;
        }

        if (!File.Exists(opts.ScenarioPath))
        {
            Console.Error.WriteLine($"Scénario introuvable : {opts.ScenarioPath}");
            return 2;
        }

        var scenario = ScenarioLoader.Load(opts.ScenarioPath);

        // Un bloc absent rend son module inerte sans le dire. Le signaler ne coûte
        // qu'une ligne ; ne pas le signaler a déjà coûté une conclusion fausse.
        foreach (var block in ScenarioLoader.UndeclaredModuleBlocks(File.ReadAllText(opts.ScenarioPath)))
            Console.Error.WriteLine(
                $"Attention : aucun bloc « {block} » ni « //{block} » dans ce scénario — le module tourne sur ses défauts.");

        if (opts.BalanceOnly)
        {
            Report.PrintBalance(scenario);
            return 0;
        }

        if (opts.SurveyOnly)
        {
            Report.PrintSurvey(scenario);
            return 0;
        }

        var sim = new Simulation(scenario, SelectEconomy(opts.Solver));
        double initialStock = Invariants.InitialStockTotal(sim.World);
        var recorder = new CsvRecorder { Every = opts.RecordEvery };
        var stats = new RunStatistics { WarmupTicks = opts.WarmupTicks };
        var rotation = new FreightRotation { WarmupTicks = opts.WarmupTicks };

        var cashHistory = new List<double>();
        var violations = new List<(int Tick, Invariants.Violation V)>();

        // Pire écart de bilan observé sur toute la course. Il doit valoir zéro
        // exactement : la comptabilité est tenue en decimal et en partie double,
        // donc un écart d'un centime est un bug et non un résidu de calcul.
        decimal worstResidual = 0m;
        int worstResidualTick = 0;
        double worstFrontierGap = 0;

        recorder.Record(sim.World);
        for (int i = 0; i < opts.Ticks; i++)
        {
            sim.Step();
            recorder.Record(sim.World);
            stats.Sample(sim.World);
            rotation.Sample(sim.World);
            cashHistory.Add(sim.World.Company.Cash);

            decimal residual = Report.WorstResidual(sim.World);
            if (residual > worstResidual)
            {
                worstResidual = residual;
                worstResidualTick = sim.World.Tick.Index;
            }

            // Écart à la frontière entre le monde en double et la comptabilité. Il
            // ne s'accumule pas — on reflète le cumul arrondi, pas la somme des
            // flux arrondis — donc il doit rester sous le demi-centime pour
            // toujours, et non croître d'un demi-centime par tick.
            if (sim.World.Finance.Enabled)
            {
                double gap = Math.Abs(
                    (double)sim.World.Finance.ReflectedOperatingCash -
                    (sim.World.Def.StartingCash + sim.World.Company.NetProfit));
                if (gap > worstFrontierGap) worstFrontierGap = gap;
            }

            // On s'arrête au premier tick fautif : les violations en cascade
            // masquent la cause initiale, qui est la seule intéressante.
            if (violations.Count == 0)
            {
                foreach (var v in Invariants.Check(sim.World, initialStock))
                    violations.Add((sim.World.Tick.Index, v));
            }
        }

        recorder.WriteTo(opts.OutDir);
        CsvRecorder.WriteEvents(sim.World, opts.OutDir);
        Report.PrintBalance(scenario);
        Report.PrintRun(sim, opts, recorder, stats, cashHistory, rotation);
        Report.PrintEvents(sim);
        Report.PrintFinance(sim, worstResidual, worstResidualTick, worstFrontierGap);

        if (violations.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ÉCHEC — {violations.Count} invariant(s) violé(s), premier au tick {violations[0].Tick} :");
            foreach (var (tick, v) in violations.Take(10))
                Console.Error.WriteLine($"  [{tick}] {v.Rule} : {v.Detail}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Invariants : tous respectés.");
        return 0;
    }

    /// <summary>
    /// Choix du solveur économique. La référence reste le défaut : elle est le
    /// témoin auquel on compare, et une mesure publiée sans dire quel solveur l'a
    /// produite ne veut rien dire.
    /// </summary>
    private static RailTycoon.Sim.Economy.IEconomySolver SelectEconomy(string name) => name switch
    {
        "reference" => new RailTycoon.Sim.Economy.ReferenceEconomySolver(),
        "anticipating" => new RailTycoon.Sim.Economy.AnticipatingEconomySolver(),
        _ => throw new ArgumentException($"Solveur économique inconnu : '{name}' (reference, anticipating)"),
    };
}

internal sealed class Options
{
    public string ScenarioPath = Path.Combine("data", "heartland.json");
    public int Ticks = 720;
    public string OutDir = "out";
    public int RecordEvery = 1;
    public int WarmupTicks = 90;
    public bool BalanceOnly;
    public bool SurveyOnly;
    public bool ShowHelp;
    public string Solver = "reference";

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help": o.ShowHelp = true; break;
                case "-s" or "--scenario": o.ScenarioPath = Next(args, ref i); break;
                case "-t" or "--ticks": o.Ticks = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                case "-o" or "--out": o.OutDir = Next(args, ref i); break;
                case "--every": o.RecordEvery = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                case "--warmup": o.WarmupTicks = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                case "--balance": o.BalanceOnly = true; break;
                case "--solver": o.Solver = Next(args, ref i); break;
                case "--survey": o.SurveyOnly = true; break;
                default:
                    Console.Error.WriteLine($"Argument inconnu : {args[i]}");
                    o.ShowHelp = true;
                    break;
            }
        }
        return o;
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"Valeur manquante après {args[i]}");
        return args[++i];
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            Usage : railtycoon [options]

              -s, --scenario <fichier>   scénario JSON       (défaut : data/heartland.json)
              -t, --ticks <n>            nombre de ticks     (défaut : 720, soit deux ans)
              -o, --out <dossier>        sortie CSV          (défaut : out)
                  --every <n>            n'enregistrer qu'un tick sur n
                  --warmup <n>           ticks exclus des statistiques (défaut : 90)
                  --balance              bilan offre/demande du scénario, sans simuler
                  --solver <nom>         économie : reference | anticipating
                  --survey               devis de construction du réseau, sans simuler
              -h, --help                 cette aide
            """);
    }
}

internal static class Report
{
    private const string Blocks = "▁▂▃▄▅▆▇█";
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>
    /// Bilan statique. Affiché avant toute simulation parce qu'un déséquilibre
    /// structurel invalide d'avance la lecture des courbes : inutile de se
    /// demander pourquoi les boulangeries chôment si la farine ne peut pas exister
    /// en quantité suffisante.
    /// </summary>
    public static void PrintBalance(RailTycoon.Sim.Economy.ScenarioDef scenario)
    {
        Console.WriteLine($"Bilan offre/demande — {scenario.Name}");
        Console.WriteLine($"  {"Marchandise",-13}{"primaire",10}{"usines→",10}{"habitants",11}{"→usines",9}{"O/D",8}  verdict");

        foreach (var b in BalanceReport.Compute(scenario))
        {
            string ratio = double.IsPositiveInfinity(b.Ratio) ? "∞" : b.Ratio.ToString("0.00", Ci);
            Console.WriteLine(
                $"  {b.CargoName,-13}{b.PrimaryProduction.ToString("0.00", Ci),10}" +
                $"{b.IndustryOutput.ToString("0.00", Ci),10}" +
                $"{b.CitizenDemand.ToString("0.00", Ci),11}" +
                $"{b.IndustryInput.ToString("0.00", Ci),9}" +
                $"{ratio,8}  {b.Verdict}");
        }
        Console.WriteLine();

        // Le même bilan pour le catalogue d'événements aléatoires : un catalogue qui
        // ne frappe que dans un sens déplace l'équilibre du scénario sans rien
        // animer. Voir EventCatalogBalance.
        if (scenario.Events.Enabled && scenario.Events.Random.Count > 0)
        {
            Console.WriteLine("Biais du catalogue aléatoire (espérance annuelle, à l'échelle de la carte)");
            Console.WriteLine($"  {"Marchandise",-13}{"levier",-12}{"hausses",10}{"baisses",10}{"biais",9}  verdict");
            foreach (var b in RailTycoon.Sim.Events.EventCatalogBalance.Compute(scenario))
            {
                string name = scenario.Cargos.FirstOrDefault(c => c.Id == b.Cargo)?.Name ?? b.Cargo;
                string verdict = Math.Abs(b.Bias) <= 0.01 ? "équilibré" :
                    b.Bias > 0 ? "À LA HAUSSE — déplace l'équilibre du scénario" : "À LA BAISSE — déplace l'équilibre du scénario";
                Console.WriteLine($"  {name,-13}{(b.On == "production" ? "production" : "demande"),-12}" +
                                  $"{Pct(b.Up),10}{Pct(b.Down),10}{Pct(b.Bias),9}  {verdict}");
            }
            Console.WriteLine();
        }
    }

    private static string Pct(double x) => (x * 100).ToString("+0.0;-0.0;0.0", Ci) + " %";

    /// <summary>
    /// Devis du réseau, tronçon par tronçon, sans rien simuler.
    /// <para>
    /// C'est le pendant de <c>--balance</c> pour la géographie : il dit où part
    /// l'argent d'une ligne. Un total ne suffit pas à décider — c'est la ventilation
    /// entre voie, terrassement, ponts et tunnels qui indique s'il faut chercher un
    /// tracé plus bas, plus long, ou percer.
    /// </para>
    /// </summary>
    public static void PrintSurvey(RailTycoon.Sim.Economy.ScenarioDef scenario)
    {
        var world = new Simulation(scenario).World;
        if (world.Network is null)
        {
            Console.WriteLine($"{scenario.Name} ne déclare pas de réseau : ses lignes sont posées à la main.");
            return;
        }

        var network = world.Network;
        Console.WriteLine($"Devis de construction — {scenario.Name}");
        Console.WriteLine($"  relief {network.Terrain.Columns}×{network.Terrain.Rows} mailles de " +
                          $"{network.Terrain.CellSizeKm.ToString("0.##", Ci)} km, empreinte {network.Terrain.Fingerprint()}");
        Console.WriteLine();
        Console.WriteLine($"  {"Tronçon",-14}{"km",7}{"pente",8}{"stratégie",11}" +
                          $"{"voie",11}{"terrass.",11}{"ponts",11}{"tunnels",11}{"courbes",9}{"total",12}");

        foreach (var edge in network.Graph.Edges)
        {
            var c = edge.Construction;
            Console.WriteLine(
                $"  {edge.Id,-14}{c.LengthKm.ToString("0.0", Ci),7}" +
                $"{(c.MaxGradePercent.ToString("0.00", Ci) + "%"),8}" +
                $"{Strategy(c.ProfileBias),11}" +
                $"{c.TrackCost.ToString("N0", Ci),11}{c.EarthworkCost.ToString("N0", Ci),11}" +
                $"{c.BridgeCost.ToString("N0", Ci),11}{c.TunnelCost.ToString("N0", Ci),11}" +
                $"{c.CurveCost.ToString("N0", Ci),9}{c.TotalCost.ToString("N0", Ci),12}");

            foreach (var structure in c.Structures)
                Console.WriteLine(
                    $"      {structure.Kind,-10} du km {structure.StartKm.ToString("0.0", Ci)} " +
                    $"sur {structure.LengthKm.ToString("0.0", Ci)} km, " +
                    $"{structure.MaxHeightM.ToString("0", Ci)} m, {structure.Cost.ToString("N0", Ci)}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Total réseau {network.BuiltCost.ToString("N0", Ci),12}");
        Console.WriteLine();

        foreach (var route in network.Routes)
        {
            Console.WriteLine(
                $"  Itinéraire {route.Id,-10} {route.LengthKm.ToString("0.0", Ci),7} km, " +
                $"{route.Stations().Count} gares, rampe déterminante " +
                $"{RulingGrade(route, true).ToString("0.00", Ci)} % à l'aller / " +
                $"{RulingGrade(route, false).ToString("0.00", Ci)} % au retour");
        }
        Console.WriteLine();
    }

    private static string Strategy(double bias) => bias switch
    {
        <= 0.01 => "déblai",
        >= 0.99 => "remblai",
        _ => "mixte " + bias.ToString("0.00", Ci),
    };

    private static double RulingGrade(RailTycoon.Sim.Network.TrackRoute route, bool forward)
    {
        double worst = 0;
        foreach (var leg in route.Legs)
        {
            double grade = leg.Edge.Profile.RulingGradePercent(forward == leg.Forward);
            if (grade > worst) worst = grade;
        }
        return worst;
    }

    public static void PrintRun(
        Simulation sim, Options opts, CsvRecorder recorder,
        RunStatistics stats, List<double> cashHistory, FreightRotation rotation)
    {
        var w = sim.World;

        Console.WriteLine($"Scénario   : {w.Def.Name}  ({w.Def.Id}, graine {w.Def.Seed})");
        Console.WriteLine($"Modules    : économie={sim.Economy.Name}, transport={sim.Haulage.Name}, prix={w.PriceModel.Name}" +
                          (w.Events.Enabled ? $", événements={sim.Events.Name}" : ""));
        Console.WriteLine($"Durée      : {opts.Ticks} ticks  →  {w.Tick}   (statistiques sur {stats.Samples} ticks, {opts.WarmupTicks} de chauffe exclus)");
        Console.WriteLine($"Empreinte  : {recorder.Fingerprint()}   (doit être stable à graine identique)");
        Console.WriteLine();

        // --- Prix MOYENS, pas finaux. Un marché desservi oscille entre plein et
        // vide ; l'instantané du dernier tick tombe au hasard sur une crête ou un
        // creux et induit systématiquement en erreur.
        //
        // Les « — » sont les villes qui ne consomment pas la marchandise : leur
        // prix est au plancher par construction et ne veut rien dire. Les afficher
        // comme des chiffres invitait à les lire comme des marchés.
        Console.WriteLine("Prix moyens chez les acheteurs (× prix de référence, — = aucun acheteur)");
        Console.Write($"{"",-12}");
        foreach (string cargoId in w.CargoOrder)
            Console.Write($"{w.Cargo(cargoId).Name,12}");
        Console.WriteLine();

        foreach (var city in w.Cities)
        {
            Console.Write($"{city.Def.Name,-12}");
            foreach (string cargoId in w.CargoOrder)
            {
                var m = stats.Market(city.Id, cargoId);
                Console.Write($"{(m.HasDemand ? m.MeanRatio.ToString("0.00", Ci) : "—"),12}");
            }
            Console.WriteLine();
        }
        Console.WriteLine();

        // --- Le tableau qui décide si le prototype est concluant.
        Console.WriteLine("Santé du signal-prix (mesurée chez les acheteurs seulement)");
        Console.WriteLine($"  {"Marchandise",-13}{"acheteurs",10}{"écart moyen",13}{"au plafond",12}{"au plancher",13}  diagnostic");
        foreach (string cargoId in w.CargoOrder)
        {
            double spread = stats.MeanSpread(cargoId);
            double ceiling = stats.MeanCeilingFraction(w, cargoId);
            double floor = stats.MeanFloorFraction(w, cargoId);

            string diagnosis =
                ceiling > 0.5 ? "pénurie permanente — prix sans information" :
                floor > 0.5 ? "surabondance permanente — invendable" :
                spread < 1.5 ? "trop uniforme — rien à transporter" :
                spread > 12 ? "écart extrême — marchés déconnectés" :
                "exploitable";

            Console.WriteLine(
                $"  {w.Cargo(cargoId).Name,-13}{stats.BuyerCount(w, cargoId),10}" +
                $"{("×" + spread.ToString("0.0", Ci)),13}" +
                $"{(ceiling * 100).ToString("0", Ci) + " %",12}" +
                $"{(floor * 100).ToString("0", Ci) + " %",13}  {diagnosis}");
        }
        Console.WriteLine();

        // --- Le tableau qui décide si l'économie est encore vivante APRÈS s'être
        // installée. Le précédent dit s'il y a quelque chose à transporter ; celui-ci
        // dit si ce quelque chose change. Un écart de ×5 parfaitement immobile donne
        // une seule route à entretenir, et c'est une économie morte qui a l'air saine.
        Console.WriteLine("Mobilité de la dispersion (ce que le joueur doit rouvrir sa carte pour suivre)");
        Console.WriteLine($"  {"Marchandise",-13}{"écart moyen",13}{"volatilité",12}{"mobilité",11}{"tête change",13}{"tête dominante",16}");
        foreach (string cargoId in w.CargoOrder)
        {
            if (stats.MobilitySamples(cargoId) < 2)
            {
                Console.WriteLine($"  {w.Cargo(cargoId).Name,-13}{"—",13}   (moins de deux acheteurs)");
                continue;
            }

            Console.WriteLine(
                $"  {w.Cargo(cargoId).Name,-13}" +
                $"{("×" + stats.MeanSpread(cargoId).ToString("0.00", Ci)),13}" +
                $"{stats.SpreadVolatility(cargoId).ToString("0.000", Ci),12}" +
                $"{stats.SpreadMobility(cargoId).ToString("0.000", Ci),11}" +
                $"{(stats.LeaderChurn(cargoId) * 100).ToString("0.0", Ci) + " %",13}" +
                $"{(stats.LeaderDominance(w, cargoId) * 100).ToString("0", Ci) + " %",16}");
        }
        Console.WriteLine(
            $"  {"MOYENNE",-13}{"",13}{"",12}" +
            $"{stats.MeanSpreadMobility(w).ToString("0.000", Ci),11}" +
            $"{(stats.MeanLeaderChurn(w) * 100).ToString("0.0", Ci) + " %",13}");
        Console.WriteLine();

        Console.WriteLine("Utilisation moyenne des usines");
        foreach (var city in w.Cities)
            foreach (var industry in city.Industries)
            {
                double u = stats.MeanUtilization(city.Id, industry.Recipe.Id);
                string note = u < 0.15 ? "  ← à l'arrêt : intrants absents ou non rentable" : "";
                Console.WriteLine($"  {city.Def.Name,-12} {industry.Recipe.Name,-14} " +
                                  $"{(u * 100).ToString("0", Ci),4} %{note}");
            }
        Console.WriteLine();

        rotation.Print(w);

        var co = w.Company;
        Console.WriteLine("Compagnie");
        Console.WriteLine($"  Trésorerie           {co.Cash.ToString("N0", Ci),12}");
        Console.WriteLine($"  Recettes transport   {co.TotalHaulRevenue.ToString("N0", Ci),12}");
        Console.WriteLine($"  Achats de fret      -{co.TotalCargoPurchases.ToString("N0", Ci),12}");
        Console.WriteLine($"  Exploitation        -{co.TotalOperatingCost.ToString("N0", Ci),12}");
        // Ce que le relief a ajouté au coût kilométrique : l'écart entre ce qui a été
        // facturé et ce qu'auraient coûté les mêmes kilomètres à plat. C'est le seul
        // effet du relief sur l'économie tant qu'il n'entre dans aucune décision du
        // transporteur (docs/FINDINGS.md, « Relief et économie ensemble »).
        double flatCost = w.Trains.Sum(t => t.TotalKmTravelled * t.CostPerKm);
        if (w.Network is not null && flatCost > 0)
            Console.WriteLine($"    dont relief       -{(co.TotalOperatingCost - flatCost).ToString("N0", Ci),12}" +
                              $"   ({((co.TotalOperatingCost / flatCost - 1) * 100).ToString("0.0", Ci)} % du coût à plat)");
        Console.WriteLine($"  Résultat net         {co.NetProfit.ToString("N0", Ci),12}");
        // Quatrième flux de bilan-tresorerie. Négatif = la finance a prélevé au
        // transporteur ; c'est de l'argent qui n'est plus disponible pour le fret.
        if (Math.Abs(co.TotalFinanceFlow) > 0.005)
            Console.WriteLine($"  Flux financiers      {co.TotalFinanceFlow.ToString("N0", Ci),12}");
        double totalKm = w.Trains.Sum(t => t.TotalKmTravelled);
        Console.WriteLine($"  Kilomètres parcourus {totalKm.ToString("N0", Ci),12}");
        if (totalKm > 0)
            Console.WriteLine($"  Marge au kilomètre   {(co.NetProfit / totalKm).ToString("0.00", Ci),12}");
        Console.WriteLine();

        Console.WriteLine($"Trésorerie  {Sparkline(cashHistory)}");
        Console.WriteLine($"CSV         {Path.GetFullPath(opts.OutDir)}");
    }

    /// <summary>
    /// Courbe en une ligne. Ce n'est pas de la coquetterie : voir la forme de la
    /// trésorerie tout de suite, sans ouvrir un tableur, change la vitesse
    /// d'itération sur l'équilibrage.
    /// </summary>
    private static string Sparkline(List<double> values, int width = 60)
    {
        if (values.Count == 0) return "(vide)";

        var sampled = new List<double>(width);
        for (int i = 0; i < width; i++)
        {
            int index = (int)((long)i * (values.Count - 1) / Math.Max(1, width - 1));
            sampled.Add(values[index]);
        }

        double min = sampled.Min(), max = sampled.Max();
        double range = max - min;
        var chars = new char[width];
        for (int i = 0; i < width; i++)
        {
            int level = range <= 0 ? 0 : (int)((sampled[i] - min) / range * (Blocks.Length - 1));
            chars[i] = Blocks[Math.Clamp(level, 0, Blocks.Length - 1)];
        }
        return new string(chars) + $"  {min.ToString("N0", Ci)} → {max.ToString("N0", Ci)}";
    }

    /// <summary>
    /// Journal des événements déclenchés. Ce que le joueur verrait dans la gazette,
    /// et tout ce qu'un concurrent a le droit de savoir : ce qui a commencé, jamais
    /// ce qui va commencer.
    /// </summary>
    public static void PrintEvents(Simulation sim)
    {
        var w = sim.World;
        var events = w.Events;
        Console.WriteLine();
        if (!events.Enabled)
        {
            Console.WriteLine("Événements  module inactif (aucun bloc « events » dans le scénario)");
            return;
        }

        int active = events.Active.Count(e => e.IsActiveAt(w.Tick.Index));
        Console.WriteLine($"Événements (journal public, module {sim.Events.Name}) : " +
                          $"{events.Journal.Count} déclenché(s), {active} encore actif(s) au dernier tick");
        if (events.Journal.Count == 0) return;

        if (w.Def.Events.StartYear > 0)
            Console.WriteLine("  (dates du calendrier de jeu : douze mois de trente jours)");
        Console.WriteLine($"  {"début",-12}{"fin",-12}{"origine",-13}{"événement",-34}cibles (multiplicateur au plus fort)");

        // Les historiques s'affichent toujours ; les aléatoires jusqu'à une limite,
        // le reste est dans events.csv. Tronquer le journal dans l'ordre aurait
        // caché l'incendie de Chicago derrière quarante redoux.
        const int MaxRandomLines = 40;
        int randomShown = 0, randomHidden = 0;
        foreach (var e in events.Journal)
        {
            if (e.Origin == RailTycoon.Sim.Events.EventOrigin.Random && randomShown++ >= MaxRandomLines)
            {
                randomHidden++;
                continue;
            }
            string origin = e.Origin switch
            {
                RailTycoon.Sim.Events.EventOrigin.Historical => "historique",
                RailTycoon.Sim.Events.EventOrigin.Inspired => "inspiré de",
                _ => "aléatoire",
            };
            string targets = string.Join(", ", e.Targets.Select(t =>
                $"{w.CityById(t.CityId).Def.Name} {w.Cargo(t.Cargo).Name.ToLowerInvariant()} " +
                $"{(t.On == "production" ? "prod." : "dem.")} ×{t.PeakFactor.ToString("0.00", Ci)}"));
            if (targets.Length > 90) targets = targets[..87] + "...";
            Console.WriteLine($"  {Date(w, e.StartTick),-12}{Date(w, e.EndTick),-12}{origin,-13}" +
                              $"{Truncate(e.Name, 33),-34}{targets}");
        }
        if (randomHidden > 0)
            Console.WriteLine($"  … et {randomHidden} événement(s) aléatoire(s) de plus : voir events.csv");
        Console.WriteLine();
    }

    /// <summary>Date lisible d'un tick : calendrier de jeu (12 mois de 30 jours) si le scénario donne son année de départ.</summary>
    private static string Date(WorldState w, int tick)
    {
        int startYear = w.Def.Events.StartYear;
        var t = new RailTycoon.Sim.Core.SimTick(tick);
        if (startYear <= 0) return t.ToString();
        int month = t.DayOfYear / 30 + 1, day = t.DayOfYear % 30 + 1;
        return $"{day:D2}/{month:D2}/{startYear + t.Year}";
    }

    private static string Truncate(string text, int width)
        => text.Length <= width ? text : text[..(width - 1)] + "…";

    /// <summary>
    /// Plus grand écart de bilan, tous grands livres confondus. C'est le chiffre
    /// qui dit si la comptabilité tient : il doit valoir zéro, pas « très peu ».
    /// </summary>
    public static decimal WorstResidual(WorldState world)
    {
        if (!world.Finance.Enabled) return 0m;

        decimal worst = 0m;
        foreach (var company in world.Finance.Companies)
        {
            decimal residual = Math.Abs(company.Book.Residual);
            if (residual > worst) worst = residual;
        }
        if (world.Finance.Magnate is not null)
        {
            decimal residual = Math.Abs(world.Finance.Magnate.Book.Residual);
            if (residual > worst) worst = residual;
        }
        return worst;
    }

    public static void PrintFinance(Simulation sim, decimal worstResidual, int worstResidualTick,
        double worstFrontierGap)
    {
        var finance = sim.World.Finance;
        if (!finance.Enabled || finance.Player is null || finance.Magnate is null)
        {
            Console.WriteLine("Finance     module inactif (aucun bloc « finance » dans le scénario)");
            return;
        }

        var player = finance.Player;
        var tycoon = finance.Magnate;

        Console.WriteLine();
        Console.WriteLine($"Société — {player.Name}  (module {sim.Finance.Name})");
        Console.WriteLine($"  Caisse société       {player.Cash.ToString("N2", Ci),14}");
        Console.WriteLine($"  Matériel (net)       {player.Book[Accounts.FixedAssets].ToString("N2", Ci),14}");
        Console.WriteLine($"  Participations       {player.Book[Accounts.Investments].ToString("N2", Ci),14}");
        Console.WriteLine($"  Dette obligataire   -{player.Debt.ToString("N2", Ci),14}");
        Console.WriteLine($"  Découvert bancaire  -{player.OverdraftBalance.ToString("N2", Ci),14}" +
                          $"   (autorisé : {player.CreditFacility.ToString("N2", Ci)})");
        Console.WriteLine($"  Intérêts à payer    -{player.UnpaidInterest.ToString("N2", Ci),14}");
        Console.WriteLine($"  Capitaux propres     {player.BookEquity.ToString("N2", Ci),14}");
        Console.WriteLine($"  Cours de l'action    {player.SharePrice.ToString("N2", Ci),14}");
        Console.WriteLine($"  Capitalisation       {player.MarketCap.ToString("N2", Ci),14}");
        Console.WriteLine($"  Valeur d'entreprise  {player.EnterpriseValue.ToString("N2", Ci),14}");
        Console.WriteLine($"  Emprunté / remboursé {player.PrincipalIssuedTotal.ToString("N2", Ci),14}" +
                          $" / {player.PrincipalRepaidTotal.ToString("N2", Ci)}");
        Console.WriteLine($"  Intérêts payés       {player.InterestPaidTotal.ToString("N2", Ci),14}");
        Console.WriteLine($"  Intérêts de découvert{player.OverdraftInterestTotal.ToString("N2", Ci),14}");
        Console.WriteLine($"  Dividendes versés    {player.DividendsPaidTotal.ToString("N2", Ci),14}");
        Console.WriteLine($"  Capital appelé       {player.CapitalRaisedTotal.ToString("N2", Ci),14}" +
                          $"   ({player.SharesIssuedInRescues} actions nouvelles)");

        // Ligne à lire en premier quand une trésorerie part au rouge : elle dit si
        // la compagnie est encore financée, ou si le module a coupé les trains.
        string solvency = player.InReceivership
            ? $"sous administration depuis le tick {player.ReceivershipTick} — trains à l'arrêt"
            : player.OverdraftBalance > 0m
                ? "à découvert, dans la limite accordée"
                : "solvable";
        Console.WriteLine($"  Solvabilité          {solvency,14}" +
                          (player.ReceivershipCount > 0
                              ? $"   ({player.ReceivershipCount} mise(s) sous administration)"
                              : ""));

        Console.WriteLine();
        Console.WriteLine("Magnat");
        Console.WriteLine($"  Caisse personnelle   {tycoon.Cash.ToString("N2", Ci),14}");
        Console.WriteLine($"  Portefeuille (coût)  {tycoon.PortfolioCost.ToString("N2", Ci),14}");
        Console.WriteLine($"  Portefeuille (cours) {tycoon.PortfolioMarketValue(finance).ToString("N2", Ci),14}");
        Console.WriteLine($"  Dette de marge      -{tycoon.MarginLoan.ToString("N2", Ci),14}");
        Console.WriteLine($"  Fortune personnelle  {tycoon.NetWorth(finance).ToString("N2", Ci),14}");
        Console.WriteLine($"  Dividendes reçus     {tycoon.DividendsReceivedTotal.ToString("N2", Ci),14}");
        Console.WriteLine($"  Plus-values réalisées{tycoon.RealizedResultTotal.ToString("N2", Ci),14}");
        Console.WriteLine($"  Titres achetés/vendus{tycoon.SharesBoughtTotal,14} / {tycoon.SharesSoldTotal}" +
                          $"   appels de marge : {tycoon.MarginCalls}");
        Console.WriteLine($"  Part du capital      {(player.Register.HeldBy(Holders.Tycoon) * 100.0
            / Math.Max(1, player.Register.SharesIssued)).ToString("0.0", Ci),13} %");

        Console.WriteLine();
        Console.WriteLine("Concurrents");
        foreach (var rival in finance.Companies)
        {
            if (rival.Rival is null) continue;
            long stake = rival.Register.SharesIssued > 0
                ? rival.Register.HeldBy(player.Id)
                : 0;
            string state = rival.Merged ? "absorbée" :
                $"participation {stake} / {rival.Register.SharesIssued}";
            Console.WriteLine($"  {rival.Name,-24} cours {rival.SharePrice.ToString("N2", Ci),8}   " +
                              $"capitaux propres {rival.BookEquity.ToString("N2", Ci),12}   {state}");
        }

        foreach (var merger in finance.Mergers)
            Console.WriteLine($"  Fusion au tick {merger.Tick} : {merger.TargetId} absorbée, " +
                              $"actif net {merger.NetAssetsAbsorbed.ToString("N2", Ci)}, " +
                              $"résultat de fusion {merger.MergerResult.ToString("N2", Ci)}");

        Console.WriteLine();
        Console.WriteLine($"Écart de bilan maximal sur la course : {worstResidual.ToString("0.00######", Ci)}" +
                          (worstResidual == 0m
                              ? "   (tous les bilans équilibrés au centime, à chaque tick)"
                              : $"   ← FUITE, tick {worstResidualTick}"));
        Console.WriteLine($"Écart à la frontière double/decimal  : " +
                          $"{worstFrontierGap.ToString("0.00######", Ci)}" +
                          (worstFrontierGap <= 0.005 + 1e-9
                              ? "   (sous le demi-centime, et il ne s'accumule pas)"
                              : "   ← DÉRIVE de l'arrondi"));
    }
}

/// <summary>
/// Rotation du fret : chargements livrés rapportés aux chargements produits, par
/// marchandise, après la chauffe.
/// <para>
/// Un rapport de 1 veut dire que chaque chargement produit a fait un voyage. Bien
/// au-delà, le transporteur revend d'une ville à l'autre ce qu'il vient de livrer :
/// c'est légitime tant que chaque revente paie sa marge sur un vrai écart de prix,
/// mais c'est aussi la signature d'un fret qui tourne en rond — la famille du
/// lavage de fret. La campagne sur relief l'a relevé sur la nourriture (×70) et sur
/// le charbon d'un bloc d'anticipation mal réglé (×41).
/// </para>
/// </summary>
internal sealed class FreightRotation
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    // Jamais énumérés : l'affichage parcourt WorldState.CargoOrder.
    private readonly Dictionary<string, double> _delivered = new();
    private readonly Dictionary<string, double> _produced = new();

    public int WarmupTicks { get; init; }

    public void Sample(WorldState world)
    {
        if (world.Tick.Index < WarmupTicks) return;

        foreach (var city in world.Cities)
            foreach (var market in world.MarketsOf(city))
            {
                _delivered[market.CargoId] = _delivered.GetValueOrDefault(market.CargoId) + market.ImportedThisTick;
                _produced[market.CargoId] = _produced.GetValueOrDefault(market.CargoId) + market.ProducedThisTick;
            }
    }

    public void Print(WorldState world)
    {
        Console.WriteLine("Rotation du fret (après chauffe : chargements livrés ÷ produits)");
        foreach (string cargoId in world.CargoOrder)
        {
            double delivered = _delivered.GetValueOrDefault(cargoId);
            double produced = _produced.GetValueOrDefault(cargoId);
            string ratio = produced > 1e-9 ? "×" + (delivered / produced).ToString("0.0", Ci) : "—";
            Console.WriteLine($"  {world.Cargo(cargoId).Name,-13}{delivered.ToString("N0", Ci),10} livrés" +
                              $"{produced.ToString("N0", Ci),10} produits{ratio,9}");
        }
        Console.WriteLine();
    }
}
