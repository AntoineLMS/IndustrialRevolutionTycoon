using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Events;
using RailTycoon.Sim.Finance;
using RailTycoon.Sim.Transport;

namespace RailTycoon.Sim;

/// <summary>
/// L'orchestrateur. Il possède l'ordre des phases d'un tick et rien d'autre :
/// aucune règle de jeu ne vit ici, seulement l'enchaînement.
/// <para>
/// <b>L'ordre des phases fait partie du contrat du projet.</b> Le modifier change
/// les résultats de toutes les parties et invalide les traces de régression. Si
/// un module a besoin d'un ordre différent, cela se discute et se documente dans
/// docs/ARCHITECTURE.md — cela ne se change pas au détour d'un correctif.
/// </para>
/// </summary>
public sealed class Simulation
{
    public WorldState World { get; }
    public IEconomySolver Economy { get; }
    public IHaulageSolver Haulage { get; }
    public IFinanceSolver Finance { get; }
    public IEventSolver Events { get; }

    public Simulation(ScenarioDef scenario, IEconomySolver? economy = null, IHaulageSolver? haulage = null,
        IFinanceSolver? finance = null, IEventSolver? events = null)
    {
        var priceModel = new HyperbolicPriceModel(scenario.PriceModel);

        World = WorldBuilder.Build(scenario, priceModel);
        Economy = economy ?? new ReferenceEconomySolver();
        Haulage = haulage ?? new OpportunisticHaulageSolver();
        Finance = finance ?? new ReferenceFinanceSolver();
        Events = events ?? new ReferenceEventSolver();

        // Les événements s'initialisent avant l'économie : ils valident leurs
        // cibles et ouvrent leur flux aléatoire, mais ne publient rien avant le
        // premier tick. Les multiplicateurs valent 1 à l'initialisation, donc les
        // prix d'ouverture sont ceux du scénario nu, avec ou sans module.
        Events.Initialize(World);
        Economy.Initialize(World);
        Haulage.Initialize(World);
        // La finance ouvre ses comptes en dernier : elle reflète la trésorerie
        // d'exploitation, or le transporteur a déjà pu échanger à l'initialisation.
        Finance.Initialize(World);
    }

    public void Step()
    {
        var tick = World.Tick.Next();
        World.Tick = tick;

        // Phase 0 — remise à zéro de la télémétrie du tick.
        World.Company.BeginTick();
        foreach (var city in World.Cities)
            foreach (var market in World.MarketsOf(city))
                market.BeginTick();

        // Phase 0b — événements. Ouvre et éteint les événements du jour, tire les
        // aléatoires sur la séquence propre du module, et publie sur chaque marché
        // les multiplicateurs que le solveur économique va lire. Ne touche ni stock,
        // ni prix, ni argent ; inactif, n'écrit rien. Voir docs/ARCHITECTURE.md,
        // tableau des phases, pour la raison de cette place.
        Events.Step(World, tick);

        // Phase 0c — le charroi : les charrettes en route avancent et livrent, de
        // nouvelles partent. Avant l'économie, sur les prix de la veille, pour qu'aucun
        // solveur n'ait à le connaître : il déplace du stock, il ne fixe aucun prix.
        // Voir docs/ARCHITECTURE.md, tableau des phases.
        Cartage.Step(World);

        // Phase 1 — production, consommation, formation des prix.
        Economy.Step(World, tick);

        // Phase 2 — les trains roulent et échangent. Ils voient les prix
        // d'après-production : le joueur arrive sur un marché tel qu'il est.
        Haulage.Step(World, tick);

        // Phase 6 du tableau de docs/ARCHITECTURE.md — la finance : société,
        // emprunts, dividendes, bourse. Ajoutée en fin de tick, jamais avant : elle
        // constate ce que l'exploitation a produit. La placer plus tôt ferait
        // décider d'un dividende sur le résultat de la veille, et changerait le
        // résultat de toutes les parties existantes.
        Finance.Step(World, tick);
    }

    public void Run(int ticks)
    {
        for (int i = 0; i < ticks; i++) Step();
    }
}

