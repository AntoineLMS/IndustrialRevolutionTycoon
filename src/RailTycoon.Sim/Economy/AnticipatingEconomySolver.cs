using RailTycoon.Sim.Core;

namespace RailTycoon.Sim.Economy;

/// <summary>
/// Solveur économique à trois rétroactions, conçu contre un défaut précis de
/// <see cref="ReferenceEconomySolver"/> : la référence converge. Ses prix
/// s'installent dans un régime stationnaire où la ville la plus chère d'une
/// marchandise est toujours la même, et le joueur n'a plus qu'une route à
/// entretenir.
/// <para>
/// Les trois mécanismes agissent à trois échelles de temps différentes, et c'est
/// intentionnel : superposés, ils n'ont pas de point fixe commun.
/// </para>
/// <list type="number">
///   <item><b>Anticipation</b> (quelques ticks) — le prix dépend de la pente du
///   stock autant que de son niveau. Terme dérivé : il fait dépasser la cible,
///   donc osciller. <em>C'est le seul mécanisme qui ait tenu ses promesses à la
///   mesure</em> : mobilité de la dispersion 0,411 → 0,615, changement de la ville
///   la plus chère 20,0 % → 26,2 %.</item>
///   <item><b>Saisonnalité</b> (l'année) — récoltes et chauffage, fonction du tick
///   seul. Elle déplace la cible au lieu de la laisser fixe, mais toutes les villes
///   en même temps : +6 % de mobilité, et rien du tout sur le changement de ville la
///   plus chère.</item>
///   <item><b>Croissance</b> (plusieurs années) — implémentée, mesurée,
///   <em>désactivée</em> dans le scénario de référence. Elle fait l'inverse de ce
///   qu'on attendait ; voir <see cref="UpdateCitySizes"/>.</item>
/// </list>
/// <para>
/// Ce que ce solveur ne fait pas, et ne doit pas faire : créer ou détruire de la
/// marchandise autrement que par <see cref="Market.Produce"/> et
/// <see cref="Market.Consume"/>, toucher à la trésorerie, tirer un nombre au
/// hasard. La saison et la croissance sont entièrement déterminées par le tick et
/// par l'état des marchés. L'aléa des événements est tiré par le module events,
/// sur sa propre séquence, et n'arrive ici que sous forme d'un multiplicateur.
/// </para>
/// </summary>
public sealed class AnticipatingEconomySolver : IEconomySolver
{
    public string Name => "anticipating";

    /// <summary>
    /// Mémoire d'un marché. Les taux de base du <see cref="Market"/> deviennent
    /// variables (saison, croissance), il faut donc garder ailleurs la valeur
    /// nominale du scénario : c'est elle qui sert de référence à toutes les
    /// modulations, sans quoi les facteurs se composeraient d'un tick au suivant
    /// et dériveraient sans retour.
    /// </summary>
    private sealed class MarketState
    {
        public required Market Market { get; init; }
        public required CargoDef Cargo { get; init; }

        public double NominalDemand;
        public double NominalProduction;

        public double ProductionAmplitude;
        public double ProductionPeak;
        public double DemandAmplitude;
        public double DemandPeak;

        /// <summary>Stock au début du tick précédent, pour mesurer la pente.</summary>
        public double LastStock;

        /// <summary>Pente lissée du stock, en chargements par tick.</summary>
        public double Drift;
    }

    private sealed class CityState
    {
        public required City City { get; init; }

        /// <summary>Taille relative à celle du scénario. 1 = telle que conçue.</summary>
        public double Size = 1.0;

        /// <summary>
        /// Somme des demandes nominales des habitants. Sert de poids à la
        /// renormalisation : ce qu'on veut conserver est la demande totale de la
        /// carte, pas le nombre de villes.
        /// </summary>
        public double DemandWeight;

        /// <summary>Avantage de prix lissé sur la mémoire démographique.</summary>
        public double Advantage;

        public readonly List<MarketState> Markets = new();
    }

    // Listes et non dictionnaires : l'ordre de parcours influe sur les prix, donc
    // sur le résultat de la partie. Les marchés y sont dans l'ordre de
    // WorldState.CargoOrder, figé à l'initialisation.
    private readonly List<CityState> _cities = new();

