using System.Globalization;
using RailTycoon.Sim;
using RailTycoon.Sim.Telemetry;

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

        var cashHistory = new List<double>();
        var violations = new List<(int Tick, Invariants.Violation V)>();

        recorder.Record(sim.World);
        for (int i = 0; i < opts.Ticks; i++)
        {
            sim.Step();
            recorder.Record(sim.World);
            stats.Sample(sim.World);
            cashHistory.Add(sim.World.Company.Cash);

            // On s'arrête au premier tick fautif : les violations en cascade
            // masquent la cause initiale, qui est la seule intéressante.
            if (violations.Count == 0)
            {
                foreach (var v in Invariants.Check(sim.World, initialStock))
                    violations.Add((sim.World.Tick.Index, v));
            }
        }

        recorder.WriteTo(opts.OutDir);
        Report.PrintBalance(scenario);
        Report.PrintRun(sim, opts, recorder, stats, cashHistory);

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
    }

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
        RunStatistics stats, List<double> cashHistory)
    {
        var w = sim.World;

        Console.WriteLine($"Scénario   : {w.Def.Name}  ({w.Def.Id}, graine {w.Def.Seed})");
        Console.WriteLine($"Modules    : économie={sim.Economy.Name}, transport={sim.Haulage.Name}, prix={w.PriceModel.Name}");
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

        var co = w.Company;
        Console.WriteLine("Compagnie");
        Console.WriteLine($"  Trésorerie           {co.Cash.ToString("N0", Ci),12}");
        Console.WriteLine($"  Recettes transport   {co.TotalHaulRevenue.ToString("N0", Ci),12}");
        Console.WriteLine($"  Achats de fret      -{co.TotalCargoPurchases.ToString("N0", Ci),12}");
        Console.WriteLine($"  Exploitation        -{co.TotalOperatingCost.ToString("N0", Ci),12}");
        Console.WriteLine($"  Résultat net         {co.NetProfit.ToString("N0", Ci),12}");
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
}