internal static class WorldBuilder
{
    public static WorldState Build(ScenarioDef scenario, IPriceModel priceModel)
    {
        Validate(scenario);

        var world = new WorldState
        {
            Def = scenario,
            PriceModel = priceModel,
            Rng = new DeterministicRandom(scenario.Seed),
            Company = new Company { Cash = scenario.StartingCash },
        };

        foreach (var cargo in scenario.Cargos) world.Register(cargo);
        foreach (var recipe in scenario.Recipes) world.Register(recipe);

        foreach (var cityDef in scenario.Cities)
        {
            var city = new City { Def = cityDef };

            // La place de chaque ville a un marché pour chaque marchandise, même sans
            // offre ni demande locale. Cela évite un test d'existence dans chaque
            // boucle. Les habitants y achètent ; rien n'y est produit.
            foreach (var cargo in scenario.Cargos)
            {
                var market = new Market { CargoId = cargo.Id, CityId = cityDef.Id };
                cityDef.Demand.TryGetValue(cargo.Id, out double demand);
                market.BaseDemandRate = demand;
                market.NominalDemandRate = demand;
                city.Markets[cargo.Id] = market;
            }

            // Une installation par marchandise produite, puis une par usine. Chacune a
            // ses marchés : la forêt vend ses grumes, la scierie achète les siennes, et
            // rien ne passe de l'une à l'autre sans un train ou une charrette, même
            // dans la même ville (décision de l'équipe du 27 octobre 2026).
            double storageTicks = scenario.Economy.ProductionStorageTicks;
            foreach (var cargo in scenario.Cargos)
            {
                if (!cityDef.Production.TryGetValue(cargo.Id, out double production) || production <= 0) continue;
                cityDef.ProductionSites.TryGetValue(cargo.Id, out var offset);
                var site = new Site
                {
                    Id = "production:" + cargo.Id, CityId = cityDef.Id,
                    DxKm = offset?.DxKm ?? 0, DyKm = offset?.DyKm ?? 0,
                };
                site.Markets[cargo.Id] = new Market
                {
                    CargoId = cargo.Id, CityId = cityDef.Id, SiteId = site.Id,
                    BaseProductionRate = production,
                    NominalProductionRate = production,
                    // L'entrepôt, sur le même débit nominal que le frein de production
                    // des solveurs : c'est à lui que se mesure l'abondance chez un
                    // producteur, donc son prix.
                    StorageCapacity = production * storageTicks,
                };
                city.Sites.Add(site);
            }

            var siteIds = new HashSet<string>();
            foreach (var industryDef in cityDef.Industries)
            {
                var industry = new Industry
                {
                    Recipe = world.Recipe(industryDef.Recipe),
                    Capacity = industryDef.Capacity,
                };
                string id = industryDef.Recipe;
                for (int k = 2; !siteIds.Add(id); k++) id = industryDef.Recipe + "#" + k;
                var site = new Site
                {
                    Id = id, CityId = cityDef.Id, Industry = industry,
                    DxKm = industryDef.DxKm, DyKm = industryDef.DyKm,
                };
                industry.Site = site;

                // La demande de l'usine est celle de son marché d'intrants : un moulin
                // rend le blé cher chez lui, ce qui est précisément le signal qui doit
                // attirer le joueur.
                double runs = industry.Recipe.RatePerTick * industry.Capacity;
                foreach (var cargo in scenario.Cargos)
                {
                    double demand = industry.Recipe.Inputs.Where(i => i.Cargo == cargo.Id).Sum(i => i.Qty) * runs;
                    double output = industry.Recipe.Outputs.Where(o => o.Cargo == cargo.Id).Sum(o => o.Qty) * runs;
                    if (demand <= 0 && output <= 0) continue;
                    site.Markets[cargo.Id] = new Market
                    {
                        CargoId = cargo.Id, CityId = cityDef.Id, SiteId = site.Id,
                        IndustryDemandRate = demand,
                        StorageCapacity = output * storageTicks,
                    };
                }
                city.Industries.Add(industry);
                city.Sites.Add(site);
            }

            // Le stock déclaré va dans l'entrepôt de la production primaire si la
            // ville produit la marchandise, sur la place sinon.
            foreach (var cargo in scenario.Cargos)
                if (cityDef.InitialStock.TryGetValue(cargo.Id, out double stock))
                    (city.ProductionMarket(cargo.Id) ?? city.Market(cargo.Id)).Stock = stock;

            foreach (var cargo in scenario.Cargos) city.AllMarkets.Add(city.Markets[cargo.Id]);
            foreach (var site in city.Sites)
                foreach (var cargo in scenario.Cargos)
                    if (site.Markets.TryGetValue(cargo.Id, out var market)) city.AllMarkets.Add(market);

            world.Register(city);
        }

        ApplyInitialCoverage(scenario, world, priceModel);

        foreach (var lineDef in scenario.Lines)
        {
            world.Lines.Add(new RailLine
            {
                Id = lineDef.Id,
                Name = lineDef.Name,
                Stops = lineDef.Stops
                    .Select(s => new RailStop { CityId = s.City, DistanceKm = s.DistanceKm, DeclaredRadiusKm = s.RadiusKm })
                    .ToList(),
            });
        }

        // Le module réseau construit son graphe de voies sur le relief et en dérive
        // ses propres lignes. Sans relief dans le scénario, il ne fait rien et les
        // lignes ci-dessus font foi : c'est la voie de compatibilité.
        world.Network = RailTycoon.Sim.Network.NetworkBuilder.Attach(
            scenario, world.Lines, cityId => world.Cities.Any(c => c.Id == cityId));

        world.Terrain = world.Network?.Terrain
            ?? (scenario.Network.Terrain is { } terrain
                ? RailTycoon.Sim.Network.TerrainFactory.Build(terrain, scenario.Seed)
                : null);

        PlaceOnMap(scenario, world);
        AttachStations(scenario, world);

        foreach (var trainDef in scenario.Trains)
        {
            var line = world.Lines.FirstOrDefault(l => l.Id == trainDef.Line)
                ?? throw new InvalidDataException(
                    $"Le train '{trainDef.Id}' référence la ligne inconnue '{trainDef.Line}'.");

            int startStop = Math.Clamp(trainDef.StartStop, 0, line.Stops.Count - 1);
            world.Trains.Add(new Train
            {
                Id = trainDef.Id,
                Line = line,
                Capacity = trainDef.Capacity,
                SpeedKmPerTick = trainDef.SpeedKmPerTick,
                CostPerKm = trainDef.CostPerKm,
                StopIndex = startStop,
                // Au terminus on ne peut que revenir.
                Direction = startStop >= line.Stops.Count - 1 ? -1 : 1,
            });
        }

        return world;
    }

