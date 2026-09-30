using System.Globalization;
using RailTycoon.Sim;
using RailTycoon.Sim.Economy;
using RailTycoon.Sim.Telemetry;

namespace RailTycoon.Tests;

/// <summary>
/// Traces de référence : l'empreinte attendue de chaque scénario livré, figée ici.
/// <para>
/// Elles remplacent le test « deux exécutions donnent la même trace », qui ne
/// pouvait pas échouer : la simulation ne tire aucun aléa, et deux exécutions du
/// même processus parcourent leurs dictionnaires dans le même ordre. Une empreinte
/// figée se compare, elle, à une exécution d'un autre jour, d'un autre processus et
/// d'une autre version du code.
/// </para>
/// <para>
/// <b>Quand l'une d'elles casse.</b> Ce n'est pas forcément un bug : c'est un
/// changement de comportement, et il doit être justifié. Si le changement est
/// voulu, recopier l'empreinte obtenue que donne le message d'échec, et dire dans
/// le message de commit pourquoi la trace a bougé — et si un chiffre cité dans
/// docs/FINDINGS.md bouge avec elle. Si le changement n'est pas voulu, c'est
/// exactement ce que ce test existe pour attraper.
/// </para>
/// <para>
/// <b>Limite connue.</b> Les empreintes ont été posées sous Linux x64, .NET 8. La
/// consommation passe par <c>Math.Pow</c>, que .NET délègue à la bibliothèque
/// mathématique du système : rien ne garantit le même dernier bit sous Windows ou
/// macOS. Si ces tests échouent sur une autre plateforme sans changement de code,
/// c'est une découverte, pas un faux positif — la même simulation n'y joue pas la
/// même partie, ce qui compte pour une sauvegarde partagée ou un multijoueur en
/// lockstep.
/// </para>
/// </summary>
internal static class ReferenceTraceTests
{
    private const int Ticks = 720;

    private sealed record Case(string Scenario, string Solver, string Expected);

    /// <summary>
    /// Une ligne par couple scénario × solveur qu'un document cite ou qu'un module
    /// revendique. heartland-finance n'est éprouvée que sous la référence : sous
    /// l'anticipant, sa trace des marchés est celle de heartland, et le test
    /// « le module n'influence pas l'économie » le vérifie déjà. heartland-events,
    /// au contraire, change l'économie : il est figé sous les deux solveurs.
    /// </summary>
    private static readonly Case[] Cases =
    [
        new("heartland.json", "reference", "B966B86D3F0AF83C"),
        new("heartland.json", "anticipating", "29EE085518F1B2B0"),
        new("heartland-finance.json", "reference", "227CB2EF4504BDBE"),
        // Le scénario du module events, sous les deux solveurs : chacun compose les
        // multiplicateurs du jour dans ses propres taux, et l'empreinte porte aussi
        // le journal des événements déclenchés.
        new("heartland-events.json", "reference", "58116D2D6C28310A"),
        new("heartland-events.json", "anticipating", "E151A952D595E431"),
        new("ironpeak.json", "reference", "76B80943D1BEA4B7"),
        new("terrain-plain.json", "reference", "385B9C85FF4583DB"),
        new("terrain-valley.json", "reference", "20F02CD65C227C94"),
        new("terrain-pass.json", "reference", "597B843F8505881B"),
        new("sierra.json", "reference", "44876808B521C9E4"),
        new("sierra.json", "anticipating", "B80E7C2F7DFBD1A5"),
    ];

