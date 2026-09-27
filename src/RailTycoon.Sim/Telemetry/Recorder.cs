using System.Globalization;
using System.Text;

namespace RailTycoon.Sim.Telemetry;

/// <summary>
/// Enregistre la trace de la simulation en CSV.
/// <para>
/// Format long (une ligne par tick × ville × marchandise) plutôt que large :
/// c'est le format qu'attendent pandas, R et gnuplot pour agréger et filtrer, et
/// il ne change pas de colonnes quand on ajoute une marchandise au scénario.
/// </para>
/// <para>
/// Toutes les valeurs sont formatées en culture invariante. Un CSV dont le
/// séparateur décimal dépend de la machine n'est pas une trace de régression.
/// </para>
/// </summary>
public sealed class CsvRecorder
{
    private readonly StringBuilder _markets = new();
    private readonly StringBuilder _company = new();
    private readonly StringBuilder _industries = new();

    /// <summary>Enregistre un tick sur N pour limiter la taille des traces longues.</summary>
    public int Every { get; init; } = 1;

    public CsvRecorder()
    {
        _markets.AppendLine("tick,city,cargo,stock,price,coverage,produced,consumed,imported,exported");
        _company.AppendLine("tick,cash,revenue,cost,net_profit");
        _industries.AppendLine("tick,city,recipe,utilization");
    }

    public void Record(WorldState world)
    {
        int tick = world.Tick.Index;
        if (Every > 1 && tick % Every != 0) return;

        var ci = CultureInfo.InvariantCulture;

        foreach (var city in world.Cities)
        {
            foreach (var market in world.MarketsOf(city))
            {
                double coverage = world.PriceModel.Coverage(market);
                _markets.Append(tick.ToString(ci)).Append(',')
                        .Append(city.Id).Append(',')
                        .Append(market.CargoId).Append(',')
                        .Append(F(market.Stock)).Append(',')
                        .Append(F(market.Price)).Append(',')
                        .Append(F(coverage)).Append(',')
                        .Append(F(market.ProducedThisTick)).Append(',')
                        .Append(F(market.ConsumedThisTick)).Append(',')
                        .Append(F(market.ImportedThisTick)).Append(',')
                        .Append(F(market.ExportedThisTick)).Append('\n');
            }

            foreach (var industry in city.Industries)
            {
                _industries.Append(tick.ToString(ci)).Append(',')
                           .Append(city.Id).Append(',')
                           .Append(industry.Recipe.Id).Append(',')
                           .Append(F(industry.Utilization)).Append('\n');
            }
        }

        var co = world.Company;
        _company.Append(tick.ToString(ci)).Append(',')
                .Append(F(co.Cash)).Append(',')
                .Append(F(co.RevenueThisTick)).Append(',')
                .Append(F(co.CostThisTick)).Append(',')
                .Append(F(co.NetProfit)).Append('\n');
    }

    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    public void WriteTo(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "markets.csv"), _markets.ToString());
        File.WriteAllText(Path.Combine(directory, "company.csv"), _company.ToString());
        File.WriteAllText(Path.Combine(directory, "industries.csv"), _industries.ToString());
    }

    /// <summary>
    /// Empreinte de la trace des marchés. Deux exécutions de la même version sur
    /// la même graine doivent produire la même empreinte : c'est le test de
    /// déterminisme le moins cher qui existe.
    /// </summary>
    public string Fingerprint() => Hash(_markets.ToString());

    /// <summary>
    /// Empreinte des trois traces — marchés, compagnie, usines — suivie d'un
    /// complément libre. C'est celle des traces de référence : l'empreinte des
    /// marchés seule ne verrait pas un changement de la trésorerie ou de
    /// l'utilisation des usines qui laisserait les prix intacts.
    /// </summary>
    public string TraceFingerprint(string extra = "")
        => Hash(_markets.ToString() + _company.ToString() + _industries.ToString() + extra);

    private static string Hash(string text)
    {
        byte[] bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 8);
    }
}
