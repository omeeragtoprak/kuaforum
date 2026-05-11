namespace Kuaforum.Domain.Common;

public abstract class AggregateRoot : Entity
{
    public uint Version { get; protected set; }

    /// <summary>
    /// Aggregate mutation Touch'ı çağırır → UpdatedAt + Version bump.
    /// Version EF concurrency token; bump etmezsek WHERE clause hep aynı kalır
    /// ve aynı entity üzerinde art arda iki SaveChanges 0 row affected verir
    /// (DbUpdateConcurrencyException). Bkz. Review approve + reply senaryosu.
    /// </summary>
    protected override void Touch()
    {
        base.Touch();
        Version++;
    }
}