    /// <summary>
    /// Pose les villes, leurs installations et donc leurs marchés sur la carte.
    /// <para>
    /// Le centre d'une ville est la position que le scénario lui donne, à défaut celle
    /// du premier nœud du réseau qui lui est rattaché, à défaut celle de son premier
    /// arrêt sur une ligne saisie à la main — l'arrêt à <c>distanceKm</c> est au point
    /// (distanceKm, 0) : une ligne sans relief est une droite. Une installation est
    /// au centre de sa ville, décalée de ce que les données déclarent.
    /// </para>
    /// </summary>
    private static void PlaceOnMap(ScenarioDef scenario, WorldState world)
    {
        foreach (var city in world.Cities)
        {
            (double X, double Y)? position = null;
            if (city.Def.XKm is double x && city.Def.YKm is double y) position = (x, y);

            var node = scenario.Network.Nodes.FirstOrDefault(n => n.City == city.Id);
            if (position is null && node is not null) position = (node.XKm, node.YKm);

            if (position is null)
                foreach (var line in scenario.Lines)
                {
                    var stop = line.Stops.FirstOrDefault(s => s.City == city.Id);
                    if (stop is null) continue;
                    position = (stop.DistanceKm, 0);
                    break;
                }

            (city.XKm, city.YKm) = position ?? (0, 0);

            foreach (var market in city.Markets.Values)
                (market.XKm, market.YKm) = (city.XKm, city.YKm);
            foreach (var site in city.Sites)
                foreach (var market in site.Markets.Values)
                    (market.XKm, market.YKm) = (city.XKm + site.DxKm, city.YKm + site.DyKm);
        }
    }