    /// <summary>
    /// Prix moyen chez les acheteurs, par marchandise, indexé dans l'ordre de
    /// <see cref="WorldState.CargoOrder"/>. Recalculé à chaque tick pour servir de
    /// point de comparaison à la croissance des villes.
    /// </summary>
    private double[] _cargoMeanPrice = Array.Empty<double>();

    /// <summary>
    /// Demande totale des habitants telle que le scénario la déclare. La
    /// croissance des villes est renormalisée dessus à chaque tick.
    /// </summary>
    private double _nominalDemandWeight;

    /// <summary>
    /// Marché fictif servant à interroger le modèle de prix sur un stock projeté.
    /// Réutilisé d'un appel à l'autre : il n'appartient pas au monde, n'entre dans
    /// aucun bilan matière, et sa seule raison d'être est de ne pas dupliquer ici
    /// la formule de <see cref="IPriceModel"/> — qu'on veut pouvoir remplacer sans
    /// avoir à corriger ce solveur.
    /// </summary>
    private readonly Market _probe = new() { CargoId = "probe", CityId = "probe" };

    private AnticipatingEconomyDef _cfg = new();

    public void Initialize(WorldState world)
    {
        _cfg = world.Def.Anticipating;
        _cities.Clear();

        foreach (var city in world.Cities)
        {
            var state = new CityState { City = city };
            foreach (var market in world.MarketsOf(city))
            {
                var season = FindSeason(_cfg, market.CargoId);
                state.Markets.Add(new MarketState
                {
                    Market = market,
                    Cargo = world.Cargo(market.CargoId),
                    NominalDemand = market.BaseDemandRate,
                    NominalProduction = market.BaseProductionRate,
                    ProductionAmplitude = SafeAmplitude(season?.ProductionAmplitude ?? 0),
                    ProductionPeak = season?.ProductionPeak ?? 0,
                    DemandAmplitude = SafeAmplitude(season?.DemandAmplitude ?? 0),
                    DemandPeak = season?.DemandPeak ?? 0,
                    LastStock = market.Stock,
                });
            }
            foreach (var ms in state.Markets) state.DemandWeight += ms.NominalDemand;
            _cities.Add(state);
        }

        _cargoMeanPrice = new double[world.CargoOrder.Count];
        _nominalDemandWeight = 0;
        foreach (var city in _cities) _nominalDemandWeight += city.DemandWeight;

        RecomputePrices(world);
    }

    public void Step(WorldState world, SimTick tick)
    {
        // Ce préambule ne touche aucun stock et ne déplace aucun argent : il fixe
        // les taux que les quatre phases vont ensuite utiliser. L'ordre des phases
        // lui-même est inchangé, et c'est le contrat.
        UpdateDrift();
        UpdateCitySizes();
        ApplyRates(tick);

        ProducePrimary(world);
        RunIndustries(world);
        ConsumeDemand(world);
        RecomputePrices(world);
    }

    /// <summary>
    /// Pente du stock, mesurée entre deux débuts de tick. La fenêtre est choisie
    /// ainsi pour englober le passage des trains : ce qui informe un acheteur,
    /// c'est le bilan complet de la journée écoulée — production, consommation et
    /// livraisons — pas la seule saignée de la consommation locale.
    /// </summary>
    private void UpdateDrift()
    {
        double smoothing = Maths.Clamp(_cfg.DriftSmoothing, 0.0, 1.0);

        foreach (var city in _cities)
            foreach (var ms in city.Markets)
            {
                double delta = ms.Market.Stock - ms.LastStock;
                ms.LastStock = ms.Market.Stock;
                ms.Drift += smoothing * (delta - ms.Drift);
            }
    }

