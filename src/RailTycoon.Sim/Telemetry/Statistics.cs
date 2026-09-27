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

        SampleMobility(world);
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

    // ------------------------------------------------------------------------
    // Mobilité de la dispersion.
    //
    // MeanSpread répond à « y a-t-il quelque chose à transporter ». Il ne répond
    // pas à la question du contrat du module économie, qui est autre : est-ce que
    // ce quelque chose *change* ? Un écart moyen de ×5 parfaitement immobile — la
    // même ville la plus chère du premier au dernier tick — donne une seule route
    // à entretenir et plus aucune décision à prendre. C'est la forme la plus
    // coûteuse d'économie morte, parce qu'elle a l'air saine dans toutes les
    // statistiques existantes.
    //
    // Deux mesures, parce que la dispersion peut bouger de deux façons
    // indépendantes et qu'une seule des deux suffirait à se tromper :
    //   — son amplitude respire        → SpreadMobility
    //   — son point chaud se déplace   → LeaderChurn
    // Un modèle purement saisonnier fait monter la première sans toucher la
    // seconde : tout le monde a soif au même moment, la meilleure destination
    // reste la même. C'est LeaderChurn qui dit si le joueur doit rouvrir sa carte.
    // ------------------------------------------------------------------------

    private sealed class MobilityStats
    {
        public double SpreadSum;
        public double SpreadSquareSum;
        public int Samples;

        /// <summary>Ville la plus chère au tick précédent, null au premier échantillon.</summary>
        public string? Leader;

        public int LeaderChanges;
        public int Transitions;

        /// <summary>Ticks passés en tête, par ville. Jamais énuméré directement.</summary>
        public readonly Dictionary<string, int> TicksInLead = new();
    }

    private readonly Dictionary<string, MobilityStats> _mobility = new();

    /// <summary>
    /// Échantillonne la mobilité. Le calcul de l'écart est refait ici plutôt que
    /// partagé avec la boucle de <see cref="Sample"/> : le prix de quelques
    /// divisions par tick est sans importance devant celui d'une modification d'un
    /// chemin de mesure dont trois campagnes dépendent déjà.
    /// </summary>
    private void SampleMobility(WorldState world)
    {
        foreach (string cargoId in world.CargoOrder)
        {
            if (!_mobility.TryGetValue(cargoId, out var stats))
                _mobility[cargoId] = stats = new MobilityStats();

            // Comme pour l'écart moyen : seuls les marchés ayant de vrais
            // acheteurs comptent. Une ville qui ne consomme pas la marchandise est
            // au plancher par construction et serait un « moins cher » permanent
            // qui rendrait l'écart insensible à tout le reste.
            double min = double.MaxValue, max = 0;
            string? leader = null;
            int buyers = 0;

            foreach (var city in world.Cities)
            {
                var market = city.Market(cargoId);
                if (market.BaseDemandRate + market.IndustryDemandRate <= 0) continue;

                buyers++;
                if (market.Price < min) min = market.Price;
                // Comparaison stricte, et parcours dans l'ordre du scénario : à
                // prix égaux la tête ne change pas, ce qui évite de compter comme
                // mouvement un simple aléa d'énumération.
                if (market.Price > max) { max = market.Price; leader = city.Id; }
            }

            if (buyers <= 1) continue;

            double spread = min > 1e-9 ? max / min : 1.0;
            stats.SpreadSum += spread;
            stats.SpreadSquareSum += spread * spread;
            stats.Samples++;

            if (leader is not null)
            {
                stats.TicksInLead.TryGetValue(leader, out int ticks);
                stats.TicksInLead[leader] = ticks + 1;

                if (stats.Leader is not null)
                {
                    stats.Transitions++;
                    if (stats.Leader != leader) stats.LeaderChanges++;
                }
                stats.Leader = leader;
            }
        }
    }

    /// <summary>
    /// Écart-type de l'écart de prix instantané, au cours du temps. Mesure la
    /// respiration de la dispersion en unités d'écart.
    /// </summary>
    public double SpreadVolatility(string cargoId)
    {
        if (!_mobility.TryGetValue(cargoId, out var stats) || stats.Samples < 2) return 0;
        double mean = stats.SpreadSum / stats.Samples;
        double variance = stats.SpreadSquareSum / stats.Samples - mean * mean;
        return variance > 0 ? Math.Sqrt(variance) : 0;
    }

    /// <summary>
    /// Mobilité de l'amplitude : écart-type de l'écart de prix rapporté à sa
    /// moyenne.
    /// <para>
    /// Le rapport est nécessaire pour comparer des marchandises entre elles. Le
    /// charbon du scénario de référence vit autour d'un écart de ×11, la farine
    /// autour de ×1,4 : à volatilité absolue égale, la seconde est bien plus
    /// vivante que la première. Une moyenne de volatilités absolues ne mesurerait
    /// que le charbon.
    /// </para>
    /// </summary>
    public double SpreadMobility(string cargoId)
    {
        if (!_mobility.TryGetValue(cargoId, out var stats) || stats.Samples < 2) return 0;
        double mean = stats.SpreadSum / stats.Samples;
        return mean > 1e-9 ? SpreadVolatility(cargoId) / mean : 0;
    }

    /// <summary>
    /// Taux de changement de la ville la plus chère : fraction des ticks où le
    /// meilleur débouché n'est plus celui du tick précédent.
    /// <para>
    /// C'est la mesure la plus proche de la décision du joueur. À 0, une seule
    /// route est à entretenir pour toute la partie. Très haut, la tête change si
    /// souvent qu'aucune décision ne survit au temps de trajet, ce qui n'est pas
    /// mieux : le bon régime est intermédiaire, et se juge en jouant.
    /// </para>
    /// </summary>
    public double LeaderChurn(string cargoId)
        => _mobility.TryGetValue(cargoId, out var stats) && stats.Transitions > 0
            ? (double)stats.LeaderChanges / stats.Transitions
            : 0;

    /// <summary>
    /// Part du temps détenue par la ville la plus souvent en tête. 1 = un débouché
    /// unique et permanent ; proche de 1/nombre d'acheteurs = la tête tourne entre
    /// toutes les villes.
    /// </summary>
    public double LeaderDominance(WorldState world, string cargoId)
    {
        if (!_mobility.TryGetValue(cargoId, out var stats) || stats.Samples == 0) return 0;

        // Parcours des villes dans l'ordre du scénario, et non du dictionnaire :
        // à égalité de ticks en tête, le résultat doit être le même d'une
        // exécution à l'autre.
        int best = 0;
        foreach (var city in world.Cities)
            if (stats.TicksInLead.TryGetValue(city.Id, out int ticks) && ticks > best)
                best = ticks;

        return (double)best / stats.Samples;
    }

    /// <summary>Nombre de ticks pendant lesquels cette marchandise avait au moins deux acheteurs.</summary>
    public int MobilitySamples(string cargoId)
        => _mobility.TryGetValue(cargoId, out var stats) ? stats.Samples : 0;

    /// <summary>
    /// Mobilité d'amplitude moyenne, sur les marchandises ayant plusieurs
    /// acheteurs. C'est le chiffre unique qu'on compare entre deux solveurs.
    /// </summary>
    public double MeanSpreadMobility(WorldState world) => MeanOverCargos(world, SpreadMobility);

    /// <summary>Taux de changement de tête moyen, sur les marchandises ayant plusieurs acheteurs.</summary>
    public double MeanLeaderChurn(WorldState world) => MeanOverCargos(world, LeaderChurn);

    private double MeanOverCargos(WorldState world, Func<string, double> select)
    {
        double sum = 0;
        int count = 0;
        foreach (string cargoId in world.CargoOrder)
        {
            // Une marchandise à acheteur unique n'a pas de dispersion : l'inclure
            // à zéro pénaliserait un scénario simplement parce qu'il compte peu de
            // villes consommatrices.
            if (MobilitySamples(cargoId) < 2) continue;
            sum += select(cargoId);
            count++;
        }
        return count > 0 ? sum / count : 0;
    }
}
