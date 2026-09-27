namespace RailTycoon.Sim.Core;

public static class Maths
{
    public static double Clamp(double value, double min, double max)
        => value < min ? min : (value > max ? max : value);

    /// <summary>
    /// Écrase les résidus de calcul flottant vers zéro. Utilisé après les
    /// soustractions de stock pour qu'un stock « vide » le soit vraiment et ne
    /// traîne pas un -1e-17 qui ferait échouer l'invariant de positivité.
    /// </summary>
    public static double SnapToZero(double value, double epsilon = 1e-9)
        => Math.Abs(value) < epsilon ? 0.0 : value;
}