    /// <summary>
    /// Croissance et déclin des villes selon leur desserte. Une ville mieux
    /// approvisionnée que les autres attire des habitants ; une ville plus mal
    /// servie que la moyenne en perd.
    /// <para>
    /// <b>Ce mécanisme ne fait pas ce qu'on espérait, et la mesure est nette.</b> On
    /// attendait un cycle : une ville bien desservie grandit, redevient affamée, et
    /// le meilleur débouché se déplace. C'est l'inverse qui se produit. Le signal qui
    /// fait grandir une ville est qu'elle soit <em>moins chère</em> que les autres ;
    /// la croissance déplace donc la demande des villes chères vers les villes bon
    /// marché, ce qui <em>comprime</em> la dispersion par construction. C'est une
    /// rétroaction négative, et l'intuition d'un cycle était fausse.
    /// </para>
    /// <para>
    /// Mesuré à 0,31–0,43 de mobilité contre 0,411 pour la référence, pour tous les
    /// rythmes de 0,003 à 0,2 par tick et toutes les mémoires démographiques de 1 à
    /// 180 ticks — y compris celles censées introduire assez de retard pour
    /// déclencher un cycle. Le taux de changement de la ville la plus chère ne bouge
    /// pas (18,8 à 21,0 % contre 20,0 %), et la fraction de temps au plafond monte de
    /// 23 % à 28-31 %. Le mécanisme reste ici parce qu'il est correct et mesuré, à
    /// <c>growthRatePerTick = 0</c> dans le scénario de référence. Le rendre utile
    /// demande une <em>autre</em> boucle de croissance — par exemple assise sur le
    /// volume réellement consommé plutôt que sur le prix — pas un autre réglage de
    /// celle-ci.
    /// </para>
    /// <para>
    /// La comparaison se fait au <b>prix moyen des autres acheteurs</b>, et non au
    /// prix de référence de la marchandise. La première version comparait à la
    /// référence et s'est effondrée : le prix d'équilibre du charbon et des
    /// planches est structurellement bien au-dessus de leur référence — c'est de la
    /// géographie économique, pas un défaut de desserte — donc <em>toutes</em> les
    /// villes se jugeaient mal servies, toutes rétrécissaient contre leur borne
    /// basse, et le résultat net du transport passait sous zéro faute de demande à
    /// servir. Le signal relatif est mécaniquement de somme nulle : la croissance
    /// redistribue la demande au lieu d'en créer ou d'en détruire, ce qui préserve
    /// le bilan offre/demande du scénario.
    /// </para>
    /// <para>
    /// Seule la demande des habitants compte dans le signal. La demande des usines
    /// est une donnée d'implantation : un moulin ne déménage pas parce que le blé
    /// a monté, et l'inclure ferait juger la ville sur des intrants dont ses
    /// habitants ne voient jamais le prix.
    /// </para>
    /// </summary>
    private void UpdateCitySizes()
    {
        if (_cfg.GrowthRatePerTick <= 0) return;

        // Prix moyen chez les acheteurs, marchandise par marchandise. L'ensemble
        // des acheteurs est défini par la demande nominale et non par la demande du
        // jour : une saison ne doit pas faire entrer ou sortir une ville du panel
        // de comparaison au milieu de l'année.
        for (int k = 0; k < _cargoMeanPrice.Length; k++)
        {
            double sum = 0;
            int buyers = 0;
            foreach (var city in _cities)
            {
                var ms = city.Markets[k];
                if (ms.NominalDemand <= 0) continue;
                sum += ms.Market.Price;
                buyers++;
            }
            _cargoMeanPrice[k] = buyers > 0 ? sum / buyers : 0;
        }

        foreach (var city in _cities)
        {
            double advantage = 0;
            int counted = 0;

            for (int k = 0; k < city.Markets.Count; k++)
            {
                var ms = city.Markets[k];
                if (ms.NominalDemand <= 0) continue;
                if (ms.Market.Price <= 0 || _cargoMeanPrice[k] <= 0) continue;

                // Écart relatif au prix moyen, et non rapport des deux. La somme
                // des écarts sur les acheteurs d'une marchandise est exactement
                // nulle, celle des rapports ne l'est pas : un rapport est borné à
                // 0 par le bas et pas par le haut, et cette asymétrie suffisait à
                // faire grandir toutes les villes à la fois jusqu'à leur borne. La
                // pénurie générale qui s'ensuivait collait 45 % des prix au plafond
                // et faisait passer le transport à perte.
                advantage += 1.0 - ms.Market.Price / _cargoMeanPrice[k];
                counted++;
            }
            if (counted == 0) continue;

            double memory = Math.Max(1.0, _cfg.GrowthMemoryTicks);
            city.Advantage += (advantage / counted - city.Advantage) / memory;

            double push = city.Advantage;
            if (Math.Abs(push) <= _cfg.GrowthDeadband) continue;
            push -= Math.Sign(push) * _cfg.GrowthDeadband;

            // Incrément additif et non multiplicatif. Un écart de somme nulle
            // appliqué en pourcentage ne l'est plus : log(1+x) étant concave, la
            // taille moyenne dérive vers le bas à chaque tick. Sur 720 ticks cela
            // faisait fondre la demande totale de la carte de 15 % — un scénario
            // silencieusement plus mou que celui qu'on a équilibré, et dont la
            // dispersion de prix flatteuse ne venait pas du modèle mais du
            // desserrement.
            city.Size = Maths.Clamp(
                city.Size + _cfg.GrowthRatePerTick * push,
                _cfg.MinCitySize,
                _cfg.MaxCitySize);
        }

        NormalizeCitySizes();
    }

