namespace RailTycoon.Sim.Economy;

/// <summary>
/// Le calendrier d'un scénario : une seule année de départ, que tous les modules
/// datés partagent.
/// <para>
/// L'année vivait dans chaque module qui en avait besoin — les événements, puis les
/// objectifs —, et les deux se vérifiaient l'un l'autre. Le catalogue de locomotives
/// en a besoin à son tour, et la question n'est plus propre à un module : elle monte
/// au scénario (<see cref="ScenarioDef.StartYear"/>). Les années des modules restent
/// lisibles pour ne casser aucun scénario, mais elles ne peuvent plus contredire
/// celle du scénario, et elles en héritent quand elles sont absentes.
/// </para>
/// </summary>
public static class ScenarioCalendar
{
    /// <summary>
    /// Vérifie que les années déclarées concordent, puis fait hériter aux modules
    /// datés l'année du scénario. Rend l'année retenue, ou 0 si aucune n'est
    /// déclarée. Appelée au chargement du monde, jamais pendant la partie.
    /// </summary>
    public static int Resolve(ScenarioDef s)
    {
        var declared = new List<(string Where, int Year)>();
        if (s.StartYear != 0) declared.Add(("startYear", s.StartYear));
        if (s.Events.StartYear != 0) declared.Add(("events.startYear", s.Events.StartYear));
        if (s.Objectives.StartYear != 0) declared.Add(("objectives.startYear", s.Objectives.StartYear));

        foreach (var (where, year) in declared)
            if (year < 0)
                throw new InvalidDataException($"{where} ne peut pas être négatif ({year}).");

        var distinct = declared.Select(d => d.Year).Distinct().ToList();
        if (distinct.Count > 1)
            throw new InvalidDataException(
                "Un scénario n'a qu'un calendrier, et ses années se contredisent : " +
                string.Join(", ", declared.Select(d => $"{d.Where} = {d.Year}")) + ".");

        int startYear = distinct.Count == 1 ? distinct[0] : 0;
        if (startYear > 0)
        {
            if (s.Events.StartYear == 0) s.Events.StartYear = startYear;
            if (s.Objectives.StartYear == 0) s.Objectives.StartYear = startYear;
        }
        return startYear;
    }
}
