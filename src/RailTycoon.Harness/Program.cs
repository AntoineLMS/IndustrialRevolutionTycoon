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

        if (opts.BalanceOnly)
        {
            Report.PrintBalance(scenario);
            return 0;
        }

        var sim = new Simulation(scenario);
        double initialStock = Invariants.InitialStockTotal(sim.World);
        var recorder = new CsvRecorder { Every = opts.RecordEvery };
        var stats = new RunStatistics { WarmupTicks = opts.WarmupTicks };

        var cashHistory = new List<double>();
        var violations = new List<(int Tick, Invariants.Violation V)>();

        // Pire écart de bilan observé sur toute la course. Il doit valoir zéro
        // exactement : la comptabilité est tenue en decimal et en partie double,
        // donc un écart d'un centime est un bug et non un résidu de calcul.
        decimal worstResidual = 0m;
        int worstResidualTick = 0;

        recorder.Record(sim.World);
        for (int i = 0; i < opts.Ticks; i++)
        {
            sim.Step();
            recorder.Record(sim.World);
            stats.Sample(sim.World);
            cashHistory.Add(sim.World.Company.Cash);

            decimal residual = Report.WorstResidual(sim.World);
            if (residual > worstResidual)
            {
                worstResidual = residual;
                worstResidualTick = sim.World.Tick.Index;
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
        Report.PrintBalance(scenario);
        Report.PrintRun(sim, opts, recorder, stats, cashHistory);
        Report.PrintFinance(sim, worstResidual, worstResidualTick);

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
}

internal sealed class Options
{
    public string ScenarioPath = Path.Combine("data", "heartland.json");
    public int Ticks = 720;
    public string OutDir = "out";
    public int RecordEvery = 1;
    public int WarmupTicks = 90;
    public bool BalanceOnly;
    public bool ShowHelp;

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

    public static void PrintFinance(Simulation sim, decimal worstResidual, int worstResidualTick)
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
    }
}
