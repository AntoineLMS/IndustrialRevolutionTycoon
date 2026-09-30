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
    /// Le journal des événements en CSV, une ligne par (événement, ville,
    /// marchandise, levier) : début, fin, origine, multiplicateur au plus fort.
    /// <para>
    /// Écrit à part des trois traces : il ne s'accumule pas tick par tick, il se lit
    /// en fin de partie. Il n'entre pas dans <see cref="Fingerprint"/> ; les traces de
    /// référence, elles, ajoutent à leur empreinte le résumé du journal
    /// (<c>EventsState.Summary</c>), vide quand le module est inactif. Toujours
    /// écrit, même vide — un
    /// dossier de sortie réutilisé ne doit pas garder le journal d'une partie
    /// précédente à côté des traces de celle-ci.
    /// </para>
    /// </summary>
    public static string EventsCsv(WorldState world)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("instance,event,name,origin,start_tick,end_tick,ramp_ticks,city,cargo,on,peak_factor\n");
        foreach (var e in world.Events.Journal)
            foreach (var t in e.Targets)
                sb.Append(e.InstanceId).Append(',')
                  .Append(e.DefinitionId).Append(',')
                  .Append('"').Append(e.Name.Replace("\"", "\"\"")).Append('"').Append(',')
                  .Append(e.Origin switch
                  {
                      Events.EventOrigin.Historical => "historical",
                      Events.EventOrigin.Inspired => "inspired",
                      _ => "random",
                  }).Append(',')
                  .Append(e.StartTick.ToString(ci)).Append(',')
                  .Append(e.EndTick.ToString(ci)).Append(',')
                  .Append(e.RampTicks.ToString(ci)).Append(',')
                  .Append(t.CityId).Append(',')
                  .Append(t.Cargo).Append(',')
                  .Append(t.On).Append(',')
                  .Append(F(t.PeakFactor)).Append('\n');
        return sb.ToString();
    }

    public static void WriteEvents(WorldState world, string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "events.csv"), EventsCsv(world));
    }

    /// <summary>
    /// Le journal de la conjoncture en CSV, une ligne par entrée : changement de
    /// phase ou poussée d'un événement. Mêmes règles que <see cref="EventsCsv"/> :
    /// écrit à part des traces, toujours écrit, même vide, et hors de
    /// <see cref="Fingerprint"/> — les traces de référence ajoutent à leur empreinte
    /// <c>CycleState.Summary</c>, vide quand le module est inactif.
    /// <para>
    /// Il ne contient que ce que le journal public contient : jamais la date prévue
    /// de la fin d'une phase.
    /// </para>
    /// </summary>
    public static string CycleCsv(WorldState world)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("tick,kind,phase,previous_phase,cause,event,shift_ticks\n");
        foreach (var r in world.Cycle.Journal)
            sb.Append(r.Tick.ToString(ci)).Append(',')
              .Append(r.Kind == Cycle.CycleRecordKind.Phase ? "phase" : "shift").Append(',')
              .Append(r.PhaseId).Append(',')
              .Append(r.PreviousPhaseId).Append(',')
              .Append(r.Cause switch
              {
                  Cycle.CycleCause.Opening => "opening",
                  Cycle.CycleCause.Elapsed => "elapsed",
                  Cycle.CycleCause.Forced => "forced",
                  _ => "pushed",
              }).Append(',')
              .Append(r.EventInstanceId).Append(',')
              .Append(r.ShiftTicks.ToString(ci)).Append('\n');
        return sb.ToString();
    }

    public static void WriteCycle(WorldState world, string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "cycle.csv"), CycleCsv(world));
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
