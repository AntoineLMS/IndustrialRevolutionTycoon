namespace RailTycoon.Sim.Finance;

/// <summary>
/// La monnaie du module finance : un <see cref="decimal"/> arrondi au centime.
/// <para>
/// <b>Pourquoi pas <c>double</c>, alors que tout le reste de la simulation en
/// utilise.</b> Un <c>double</c> ne peut pas représenter 0,01 exactement. Chaque
/// opération laisse donc un résidu de l'ordre de 1e-16 relatif, et un bilan n'est
/// jamais vérifiable qu'« à une tolérance près ». Cette tolérance est précisément
/// ce qu'il ne faut pas accorder à une comptabilité : le bug du lavage de fret
/// (voir docs/FINDINGS.md) a fait gagner deux millions à la compagnie sans qu'un
/// seul invariant ne bronche, parce qu'aucun test ne demandait l'égalité exacte.
/// Un intérêt de 0,005 par tick perdu dans l'arrondi fait 3,60 par an et par
/// emprunt, et on ne le verra jamais dans une tolérance relative de 1e-9.
/// </para>
/// <para>
/// Avec <c>decimal</c> et un arrondi au centime à chaque écriture, le bilan
/// s'équilibre <em>exactement</em> — résidu nul, pas « petit ». Un test qui
/// compare à zéro est un test qui ne peut pas se dégrader en silence.
/// </para>
/// <para>
/// <b>La frontière entre les deux mondes.</b> Les quantités de fret, les prix
/// unitaires et les kilomètres restent en <c>double</c> : ce sont des grandeurs
/// continues issues d'un modèle continu, elles ne se conservent pas et personne
/// ne les additionne sur 720 ticks. Tout ce qui est un <em>solde</em> — caisse,
/// dette, capitaux propres, cours d'une action — est en <c>decimal</c>. La
/// conversion se fait en un seul endroit, <c>FinanceState.PostOperatingResult</c>,
/// et toujours dans le même sens : du monde continu vers la comptabilité.
/// </para>
/// </summary>
public static class Money
{
    public const decimal Cent = 0.01m;

    /// <summary>
    /// Au-delà, on est manifestement en présence d'un emballement plutôt que
    /// d'une fortune : mieux vaut échouer avec un message clair que propager un
    /// nombre absurde jusqu'au bilan.
    /// </summary>
    private const double SaneLimit = 1e13;

    /// <summary>
    /// Arrondi au centime, au pair en cas d'égalité. Le mode d'arrondi est un
    /// choix documenté : arrondir toujours vers le haut biaise systématiquement
    /// les intérêts en faveur du prêteur, ce qui se voit sur 720 ticks.
    /// </summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.ToEven);

    /// <summary>Arrondi au centime inférieur. Sert au dividende par action, qui ne doit jamais distribuer plus que déclaré.</summary>
    public static decimal Floor(decimal amount) => decimal.Floor(amount * 100m) / 100m;

    /// <summary>
    /// Le seul passage du monde continu à la comptabilité. Un NaN ou un infini
    /// venant du monde en <c>double</c> est une erreur de simulation : on refuse
    /// de l'écrire au bilan, où il deviendrait indétectable.
    /// </summary>
    public static decimal FromDouble(double amount)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount))
            throw new InvalidOperationException(
                $"Montant non fini transmis à la comptabilité : {amount}.");
        if (Math.Abs(amount) > SaneLimit)
            throw new InvalidOperationException(
                $"Montant hors d'échelle transmis à la comptabilité : {amount:0.###e+0}.");
        return Round((decimal)amount);
    }

    /// <summary>Retour vers le monde continu, pour la télémétrie et les CSV uniquement.</summary>
    public static double ToDouble(decimal amount) => (double)amount;

    /// <summary>
    /// Convertit un taux annuel en pourcentage en montant d'intérêt dû pour un
    /// tick, arrondi au centime. L'année de jeu fait
    /// <see cref="Core.SimTick.TicksPerYear"/> ticks : le taux affiché dans les
    /// données est annuel parce que c'est ainsi qu'un emprunt se lit, pas parce
    /// que le pas de simulation le serait.
    /// </summary>
    public static decimal InterestForTick(decimal principal, decimal annualRatePercent)
        => Round(principal * annualRatePercent / 100m / Core.SimTick.TicksPerYear);
}