    /// <summary>
    /// Pose les gares et leur rayon d'action. Une gare est au nœud du réseau qu'elle
    /// occupe, ou, sur une ligne saisie à la main, au point (distanceKm, 0). Elle sert
    /// tous les marchés à portée, dans l'ordre stable des villes ; une gare qui n'en
    /// sert aucun est une erreur de données, signalée au chargement plutôt qu'un train
    /// qui roule sans jamais rien échanger.
    /// </summary>
    private static void AttachStations(ScenarioDef scenario, WorldState world)
    {
        foreach (var line in world.Lines)
            foreach (var stop in line.Stops)
            {
                if (stop.NodeIndex >= 0 && world.Network is { } network)
                {
                    var node = network.Graph.Nodes[stop.NodeIndex];
                    (stop.XKm, stop.YKm) = (node.Position.XKm, node.Position.YKm);
                    stop.RadiusKm = stop.DeclaredRadiusKm
                        ?? scenario.Network.Nodes[stop.NodeIndex].StationRadiusKm
                        ?? scenario.Haulage.StationRadiusKm;
                }
                else
                {
                    (stop.XKm, stop.YKm) = (stop.DistanceKm, 0);
                    stop.RadiusKm = stop.DeclaredRadiusKm ?? scenario.Haulage.StationRadiusKm;
                }

                if (stop.RadiusKm < 0)
                    throw new InvalidDataException($"La gare de '{stop.CityId}' sur la ligne '{line.Id}' a un rayon d'action négatif.");

                var markets = new List<Market>();
                foreach (var city in world.Cities)
                    foreach (var market in world.MarketsOf(city))
                    {
                        double dx = market.XKm - stop.XKm, dy = market.YKm - stop.YKm;
                        if (Math.Sqrt(dx * dx + dy * dy) <= stop.RadiusKm + 1e-9) markets.Add(market);
                    }
                if (markets.Count == 0)
                    throw new InvalidDataException(
                        $"La gare de '{stop.CityId}' sur la ligne '{line.Id}' ne dessert aucun marché à {stop.RadiusKm} km.");
                stop.Markets = markets;
            }
    }

    /// <summary>
    /// Remplit les stocks initiaux à une couverture donnée, pour les marchés qui
    /// ne déclarent pas de stock explicite.
    /// <para>
    /// Sans cela il faudrait saisir à la main un stock par ville et par
    /// marchandise — soixante nombres dans le scénario de référence, tous à
    /// recalculer dès qu'une demande change. Et démarrer tous les marchés à vide
    /// n'est pas neutre : la première année se passerait en pénurie générale, tous
    /// les prix collés au plafond, ce qui ne dit rien du régime d'équilibre qu'on
    /// cherche à observer.
    /// </para>
    /// </summary>
    private static void ApplyInitialCoverage(ScenarioDef scenario, WorldState world, IPriceModel priceModel)
    {
        if (scenario.InitialCoverage <= 0) return;

        double horizon = scenario.PriceModel.CoverageHorizonTicks;

        foreach (var city in world.Cities)
        {
            foreach (var market in world.MarketsOf(city))
            {
                // Un stock explicite dans les données est un choix de conception :
                // on ne l'écrase pas, sur le marché qui l'a reçu.
                if (city.Def.InitialStock.ContainsKey(market.CargoId)
                    && ReferenceEquals(market, city.ProductionMarket(market.CargoId) ?? city.Market(market.CargoId)))
                    continue;

                double demand = market.BaseDemandRate + market.IndustryDemandRate;
                if (demand <= 0) continue; // rien ne se consomme ici : pas de stock d'amorçage

                market.Stock = scenario.InitialCoverage * demand * horizon;
            }
        }
    }

