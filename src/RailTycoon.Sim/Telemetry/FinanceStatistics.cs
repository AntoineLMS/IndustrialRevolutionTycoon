using RailTycoon.Sim.Finance;

namespace RailTycoon.Sim.Telemetry;

/// <summary>
/// Statistiques des grandeurs financières sur toute la durée d'une exécution, après
/// la chauffe : l'équivalent, pour la bourse, de <see cref="RunStatistics"/> pour les
/// marchés.
/// <para>
/// <b>Pourquoi.</b> C'est la dette connue n° 2 du module finance. Le harnais
/// n'affichait que la fortune du magnat au dernier tick, or un tick sur trente est un
/// tick d'ordre de bourse, et le cours y porte encore l'impact que l'ordre vient de
/// produire : l'erreur d'instantané final que docs/FINDINGS.md documente pour les
/// marchés, transposée à la bourse. Une moyenne dans le temps, un écart-type, et le
/// plus grand saut d'un soir au suivant disent ce qu'un instantané ne dit pas.
/// </para>
/// <para>
/// Les sommes sont tenues en <c>double</c> : ce sont des statistiques, pas des soldes,
/// et personne ne les passe au grand livre.
/// </para>
/// </summary>
public sealed class FinanceStatistics
{
    /// <summary>Moyenne, écart-type, bornes d'une série.</summary>
    public sealed class Series
    {
        private double _sum, _sumSq;
        public int Samples { get; private set; }
        public double Min { get; private set; } = double.MaxValue;
        public double Max { get; private set; } = double.MinValue;

        public void Add(double value)
        {
            _sum += value;
            _sumSq += value * value;
            Samples++;
            if (value < Min) Min = value;
            if (value > Max) Max = value;
        }

        public double Mean => Samples > 0 ? _sum / Samples : 0;

        public double StdDev
        {
            get
            {
                if (Samples < 2) return 0;
                double mean = Mean;
                double variance = (_sumSq - Samples * mean * mean) / (Samples - 1);
                return variance > 0 ? Math.Sqrt(variance) : 0;
            }
        }
    }

    /// <summary>Ticks ignorés en début d'exécution, le temps que le régime s'établisse.</summary>
    public int WarmupTicks { get; init; }

    /// <summary>Cours de la compagnie du joueur, en actions d'origine (multiplié par le produit des divisions).</summary>
    public Series SharePrice { get; } = new();

    public Series NetWorth { get; } = new();
    public Series MarginLoan { get; } = new();

    /// <summary>Soirs où le magnat était ruiné.</summary>
    public int RuinedTicks { get; private set; }

    /// <summary>Plus grand saut de la fortune d'un soir au suivant, en valeur absolue, et le tick où il a eu lieu.</summary>
    public double LargestJump { get; private set; }
    public int LargestJumpTick { get; private set; } = -1;

    /// <summary>Plus grand saut relatif du cours d'un soir au suivant.</summary>
    public double LargestPriceMove { get; private set; }

    private double? _lastWorth;
    private double? _lastPrice;

    public void Sample(WorldState world)
    {
        var finance = world.Finance;
        if (!finance.Enabled || finance.Player is null || finance.Magnate is null) return;

        double worth = (double)finance.Magnate.NetWorth(finance);
        // Le cours en actions d'origine : une division d'actions n'est pas une baisse.
        double price = (double)(finance.Player.SharePrice * finance.Player.SplitFactor);
        if (world.Tick.Index >= WarmupTicks)
        {
            SharePrice.Add(price);
            NetWorth.Add(worth);
            MarginLoan.Add((double)finance.Magnate.MarginLoan);
            if (finance.Magnate.Ruined) RuinedTicks++;

            if (_lastWorth is double previous && Math.Abs(worth - previous) > LargestJump)
            {
                LargestJump = Math.Abs(worth - previous);
                LargestJumpTick = world.Tick.Index;
            }
            if (_lastPrice is double before && before > 0 && Math.Abs(price / before - 1) > LargestPriceMove)
                LargestPriceMove = Math.Abs(price / before - 1);
        }
        _lastWorth = worth;
        _lastPrice = price;
    }

    public int Samples => NetWorth.Samples;
}
