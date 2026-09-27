using RailTycoon.Sim.Core;
using RailTycoon.Sim.Economy;
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

    public Simulation(ScenarioDef scenario, IEconomySolver? economy = null, IHaulageSolver? haulage = null,
        IFinanceSolver? finance = null)
    {
        var priceModel = new HyperbolicPriceModel(scenario.PriceModel);

        World = WorldBuilder.Build(scenario, priceModel);
        Economy = economy ?? new ReferenceEconomySolver();
        Haulage = haulage ?? new OpportunisticHaulageSolver();
        Finance = finance ?? new ReferenceFinanceSolver();

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