    /// <summary>
    /// Validation du scénario au chargement. On échoue fort et tôt : une donnée
    /// de conception incohérente doit produire un message clair, pas une
    /// exception obscure au millième tick.
    /// </summary>
    private static void Validate(ScenarioDef s)
    {
        TrainCost.Validate(s);
        Cartage.Validate(s.Economy.Cartage);

        var prices = s.PriceModel;
        if (prices.ProducerMaxMultiplier < prices.MinMultiplier || prices.ProducerMaxMultiplier > prices.MaxMultiplier)
            throw new InvalidDataException(
                $"priceModel.producerMaxMultiplier ({prices.ProducerMaxMultiplier}) doit être entre " +
                $"minMultiplier ({prices.MinMultiplier}) et maxMultiplier ({prices.MaxMultiplier}).");

        if (s.Cargos.Count == 0)
            throw new InvalidDataException("Le scénario ne déclare aucune marchandise.");

        var cargoIds = new HashSet<string>();
        foreach (var c in s.Cargos)
        {
            if (string.IsNullOrWhiteSpace(c.Id))
                throw new InvalidDataException("Une marchandise n'a pas d'identifiant.");
            if (!cargoIds.Add(c.Id))
                throw new InvalidDataException($"Marchandise en double : '{c.Id}'.");
            if (c.BasePrice <= 0)
                throw new InvalidDataException($"La marchandise '{c.Id}' a un prix de base nul ou négatif.");
        }

        var recipeIds = new HashSet<string>();
        foreach (var r in s.Recipes)
        {
            if (!recipeIds.Add(r.Id))
                throw new InvalidDataException($"Recette en double : '{r.Id}'.");
            if (r.Inputs.Count == 0 || r.Outputs.Count == 0)
                throw new InvalidDataException($"La recette '{r.Id}' doit avoir au moins un intrant et un produit.");
            foreach (var ing in r.Inputs.Concat(r.Outputs))
            {
                if (!cargoIds.Contains(ing.Cargo))
                    throw new InvalidDataException($"La recette '{r.Id}' référence la marchandise inconnue '{ing.Cargo}'.");
                if (ing.Qty <= 0)
                    throw new InvalidDataException($"La recette '{r.Id}' a une quantité nulle ou négative pour '{ing.Cargo}'.");
            }
        }

        var cityIds = new HashSet<string>();
        foreach (var c in s.Cities)
        {
            if (!cityIds.Add(c.Id))
                throw new InvalidDataException($"Ville en double : '{c.Id}'.");
            foreach (string cargoId in c.Demand.Keys.Concat(c.Production.Keys).Concat(c.InitialStock.Keys))
                if (!cargoIds.Contains(cargoId))
                    throw new InvalidDataException($"La ville '{c.Id}' référence la marchandise inconnue '{cargoId}'.");
            foreach (var ind in c.Industries)
                if (!recipeIds.Contains(ind.Recipe))
                    throw new InvalidDataException($"La ville '{c.Id}' référence la recette inconnue '{ind.Recipe}'.");
            // Une position d'installation sans production serait inerte sans le dire.
            foreach (string cargoId in c.ProductionSites.Keys)
                if (!c.Production.TryGetValue(cargoId, out double rate) || rate <= 0)
                    throw new InvalidDataException(
                        $"La ville '{c.Id}' place une installation de production de '{cargoId}', qu'elle ne produit pas.");
            if (c.XKm.HasValue != c.YKm.HasValue)
                throw new InvalidDataException($"La ville '{c.Id}' doit déclarer xKm et yKm ensemble.");
        }

        foreach (var line in s.Lines)
        {
            if (line.Stops.Count < 2)
                throw new InvalidDataException($"La ligne '{line.Id}' doit avoir au moins deux arrêts.");
            for (int i = 0; i < line.Stops.Count; i++)
            {
                if (!cityIds.Contains(line.Stops[i].City))
                    throw new InvalidDataException($"La ligne '{line.Id}' s'arrête à la ville inconnue '{line.Stops[i].City}'.");
                if (i > 0 && line.Stops[i].DistanceKm <= line.Stops[i - 1].DistanceKm)
                    throw new InvalidDataException(
                        $"La ligne '{line.Id}' : les distances doivent être strictement croissantes " +
                        $"(arrêt '{line.Stops[i].City}').");
            }
        }
    }
}