    /// <summary>
    /// Ramène la demande totale des habitants à celle du scénario. La croissance
    /// <em>déplace</em> la population, elle n'en crée pas.
    /// <para>
    /// Sans cette étape, la croissance faisait fondre la demande de la carte de
    /// 9 %. L'écart de somme nulle l'est ville par ville, pas chargement par
    /// chargement : les grandes villes sont précisément celles qu'on approvisionne
    /// le plus mal — c'est ce qui les rend chères — donc ce sont elles qui
    /// rétrécissaient, et leur poids dans la demande totale est bien plus lourd que
    /// celui des bourgs qui grossissaient à leur place. La carte devenait
    /// silencieusement plus molle que celle qu'on a équilibrée, la marge kilométrique
    /// grimpait de 0,93 à 1,31, et cette embellie n'était due à aucun modèle : juste
    /// à un scénario desserré.
    /// </para>
    /// </summary>
    private void NormalizeCitySizes()
    {
        if (_nominalDemandWeight <= 0) return;

        double current = 0;
        foreach (var city in _cities) current += city.Size * city.DemandWeight;
        if (current <= 0) return;

        double correction = _nominalDemandWeight / current;
        foreach (var city in _cities)
            city.Size = Maths.Clamp(city.Size * correction, _cfg.MinCitySize, _cfg.MaxCitySize);
    }

    /// <summary>
    /// Recompose les taux du tick : la demande des habitants subit la taille de la
    /// ville et sa saison, la production primaire sa propre saison, et toutes deux
    /// le multiplicateur que le module events a publié sur le marché (phase 0b).
    /// Ce multiplicateur vaut 1 exactement sans événement, ce qui laisse le produit
    /// identique au bit près : la trace de ce solveur n'a pas bougé à l'arrivée du
    /// module. La montée d'un événement, sa cible, son tirage ne sont pas l'affaire
    /// de ce solveur ; il en lit le résultat, comme la référence. La demande subit
    /// en plus le multiplicateur de la conjoncture (phase 0c), qui vaut lui aussi 1
    /// exactement sans cycle.
    /// <para>
    /// La production n'est pas affectée par la taille de la ville : une mine ne
    /// creuse pas plus vite parce que le bourg a grandi. Cette asymétrie est
    /// volontaire — c'est elle qui fait qu'une ville qui grandit devient une
    /// destination et non une source.
    /// </para>
    /// </summary>
    private void ApplyRates(SimTick tick)
    {
        double phase = (double)tick.DayOfYear / SimTick.TicksPerYear;

        foreach (var city in _cities)
            foreach (var ms in city.Markets)
            {
                ms.Market.BaseDemandRate = ms.NominalDemand * city.Size
                    * Seasonal(ms.DemandAmplitude, ms.DemandPeak, phase)
                    * ms.Market.EventDemandFactor
                    * ms.Market.CycleDemandFactor;
                ms.Market.BaseProductionRate = ms.NominalProduction
                    * Seasonal(ms.ProductionAmplitude, ms.ProductionPeak, phase)
                    * ms.Market.EventProductionFactor;
            }
    }

