using BricksAndBooks.Data.DataAccess;
using Google.Cloud.Firestore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksAndBooks.Infrastructure;

/// <summary>
/// Creates contexts for the compatibility cache without FirestoreSaveChangesInterceptor.
///
/// The main application context and this factory must be configured with the same named
/// <see cref="InMemoryDatabaseRoot"/>.  This is deliberate: client-originated receipt
/// changes are copied into the legacy cache, but must never be persisted back to Firestore
/// by the server-side save interceptor.
/// </summary>
public interface IFirestoreClientCacheContextFactory
{
    AppDbContext CreateDbContext();
}

public sealed class FirestoreClientCacheContextFactory : IFirestoreClientCacheContextFactory
{
    public const string CacheDatabaseName = "BricksAndBooksCompatibilityCache";

    private readonly DbContextOptions<AppDbContext> _options;

    public FirestoreClientCacheContextFactory(InMemoryDatabaseRoot cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(cacheRoot);
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(CacheDatabaseName, cacheRoot)
            .Options;
    }

    public AppDbContext CreateDbContext() => new(_options);
}

/// <summary>
/// Pulls the two deliberately direct-client-mutable receipt fields from Firestore into the
/// short-lived EF compatibility cache before an MVC/API request runs.
///
/// The proposed Firestore rules permit a mobile client to change only <c>isRead</c> and
/// <c>readAt</c> on its own notification or received chat message.  Creation, deletion,
/// profile edits, applications, vehicles, maintenance, reviews, payments, and chat sends
/// remain protected backend operations.  Consequently this bridge must not attempt a broad
/// Firestore hydration or copy arbitrary document fields into the cache.
/// </summary>
public sealed class FirestoreClientSyncMiddleware
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);
    private static readonly Timestamp FirstPossibleReceipt = Timestamp.FromDateTime(DateTime.UnixEpoch);

    private readonly RequestDelegate _next;
    private readonly FirestoreClientReceiptSync _receiptSync;

    public FirestoreClientSyncMiddleware(RequestDelegate next, FirestoreClientReceiptSync receiptSync)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _receiptSync = receiptSync ?? throw new ArgumentNullException(nameof(receiptSync));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _receiptSync.SynchronizeAsync(context.RequestAborted);
        await _next(context);
    }

    /// <summary>
    /// A singleton, serialized receipt synchronizer.  It intentionally has no dependency on
    /// FirestorePersistenceService: using that service would make a cache update look like a
    /// server mutation and could overwrite a newer Firestore document.
    /// </summary>
    public sealed class FirestoreClientReceiptSync
    {
        private readonly FirestoreDb _firestore;
        private readonly IFirestoreClientCacheContextFactory _cacheContexts;
        private readonly ILogger<FirestoreClientReceiptSync> _logger;
        private readonly SemaphoreSlim _sweepLock = new(1, 1);

        private Timestamp _notificationWatermark = FirstPossibleReceipt;
        private Timestamp _chatWatermark = FirstPossibleReceipt;
        private DateTimeOffset _nextSweepUtc = DateTimeOffset.MinValue;

        public FirestoreClientReceiptSync(
            FirestoreDb firestore,
            IFirestoreClientCacheContextFactory cacheContexts,
            ILogger<FirestoreClientReceiptSync> logger)
        {
            _firestore = firestore ?? throw new ArgumentNullException(nameof(firestore));
            _cacheContexts = cacheContexts ?? throw new ArgumentNullException(nameof(cacheContexts));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
        {
            if (DateTimeOffset.UtcNow < _nextSweepUtc)
            {
                return;
            }

            await _sweepLock.WaitAsync(cancellationToken);
            try
            {
                if (DateTimeOffset.UtcNow < _nextSweepUtc)
                {
                    return;
                }

                var notificationReceipts = await ReadReceiptsAsync("notifications", _notificationWatermark, cancellationToken);
                var chatReceipts = await ReadReceiptsAsync("chatMessages", _chatWatermark, cancellationToken);

                // Cache entities are created only by the protected backend.  A missing entity is
                // never inserted here: that would recreate a document that may have been deleted,
                // and would defeat the one-time startup hydration invariant.
                await using var cache = _cacheContexts.CreateDbContext();
                var notificationWatermark = await ApplyNotificationReceiptsAsync(cache, notificationReceipts, cancellationToken);
                var chatWatermark = await ApplyChatReceiptsAsync(cache, chatReceipts, cancellationToken);

                if (cache.ChangeTracker.HasChanges())
                {
                    // This factory has no FirestoreSaveChangesInterceptor.  SaveChanges writes only
                    // to the shared in-memory compatibility cache; no Firebase write is issued.
                    await cache.SaveChangesAsync(cancellationToken);
                }

                _notificationWatermark = LaterOf(_notificationWatermark, notificationWatermark);
                _chatWatermark = LaterOf(_chatWatermark, chatWatermark);
                _nextSweepUtc = DateTimeOffset.UtcNow.Add(SweepInterval);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Do not block a backend action behind a best-effort receipt refresh.  The
                // watermark remains unchanged, so a later request safely retries the same docs.
                _nextSweepUtc = DateTimeOffset.UtcNow.Add(SweepInterval);
                _logger.LogError(exception, "Could not synchronize direct Firestore read receipts into the compatibility cache.");
            }
            finally
            {
                _sweepLock.Release();
            }
        }

        private async Task<IReadOnlyList<Receipt>> ReadReceiptsAsync(
            string collection,
            Timestamp watermark,
            CancellationToken cancellationToken)
        {
            // >= deliberately replays the document at the high-water mark.  Firestore timestamps
            // can be equal for separate writes; applying a read receipt is idempotent and replaying
            // it avoids dropping a same-timestamp update.
            var snapshot = await _firestore.Collection(collection)
                .WhereGreaterThanOrEqualTo("readAt", watermark)
                .OrderBy("readAt")
                .GetSnapshotAsync(cancellationToken);

            var receipts = new List<Receipt>();
            foreach (var document in snapshot.Documents)
            {
                var data = document.ToDictionary();
                if (!TryGetReceipt(document.Id, data, out var receipt))
                {
                    _logger.LogWarning(
                        "Ignoring malformed {Collection} receipt document {DocumentId}; it has not been copied to the compatibility cache.",
                        collection,
                        document.Id);
                    continue;
                }

                receipts.Add(receipt);
            }

            return receipts;
        }

        private async Task<Timestamp> ApplyNotificationReceiptsAsync(
            AppDbContext cache,
            IReadOnlyList<Receipt> receipts,
            CancellationToken cancellationToken)
        {
            var watermark = _notificationWatermark;
            foreach (var receipt in receipts)
            {
                watermark = LaterOf(watermark, receipt.ReadAt);
                var notification = await cache.Notifications.SingleOrDefaultAsync(item => item.Id == receipt.Id, cancellationToken);
                if (notification is null)
                {
                    _logger.LogWarning(
                        "Notification {NotificationId} was not in the hydrated compatibility cache; skipping client receipt rather than recreating it.",
                        receipt.Id);
                    continue;
                }

                if (!notification.IsRead || notification.ReadAt != receipt.ReadAt.ToDateTime())
                {
                    notification.IsRead = true;
                    notification.ReadAt = receipt.ReadAt.ToDateTime();
                }
            }

            return watermark;
        }

        private async Task<Timestamp> ApplyChatReceiptsAsync(
            AppDbContext cache,
            IReadOnlyList<Receipt> receipts,
            CancellationToken cancellationToken)
        {
            var watermark = _chatWatermark;
            foreach (var receipt in receipts)
            {
                watermark = LaterOf(watermark, receipt.ReadAt);
                var message = await cache.ChatMessages.SingleOrDefaultAsync(item => item.Id == receipt.Id, cancellationToken);
                if (message is null)
                {
                    _logger.LogWarning(
                        "Chat message {MessageId} was not in the hydrated compatibility cache; skipping client receipt rather than recreating it.",
                        receipt.Id);
                    continue;
                }

                if (!message.IsRead || message.ReadAt != receipt.ReadAt.ToDateTime())
                {
                    message.IsRead = true;
                    message.ReadAt = receipt.ReadAt.ToDateTime();
                }
            }

            return watermark;
        }

        private static bool TryGetReceipt(string documentId, IReadOnlyDictionary<string, object> data, out Receipt receipt)
        {
            receipt = default;
            if (!TryGetInt(data, "id", out var id) && !int.TryParse(documentId, out id))
            {
                return false;
            }

            if (!data.TryGetValue("isRead", out var rawIsRead) || rawIsRead is not true
                || !data.TryGetValue("readAt", out var rawReadAt))
            {
                return false;
            }

            var readAt = rawReadAt switch
            {
                Timestamp timestamp => timestamp,
                DateTime dateTime when dateTime.Kind == DateTimeKind.Utc => Timestamp.FromDateTime(dateTime),
                DateTime dateTime => Timestamp.FromDateTime(dateTime.ToUniversalTime()),
                DateTimeOffset dateTimeOffset => Timestamp.FromDateTime(dateTimeOffset.UtcDateTime),
                _ => default
            };
            if (readAt == default)
            {
                return false;
            }

            receipt = new Receipt(id, readAt);
            return true;
        }

        private static bool TryGetInt(IReadOnlyDictionary<string, object> data, string key, out int result)
        {
            result = default;
            if (!data.TryGetValue(key, out var raw) || raw is null)
            {
                return false;
            }

            try
            {
                result = Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
                return result > 0;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static Timestamp LaterOf(Timestamp first, Timestamp second) =>
            first.ToDateTime() >= second.ToDateTime() ? first : second;

        private readonly record struct Receipt(int Id, Timestamp ReadAt);
    }
}
