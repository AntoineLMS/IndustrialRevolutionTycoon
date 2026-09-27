namespace RailTycoon.Tests;

public sealed class AssertionException(string message) : Exception(message);

/// <summary>Runner minimal : enregistrer des cas, les exécuter, sortir 1 si l'un échoue.</summary>
public sealed class TestRunner
{
    private readonly List<(string Name, Action Body)> _tests = new();

    public void Add(string name, Action body) => _tests.Add((name, body));

    public int Run()
    {
        int passed = 0;
        var failures = new List<(string Name, string Message)>();

        foreach (var (name, body) in _tests)
        {
            try
            {
                body();
                passed++;
                Console.WriteLine($"  ok    {name}");
            }
            catch (Exception ex)
            {
                failures.Add((name, ex is AssertionException ? ex.Message : ex.ToString()));
                Console.WriteLine($"  ÉCHEC {name}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{passed}/{_tests.Count} tests réussis.");

        if (failures.Count > 0)
        {
            Console.WriteLine();
            foreach (var (name, message) in failures)
            {
                Console.WriteLine($"--- {name}");
                Console.WriteLine($"    {message}");
            }
            return 1;
        }
        return 0;
    }
}

public static class Check
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new AssertionException(message);
    }

    public static void Equal(string expected, string actual, string message)
    {
        if (expected != actual)
            throw new AssertionException($"{message} — attendu « {expected} », obtenu « {actual} »");
    }

    public static void Near(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new AssertionException($"{message} — attendu {expected:0.######} ± {tolerance}, obtenu {actual:0.######}");
    }

    public static void Less(double smaller, double larger, string message)
    {
        if (!(smaller < larger))
            throw new AssertionException($"{message} — attendu {smaller:0.####} < {larger:0.####}");
    }

    public static void Throws<TException>(Action body, string message) where TException : Exception
    {
        try
        {
            body();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new AssertionException($"{message} — exception inattendue : {ex.GetType().Name}");
        }
        throw new AssertionException($"{message} — aucune exception levée");
    }
}
