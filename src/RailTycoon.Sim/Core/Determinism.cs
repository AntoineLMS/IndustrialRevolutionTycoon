namespace RailTycoon.Sim.Core;

/// <summary>
/// Générateur pseudo-aléatoire déterministe (PCG32). On n'utilise jamais
/// <see cref="System.Random"/> dans la simulation : son implémentation peut
/// changer d'une version de .NET à l'autre, ce qui casserait la reproductibilité
/// des parties sauvegardées et des tests de régression.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;
    private readonly ulong _increment;

    public DeterministicRandom(ulong seed, ulong sequence = 1)
    {
        _increment = (sequence << 1) | 1UL;
        _state = 0UL;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * 6364136223846793005UL + _increment);
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    /// <summary>Double dans [0, 1).</summary>
    public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

    /// <summary>Double dans [min, max).</summary>
    public double NextRange(double min, double max) => min + NextDouble() * (max - min);
}

/// <summary>
/// Horloge de simulation à pas fixe. Un tick = un jour de jeu.
/// <para>
/// Le pas fixe n'est pas un détail : c'est ce qui permet de rejouer une partie
/// à l'identique, de comparer deux versions de l'économie sur la même trace, et
/// plus tard d'envisager un multijoueur en lockstep. Rien dans la simulation ne
/// doit dépendre du temps réel ni du framerate.
/// </para>
/// </summary>
public readonly record struct SimTick(int Index)
{
    public const int TicksPerDay = 1;
    public const int TicksPerYear = 360;

    public int Year => Index / TicksPerYear;
    public int DayOfYear => Index % TicksPerYear;

    public SimTick Next() => new(Index + 1);
    public override string ToString() => $"A{Year}J{DayOfYear:D3}";
}
