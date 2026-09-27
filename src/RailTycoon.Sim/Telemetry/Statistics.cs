namespace RailTycoon.Sim.Telemetry;

/// <summary>
/// Statistiques accumulées sur toute la durée d'une exécution.
/// <para>
/// Leur raison d'être : un instantané du dernier tick ne dit rien d'utile. Dans
/// cette économie, un marché desservi par un train oscille en dents de scie entre
/// « plein » juste après le passage et « vide » juste avant le suivant. Lire un
/// seul tick, c'est tomber au hasard sur une crête ou un creux et en tirer une
/// conclusion fausse.
/// </para>
/// <para>
/// La mesure qui décide si l'économie est vivante est
/// <see cref="MeanSpread"/> : l'écart moyen de prix entre la ville la moins
/// chère et la plus chère, tick après tick. S'il est proche de 1, il n'y a rien à
/// transporter. <see cref="CeilingFraction"/> complète le diagnostic : un marché
/// passé au plafond 90 % du temps n'a plus de prix, il a une constante.
/// </para>
/// </summary>
public sealed class RunStatistics
{
    public sealed class MarketStats
    {
        /// <summary>
        /// Vrai si ce marché a de vrais acheteurs — habitants ou usines locales.
        /// <para>
        /// Sans cette distinction les statistiques mentent. Une ville qui ne
        /// consomme pas une marchandise l'affiche au prix plancher en permanence,
        /// par construction et non par surabondance. En comptant ces marchés
        /// fictifs, la première version des mesures annonçait « blé au plancher
        /// 71 % du temps » alors que le blé s'écoulait normalement : les sept
        /// villes qui n'en consomment pas tiraient la moyenne à elles.
        /// </para>
        /// </summary>
        public bool HasDemand;

        public double RatioSum;
        public double MinRatio = double.MaxValue;
        public double MaxRatio;
        public double StockSum;
        public double CoverageSum;
        public int Samples;
        public int AtCeiling;
        public int AtFloor;

        public double MeanRatio => Samples > 0 ? RatioSum / Samples : 0;
        public double MeanStock => Samples > 0 ? StockSum / Samples : 0;
        public double MeanCoverage => Samples > 0 ? CoverageSum / Samples : 0;
        public double CeilingFraction => Samples > 0 ? (double)AtCeiling / Samples : 0;
        public double FloorFraction => Samples > 0 ? (double)AtFloor / Samples : 0;
    }

    private readonly Dictionary<string, Dictionary<string, MarketStats>> _markets = new();
    private readonly Dictionary<string, double> _spreadSum = new();
    private readonly Dictionary<string, double> _utilizationSum = new();
    private int _samples;
    private int _industrySamples;

    public int Samples => _samples;

    /// <summary>Ticks ignorés en début d'exécution, le temps que le régime s'établisse.</summary>
    public int WarmupTicks { get; init; }

    public void Sample(WorldState world)
    {
        if (world.Tick.Index < WarmupTicks) return;

        _samples++;
        double ceiling = world.Def.PriceModel.MaxMultiplier;
        double floor = world.Def.PriceModel.MinMultiplier;

        foreach (var city in world.Cities)
        {
            if (!_markets.TryGetValue(city.Id, out var byCargo))
                _markets[city.Id] = byCargo = new Dictionary<string, MarketStats>();

            foreach (var market in world.MarketsOf(city))
            {
                if (!byCargo.TryGetValue(market.CargoId, out var stats))
                    byCargo[market.CargoId] = stats = new MarketStats();

                stats.HasDemand = market.BaseDemandRate + market.IndustryDemandRate > 0;

                double ratio = market.Price / world.Cargo(market.CargoId).BasePrice;
                stats.RatioSum += ratio;
                stats.Samples++;
                if (ratio < stats.MinRatio) stats.MinRatio = ratio;
                if (ratio > stats.MaxRatio) stats.MaxRatio = ratio;
                stats.StockSum += market.Stock;
                stats.CoverageSum += world.PriceModel.Coverage(market);
                if (ratio >= ceiling - 1e-6) stats.AtCeiling++;
                if (ratio <= floor + 1e-6) stats.AtFloor++;
            }

            foreach (var industry in city.Industries)
            {
                string key = city.Id + "/" + industry.Recipe.Id;
                _utilizationSum.TryGetValue(key, out double sum);
                _utilizationSum[key] = sum + industry.Utilization;
            }
        }
        _industrySamples++;

        // Écart de prix instantané entre acheteurs, par marchandise. Accumulé tick
        // par tick plutôt que calculé à la fin : la moyenne des écarts et l'écart
        // des moyennes sont deux choses différentes, et c'est la première qui dit
        // s'il y avait quelque chose à transporter à chaque instant.
        //
        // Seuls les marchés ayant de vrais acheteurs comptent. Comparer le prix
        // chez un acheteur au prix plancher d'une ville qui ne consomme pas cette
        // marchandise ne mesure rien : cet écart existe même quand l'économie
        // fonctionne parfaitement.
        foreach (string cargoId in world.CargoOrder)
        {
            double min = double.MaxValue, max = 0;
            int buyers = 0;
            foreach (var city in world.Cities)
            {
                var market = city.Market(cargoId);
                if (market.BaseDemandRate + market.IndustryDemandRate <= 0) continue;

                buyers++;
                if (market.Price < min) min = market.Price;
                if (market.Price > max) max = market.Price;
            }

            double spread = buyers > 1 && min > 1e-9 ? max / min : 1.0;
            _spreadSum.TryGetValue(cargoId, out double sum);
            _spreadSum[cargoId] = sum + spread;
        }
    }

    public MarketStats Market(string cityId, string cargoId) => _markets[cityId][cargoId];

    /// <summary>Écart de prix moyen entre la ville la moins chère et la plus chère.</summary>
    public double MeanSpread(string cargoId)
        => _samples > 0 && _spreadSum.TryGetValue(cargoId, out double sum) ? sum / _samples : 1.0;

    public double MeanUtilization(string cityId, string recipeId)
        => _industrySamples > 0 && _utilizationSum.TryGetValue(cityId + "/" + recipeId, out double sum)
            ? sum / _industrySamples
            : 0;

    /// <summary>Fraction du temps où ce marché est resté collé au plafond de prix.</summary>
    public double CeilingFraction(string cityId, string cargoId) => Market(cityId, cargoId).CeilingFraction;

    /// <summary>Fraction moyenne du temps passé au plafond, chez les acheteurs réels.</summary>
    public double MeanCeilingFraction(WorldState world, string cargoId)
        => MeanOverBuyers(world, cargoId, s => s.CeilingFraction);

    /// <summary>Fraction moyenne du temps passé au plancher, chez les acheteurs réels.</summary>
    public double MeanFloorFraction(WorldState world, string cargoId)
        => MeanOverBuyers(world, cargoId, s => s.FloorFraction);

    /// <summary>Nombre de villes qui consomment réellement cette marchandise.</summary>
    public int BuyerCount(WorldState world, string cargoId)
    {
        int count = 0;
        foreach (var city in world.Cities)
            if (Market(city.Id, cargoId).HasDemand) count++;
        return count;
    }

    private double MeanOverBuyers(WorldState world, string cargoId, Func<MarketStats, double> select)
    {
        double sum = 0;
        int count = 0;
        foreach (var city in world.Cities)
        {
            var stats = Market(city.Id, cargoId);
            if (!stats.HasDemand) continue;
            sum += select(stats);
            count++;
        }
        return count > 0 ? sum / count : 0;
    }
}