    /// <summary>
    /// Facteur saisonnier. Cosinus centré sur 1 : la moyenne annuelle vaut
    /// exactement le taux nominal, ce qui laisse valide le bilan offre/demande
    /// statique de <c>--balance</c>. Une saisonnalité qui déplacerait la moyenne
    /// serait un déséquilibre structurel déguisé en dynamique.
    /// </summary>
    private static double Seasonal(double amplitude, double peak, double phase)
    {
        if (amplitude <= 0) return 1.0;
        return 1.0 + amplitude * Math.Cos(2.0 * Math.PI * (phase - peak));
    }

    private double SafeAmplitude(double amplitude)
        => Maths.Clamp(amplitude, 0.0, Maths.Clamp(_cfg.MaxSeasonalSwing, 0.0, 0.99));

    private static CargoSeasonDef? FindSeason(AnticipatingEconomyDef cfg, string cargoId)
    {
        foreach (var season in cfg.Seasons)
            if (season.Cargo == cargoId) return season;
        return null;
    }

    /// <summary>
    /// Phase 1 — production primaire. Identique à la référence dans son principe :
    /// le frein est l'encombrement de l'entrepôt, jamais le prix ni la couverture.
    /// <para>
    /// Une seule différence, et elle compte : la capacité de l'entrepôt est calculée
    /// sur le débit <em>nominal</em> et non sur le débit du jour, et elle est
    /// agrandie de l'amplitude de la saison. Un grenier ne rétrécit pas en hiver, et
    /// une ferme qui rentre sa récolte en trois mois construit un grenier pour la
    /// récolte, pas pour sa moyenne annuelle.
    /// <para>
    /// Sans cet agrandissement, la saisonnalité <em>détruisait</em> de la production
    /// au lieu de la déplacer : l'entrepôt se remplissait au pic, le frein
    /// d'encombrement bridait la récolte, et la morte-saison ne rattrapait rien —
    /// le débit annuel d'une ferme saisonnière tombait sous celui d'une ferme
    /// régulière et le résultat du transport avec lui. Un frein dimensionné sur la
    /// moyenne d'un phénomène qui n'est plus moyen est un déséquilibre structurel
    /// déguisé.
    /// </para>
    /// </para>
    /// </summary>
    private void ProducePrimary(WorldState world)
    {
        var cfg = world.Def.Economy;

        foreach (var city in _cities)
            foreach (var ms in city.Markets)
            {
                if (ms.Market.BaseProductionRate <= 0) continue;

                double storage = ms.NominalProduction
                    * (1.0 + ms.ProductionAmplitude) * cfg.ProductionStorageTicks;
                double factor = storage > 0
                    ? Maths.Clamp(2.0 * (1.0 - ms.Market.Stock / storage), cfg.MinProductionFactor, 1.0)
                    : 1.0;

                ms.Market.Produce(ms.Market.BaseProductionRate * factor);
            }
    }

    /// <summary>
    /// Phase 2 — usines. Reprise à l'identique de la référence : la rentabilité
    /// d'une transformation n'est pas le sujet de ce solveur, et la modifier en
    /// même temps que les prix rendrait impossible d'attribuer une différence
    /// mesurée à l'un ou à l'autre.
    /// </summary>
    private void RunIndustries(WorldState world)
    {
        var cfg = world.Def.Economy;

        foreach (var city in world.Cities)
        {
            foreach (var industry in city.Industries)
            {
                var recipe = industry.Recipe;
                double capacityRuns = recipe.RatePerTick * industry.Capacity;
                double maxRuns = capacityRuns;

                foreach (var input in recipe.Inputs)
                    maxRuns = Math.Min(maxRuns, city.Market(input.Cargo).Stock / input.Qty);

                foreach (var output in recipe.Outputs)
                {
                    double storage = output.Qty * capacityRuns * cfg.ProductionStorageTicks;
                    if (storage <= 0) continue;
                    double factor = Maths.Clamp(
                        2.0 * (1.0 - city.Market(output.Cargo).Stock / storage), 0.0, 1.0);
                    maxRuns = Math.Min(maxRuns, capacityRuns * factor);
                }

                if (maxRuns <= 0)
                {
                    industry.Utilization = 0;
                    continue;
                }

                double inputValue = 0;
                foreach (var input in recipe.Inputs)
                    inputValue += city.Market(input.Cargo).Price * input.Qty;

                double outputValue = 0;
                foreach (var output in recipe.Outputs)
                    outputValue += world.Cargo(output.Cargo).BasePrice * output.Qty;

                if (outputValue < inputValue * (1.0 + recipe.MinMargin))
                {
                    industry.Utilization = 0;
                    continue;
                }

                foreach (var input in recipe.Inputs)
                    city.Market(input.Cargo).Consume(maxRuns * input.Qty);

                foreach (var output in recipe.Outputs)
                    city.Market(output.Cargo).Produce(maxRuns * output.Qty);

                industry.Utilization = capacityRuns > 0 ? maxRuns / capacityRuns : 0;
            }
        }
    }

