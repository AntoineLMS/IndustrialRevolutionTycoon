using RailTycoon.Sim.Economy;

namespace RailTycoon.Sim.Telemetry;

/// <summary>
/// Analyse statique d'un scénario : pour chaque marchandise, l'offre potentielle
/// face à la demande potentielle, sans simuler quoi que ce soit.
/// <para>
/// Cet outil existe parce qu'un déséquilibre structurel ne ressemble pas à un
/// bug : la simulation tourne, les invariants tiennent, et pourtant le jeu est
/// mort. Si la demande totale de nourriture dépasse durablement ce que les
/// boulangeries peuvent produire, tous les marchés restent collés au plafond de
/// prix, l'écart entre villes disparaît, et il n'y a plus rien à arbitrer. Le
/// prix cesse d'être une information.
/// </para>
/// <para>
/// Une chaîne se lit de haut en bas : si la farine manque, ce n'est pas la
/// boulangerie qu'il faut agrandir, c'est le moulin — voire le champ de blé.
/// </para>
/// </summary>
public static class BalanceReport
{
    public sealed record CargoBalance(
        string CargoId,
        string CargoName,
        double PrimaryProduction,
        double IndustryOutput,
        double CitizenDemand,
        double IndustryInput)
    {
        public double Supply => PrimaryProduction + IndustryOutput;
        public double Demand => CitizenDemand + IndustryInput;

        /// <summary>
        /// Offre sur demande. Une valeur nettement inférieure à 1 signifie une
        /// pénurie structurelle : aucun joueur, aussi bon soit-il, ne pourra
        /// approvisionner ce marché.
        /// </summary>
        public double Ratio => Demand > 1e-9 ? Supply / Demand : double.PositiveInfinity;

        public string Verdict => Demand <= 1e-9
            ? (Supply > 1e-9 ? "produite mais jamais consommée" : "inutilisée")
            : Ratio switch
            {
                < 0.75 => "PÉNURIE STRUCTURELLE",
                < 0.95 => "tendu",
                <= 1.35 => "équilibré",
                <= 2.0 => "abondant",
                _ => "SURABONDANCE STRUCTURELLE",
            };
    }

    /// <summary>
    /// Les débits sont des <em>potentiels</em> : l'offre industrielle suppose des
    /// usines tournant à pleine capacité, ce qui n'arrive que si leurs intrants
    /// arrivent. Le rapport dit donc si le scénario est réalisable au mieux, pas
    /// ce qui se produira réellement.
    /// </summary>
    public static List<CargoBalance> Compute(ScenarioDef scenario)
    {
        var primary = new Dictionary<string, double>();
        var output = new Dictionary<string, double>();
        var citizen = new Dictionary<string, double>();
        var input = new Dictionary<string, double>();

        foreach (var cargo in scenario.Cargos)
        {
            primary[cargo.Id] = 0;
            output[cargo.Id] = 0;
            citizen[cargo.Id] = 0;
            input[cargo.Id] = 0;
        }

        var recipes = scenario.Recipes.ToDictionary(r => r.Id);

        foreach (var city in scenario.Cities)
        {
            foreach (var (cargoId, rate) in city.Production)
                primary[cargoId] += rate;

            foreach (var (cargoId, rate) in city.Demand)
                citizen[cargoId] += rate;

            foreach (var industry in city.Industries)
            {
                var recipe = recipes[industry.Recipe];
                double runs = recipe.RatePerTick * industry.Capacity;

                foreach (var ing in recipe.Inputs)
                    input[ing.Cargo] += ing.Qty * runs;
                foreach (var ing in recipe.Outputs)
                    output[ing.Cargo] += ing.Qty * runs;
            }
        }

        return scenario.Cargos
            .Select(c => new CargoBalance(
                c.Id, c.Name, primary[c.Id], output[c.Id], citizen[c.Id], input[c.Id]))
            .ToList();
    }
}
