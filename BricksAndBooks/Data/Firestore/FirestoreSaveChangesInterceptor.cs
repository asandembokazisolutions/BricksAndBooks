using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BricksAndBooks.Data.Firestore;

/// <summary>
/// Ensures the in-memory EF compatibility cache is never treated as durable storage.
/// Every tracked insert, update, and delete is first applied to Firestore.
/// </summary>
public sealed class FirestoreSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly FirestorePersistenceService _persistence;

    public FirestoreSaveChangesInterceptor(FirestorePersistenceService persistence)
    {
        _persistence = persistence;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Persist(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await Persist(eventData.Context, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private Task Persist(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return Task.CompletedTask;
        }

        return _persistence.PersistChangesAsync(context.ChangeTracker.Entries(), cancellationToken);
    }
}
