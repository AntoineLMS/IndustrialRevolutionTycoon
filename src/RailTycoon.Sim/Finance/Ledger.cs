namespace RailTycoon.Sim.Finance;

public enum AccountKind
{
    Asset,
    Liability,
    Equity,
}

/// <summary>Un compte du grand livre. Le solde porte son signe naturel : un actif et un passif sont tous deux positifs.</summary>
public sealed class Account
{
    public required string Id { get; init; }
    public required AccountKind Kind { get; init; }
    public decimal Balance;

    /// <summary>Nombre d'écritures passées sur ce compte, pour le diagnostic.</summary>
    public int Postings;
}

/// <summary>Un mouvement élémentaire, exprimé en débit : positif augmente un actif, diminue un passif ou des capitaux propres.</summary>
public readonly record struct Leg(string Account, decimal Debit);

/// <summary>
/// Grand livre en partie double. Chaque écriture est un ensemble de mouvements
/// dont la somme des débits est nulle.
/// <para>
/// <b>Pourquoi la partie double plutôt qu'un bilan recalculé.</b> On pourrait
/// tenir des champs séparés — caisse, dette, capitaux propres — puis vérifier
/// après coup que actif = passif + capitaux propres. Ce contrôle échoue alors
/// <em>parfois</em>, et il faut retrouver laquelle des quarante lignes de code qui
/// touchent à l'argent a oublié sa contrepartie. Ici c'est impossible : une
/// écriture déséquilibrée est refusée à l'instant où elle est passée, avec le nom
/// de l'opération fautive. L'identité comptable n'est plus une propriété qu'on
/// espère, c'est une conséquence du type.
/// </para>
/// <para>
/// Corollaire : <see cref="Residual"/> vaut exactement zéro, pour toujours. Un
/// invariant qui compare à zéro sans tolérance ne peut pas se dégrader en
/// silence.
/// </para>
/// </summary>
public sealed class Ledger
{
    public required string OwnerId { get; init; }

    private readonly List<Account> _accounts = new();
    private readonly Dictionary<string, Account> _byId = new();

    /// <summary>Comptes dans leur ordre d'ouverture. Ordre de parcours canonique : un dictionnaire n'en garantit aucun.</summary>
    public IReadOnlyList<Account> Accounts => _accounts;

    public int EntryCount { get; private set; }

    /// <summary>Libellé de la dernière écriture. Seul indice utile quand un invariant casse.</summary>
    public string LastEntry { get; private set; } = "(aucune)";

    public Account Open(string id, AccountKind kind)
    {
        if (_byId.ContainsKey(id))
            throw new InvalidOperationException($"Compte déjà ouvert : {OwnerId}/{id}.");
        var account = new Account { Id = id, Kind = kind };
        _accounts.Add(account);
        _byId[id] = account;
        return account;
    }

    public decimal this[string id] => _byId[id].Balance;

    public bool Has(string id) => _byId.ContainsKey(id);

    /// <summary>Écriture à deux mouvements : <paramref name="amount"/> passe du compte <paramref name="from"/> au compte <paramref name="to"/>, en débit.</summary>
    public void Post(string label, string to, string from, decimal amount)
    {
        if (amount == 0m) return;
        // Un montant négatif inverserait silencieusement le sens de l'écriture.
        // C'est mathématiquement cohérent et humainement indéchiffrable : à
        // l'appelant de nommer le sens qu'il veut.
        if (amount < 0m)
            throw new InvalidOperationException(
                $"Écriture « {label} » sur {OwnerId} : montant négatif {amount}. " +
                "Inverser les comptes plutôt que le signe.");
        Apply(label, to, amount);
        Apply(label, from, -amount);
        EntryCount++;
        LastEntry = label;
    }

    /// <summary>Écriture générale. La somme des débits doit être nulle au centime près — c'est-à-dire exactement nulle.</summary>
    public void Post(string label, params Leg[] legs)
    {
        decimal sum = 0m;
        foreach (var leg in legs) sum += leg.Debit;
        if (sum != 0m)
            throw new InvalidOperationException(
                $"Écriture déséquilibrée « {label} » sur {OwnerId} : somme des débits = {sum}.");

        foreach (var leg in legs) Apply(label, leg.Account, leg.Debit);
        EntryCount++;
        LastEntry = label;
    }

    private void Apply(string label, string accountId, decimal debit)
    {
        if (debit == 0m) return;
        if (!_byId.TryGetValue(accountId, out var account))
            throw new InvalidOperationException(
                $"Écriture « {label} » sur un compte inconnu : {OwnerId}/{accountId}.");
        if (Money.Round(debit) != debit)
            throw new InvalidOperationException(
                $"Écriture « {label} » sur {OwnerId}/{accountId} : {debit} n'est pas un nombre entier de centimes.");

        account.Balance += account.Kind == AccountKind.Asset ? debit : -debit;
        account.Postings++;
    }

    public decimal Total(AccountKind kind)
    {
        decimal sum = 0m;
        foreach (var account in _accounts)
            if (account.Kind == kind) sum += account.Balance;
        return sum;
    }

    public decimal Assets => Total(AccountKind.Asset);
    public decimal Liabilities => Total(AccountKind.Liability);
    public decimal Equity => Total(AccountKind.Equity);

    /// <summary>Actif − (passif + capitaux propres). Vaut zéro par construction ; l'invariant le vérifie quand même.</summary>
    public decimal Residual => Assets - Liabilities - Equity;
}
