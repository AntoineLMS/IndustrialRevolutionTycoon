using RailTycoon.Sim.Core;
using RailTycoon.Sim.Cycle;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Events;
using RailTycoon.Sim.Finance;
using RailTycoon.Sim.Objectives;
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
    public ICycleSolver Cycle { get; }
    public IObjectiveSolver Objectives { get; }

    public Simulation(ScenarioDef scenario, IEconomySolver? economy = null, IHaulageSolver? haulage = null,
        IFinanceSolver? finance = null, IEventSolver? events = null, ICycleSolver? cycle = null,
        IObjectiveSolver? objectives = null)
    {
        var priceModel = new HyperbolicPriceModel(scenario.PriceModel);

        World = WorldBuilder.Build(scenario, priceModel);
        Economy = economy ?? new ReferenceEconomySolver();
        Haulage = haulage ?? new OpportunisticHaulageSolver();
        Finance = finance ?? new ReferenceFinanceSolver();
        Events = events ?? new ReferenceEventSolver();
        Cycle = cycle ?? new ReferenceCycleSolver();
        Objectives = objectives ?? new ReferenceObjectiveSolver();

        // Les événements s'initialisent avant l'économie : ils valident leurs
        // cibles et ouvrent leur flux aléatoire, mais ne publient rien avant le
        // premier tick. Les multiplicateurs valent 1 à l'initialisation, donc les
        // prix d'ouverture sont ceux du scénario nu, avec ou sans module.
        Events.Initialize(World);
        // La conjoncture après les événements, dont elle vérifie les attributs
        // « cycle » ; elle ouvre sa phase initiale et tire sa durée, mais ne pose
        // aucun multiplicateur de demande avant le premier tick.
        Cycle.Initialize(World);
        Economy.Initialize(World);
        Haulage.Initialize(World);
        // La finance ouvre ses comptes en dernier : elle reflète la trésorerie
        // d'exploitation, or le transporteur a déjà pu échanger à l'initialisation.
        Finance.Initialize(World);
        // Les objectifs tout à la fin : ils vérifient qu'une fortune a un magnat à
        // lire, donc une finance ouverte, et ne lisent rien avant le soir du tick 1.
        Objectives.Initialize(World);
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

        // Phase 0c — conjoncture. Lit les événements ouverts aujourd'hui (bascules
        // forcées, poussées), fait avancer la phase, et publie les conditions du
        // jour : demande sur chaque marché pour l'économie, taux et multiple de
        // valorisation pour la finance. Après les événements, dont elle lit le
        // journal ; avant la production, qui compose sa demande. Inactive, n'écrit
        // rien. Voir docs/ARCHITECTURE.md, tableau des phases.
        Cycle.Step(World, tick);

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

        // Phase 7 — objectifs. Le soir, une fois tout constaté : fortune du magnat
        // après la bourse, livraisons et gares desservies du jour. Observateur pur :
        // ne touche à rien, ne tire rien ; placé en queue, il ne déplace aucune phase.
        // Voir docs/ARCHITECTURE.md, tableau des phases.
        Objectives.Step(World, tick);
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

            // Chaque ville possède un marché pour chaque marchandise, même sans
            // offre ni demande locale. Cela évite un test d'existence dans
            // chaque boucle, et un marché à zéro a un prix élevé qui signale
            // correctement « ici, c'est rare ».
            foreach (var cargo in scenario.Cargos)
            {
                var market = new Market { CargoId = cargo.Id, CityId = cityDef.Id };
                cityDef.Demand.TryGetValue(cargo.Id, out double demand);
                cityDef.Production.TryGetValue(cargo.Id, out double production);
                cityDef.InitialStock.TryGetValue(cargo.Id, out double stock);

                market.BaseDemandRate = demand;
                market.BaseProductionRate = production;
                market.NominalDemandRate = demand;
                market.NominalProductionRate = production;
                market.Stock = stock;
                city.Markets[cargo.Id] = market;
            }

            foreach (var industryDef in cityDef.Industries)
            {
                city.Industries.Add(new Industry
                {
                    Recipe = world.Recipe(industryDef.Recipe),
                    Capacity = industryDef.Capacity,
                });
            }

            // La demande des usines locales fait partie de la demande du marché :
            // un moulin rend le blé cher dans sa ville, ce qui est précisément le
            // signal qui doit attirer le joueur.
            foreach (var industry in city.Industries)
                foreach (var input in industry.Recipe.Inputs)
                    city.Markets[input.Cargo].IndustryDemandRate +=
                        input.Qty * industry.Recipe.RatePerTick * industry.Capacity;

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
                    .Select(s => new RailStop { CityId = s.City, DistanceKm = s.DistanceKm })
                    .ToList(),
            });
        }

        // Le module réseau construit son graphe de voies sur le relief et en dérive
        // ses propres lignes. Sans relief dans le scénario, il ne fait rien et les
        // lignes ci-dessus font foi : c'est la voie de compatibilité.
        world.Network = RailTycoon.Sim.Network.NetworkBuilder.Attach(
            scenario, world.Lines, cityId => world.Cities.Any(c => c.Id == cityId));

        foreach (var trainDef in scenario.Trains)
        {
            var line = world.Lines.FirstOrDefault(l => l.Id == trainDef.Line)
                ?? throw new InvalidDataException(
                    $"Le train '{trainDef.Id}' référence la ligne inconnue '{trainDef.Line}'.");

            int startStop = Math.Clamp(trainDef.StartStop, 0, line.Stops.Count - 1);

            // Module vehicles : la locomotive du catalogue remplace la vitesse et le
            // coût kilométrique déclarés (Transport/VehicleDefinitions.cs). Sans
            // module, le train est celui du scénario, champ pour champ.
            var vehicle = VehicleRules.Build(scenario, trainDef);
            var train = new Train
            {
                Id = trainDef.Id,
                Line = line,
                Capacity = trainDef.Capacity,
                SpeedKmPerTick = vehicle is null
                    ? trainDef.SpeedKmPerTick
                    : VehicleRules.TopSpeedKmh(scenario.Vehicles, vehicle.Locomotive) * scenario.Vehicles.RunningHoursPerTick,
                CostPerKm = vehicle is null ? trainDef.CostPerKm : scenario.Vehicles.OtherCostPerKm,
                Vehicle = vehicle,
                StopIndex = startStop,
                // Au terminus on ne peut que revenir.
                Direction = startStop >= line.Stops.Count - 1 ? -1 : 1,
            };
            VehicleRules.CheckClimbable(world, train);
            world.Trains.Add(train);
        }

        return world;
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
                // on ne l'écrase pas.
                if (city.Def.InitialStock.ContainsKey(market.CargoId)) continue;

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
        TrainCost.Validate(s.Haulage);
        VehicleRules.Validate(s);

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