    /// <summary>
    /// Phase 3 — consommation des habitants, modulée par le prix. La saison et la
    /// taille de la ville sont déjà dans <see cref="Market.BaseDemandRate"/> : ce
    /// sont des besoins, et l'élasticité s'applique au besoin du jour, non à celui
    /// du scénario.
    /// </summary>
    private void ConsumeDemand(WorldState world)
    {
        var cfg = world.Def.Economy;

        foreach (var city in _cities)
            foreach (var ms in city.Markets)
            {
                if (ms.Market.BaseDemandRate <= 0) continue;

                double priceRatio = ms.Market.Price > 0 ? ms.Cargo.BasePrice / ms.Market.Price : 1.0;
                double response = Maths.Clamp(
                    Math.Pow(priceRatio, ms.Cargo.Elasticity),
                    cfg.MinConsumptionResponse,
                    cfg.MaxConsumptionResponse);
                ms.Market.Consume(ms.Market.BaseDemandRate * response);
            }
    }

    private void RecomputePrices(WorldState world)
    {
        foreach (var city in _cities)
            foreach (var ms in city.Markets)
                ms.Market.Price = AnticipatedPrice(world, ms);
    }

    /// <summary>
    /// Prix anticipé : ce que vaudra la marchandise dans quelques ticks si le stock
    /// continue sur sa pente, mélangé au prix au comptant.
    /// <para>
    /// Le calcul passe par le modèle de prix du monde appliqué à un stock projeté,
    /// plutôt que par un facteur correctif maison. C'est ce qui garantit que
    /// l'anticipation reste dans les bornes du modèle — donc que l'invariant
    /// <c>prix-borne</c> tient sans qu'il faille y penser — et qu'elle suit
    /// automatiquement un remplacement du modèle de prix.
    /// </para>
    /// <para>
    /// Un marché sans acheteur n'anticipe rien : il vaut le prix plancher, comme
    /// dans la référence. Projeter un prix de pénurie sur une ville qui ne consomme
    /// pas la marchandise est exactement le bug que le modèle de prix a déjà eu une
    /// fois, et il attirait des trains chargés de blé sur des quais où il
    /// pourrissait.
    /// </para>
    /// </summary>
    private double AnticipatedPrice(WorldState world, MarketState ms)
    {
        double spot = world.PriceModel.PriceFor(ms.Cargo, ms.Market);

        double weight = Maths.Clamp(_cfg.AnticipationWeight, 0.0, 1.0);
        if (weight <= 0 || _cfg.AnticipationTicks <= 0) return spot;

        double demand = ms.Market.BaseDemandRate + ms.Market.IndustryDemandRate;
        if (demand <= 0) return spot;

        _probe.BaseDemandRate = demand;
        _probe.IndustryDemandRate = 0;
        _probe.Stock = Math.Max(0.0, ms.Market.Stock + ms.Drift * _cfg.AnticipationTicks);

        double projected = world.PriceModel.PriceFor(ms.Cargo, _probe);
        double blended = spot + weight * (projected - spot);

        return Maths.Clamp(
            blended,
            ms.Cargo.BasePrice * world.Def.PriceModel.MinMultiplier,
            ms.Cargo.BasePrice * world.Def.PriceModel.MaxMultiplier);
    }
}