    public static void Register(TestRunner runner)
    {
        foreach (var c in Cases)
        {
            runner.Add($"trace de référence — {c.Scenario}, solveur {c.Solver}, {Ticks} ticks", () =>
            {
                var (fingerprint, netProfit) = Run(c.Scenario, c.Solver);
                Check.Equal(c.Expected, fingerprint,
                    $"empreinte de {c.Scenario} (résultat net obtenu : " +
                    $"{netProfit.ToString("N2", CultureInfo.InvariantCulture)}). " +
                    "Changement voulu ? Recopier l'empreinte obtenue et le justifier dans le commit");
            });
        }

        runner.Add("scénarios — chaque scénario livré prend position sur chaque module", () =>
        {
            // Un bloc absent n'est pas neutre, il est silencieusement inerte : c'est
            // ainsi qu'ironpeak tournait sans anticipation sans que personne le sache,
            // et que heartland-finance a perdu le bloc anticipating de l'économie
            // qu'il prétend copier. Chaque scénario déclare le bloc, ou dit
            // pourquoi il s'en passe par une clé « //<bloc> ».
            var dataDir = Path.Combine(Fixtures.RepoRoot(), "data");
            var scenarios = Directory.GetFiles(dataDir, "*.json")
                .Where(path => Path.GetFileName(path) != "locomotives.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            Check.True(scenarios.Count > 0, "aucun scénario trouvé dans data/");

            var silent = scenarios
                .SelectMany(path => ScenarioLoader.UndeclaredModuleBlocks(File.ReadAllText(path))
                    .Select(block => $"{Path.GetFileName(path)} : {block}"))
                .ToList();
            Check.True(silent.Count == 0,
                "blocs ni déclarés ni écartés explicitement — " + string.Join(", ", silent));
        });

        runner.Add("scénarios — un bloc absent sans explication est signalé", () =>
        {
            var undeclared = ScenarioLoader.UndeclaredModuleBlocks(
                """{ "id": "nu", "Network": {}, "//finance": "pas de finance ici", "//events": "ni d'événements" }""");
            Check.Equal("anticipating", string.Join(",", undeclared),
                "seul le bloc ni déclaré ni écarté doit être signalé, casse ignorée");
        });
    }

    private static (string Fingerprint, double NetProfit) Run(string scenarioFile, string solver)
    {
        var scenario = ScenarioLoader.Load(Path.Combine(Fixtures.RepoRoot(), "data", scenarioFile));
        IEconomySolver economy = solver switch
        {
            "reference" => new ReferenceEconomySolver(),
            "anticipating" => new AnticipatingEconomySolver(),
            _ => throw new ArgumentException($"solveur inconnu : {solver}"),
        };

        var sim = new Simulation(scenario, economy);
        var recorder = new CsvRecorder();
        recorder.Record(sim.World);
        for (int i = 0; i < Ticks; i++)
        {
            sim.Step();
            recorder.Record(sim.World);
        }

        // Le résumé du journal des événements est vide quand le module est inactif :
        // les empreintes des scénarios sans événements n'ont pas bougé à son arrivée.
        return (recorder.TraceFingerprint(FinanceSummary(sim.World) + sim.World.Events.Summary()),
            sim.World.Company.NetProfit);
    }

    /// <summary>
    /// L'état financier de fin de partie, au centime. Les traces CSV n'en portent
    /// que ce qui passe par la caisse du transporteur : un cours, une dette ou une
    /// fortune de magnat qui bougeraient seuls ne s'y verraient pas.
    /// </summary>
    private static string FinanceSummary(WorldState world)
    {
        var finance = world.Finance;
        if (!finance.Enabled || finance.Player is null || finance.Magnate is null) return "";

        var ci = CultureInfo.InvariantCulture;
        var player = finance.Player;
        var tycoon = finance.Magnate;
        return string.Join(";",
            player.Cash.ToString(ci),
            player.BookEquity.ToString(ci),
            player.SharePrice.ToString(ci),
            player.Debt.ToString(ci),
            player.OverdraftBalance.ToString(ci),
            player.DividendsPaidTotal.ToString(ci),
            tycoon.NetWorth(finance).ToString(ci),
            tycoon.MarginCalls.ToString(ci),
            finance.Mergers.Count.ToString(ci));
    }
}
