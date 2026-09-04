using System.Collections;
using System.Globalization;
using System.Reflection;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;
using Google.Cloud.Firestore;
using Google.Protobuf;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BricksAndBooks.Data.Firestore;

/// <summary>
/// Persists the application's EF-tracked entities to Cloud Firestore and restores them
/// into the short-lived in-memory EF cache at application startup. Firestore is the
/// durable source of record; the EF cache exists only to preserve the existing MVC,
/// repository, and ASP.NET Identity code paths during the migration.
/// </summary>
public sealed class FirestorePersistenceService
{
    private static readonly IReadOnlyDictionary<Type, string> CollectionNames = new Dictionary<Type, string>
    {
        [typeof(Accommodation)] = "accommodations",
        [typeof(AccommodationApplication)] = "accommodationApplications",
        [typeof(Announcement)] = "announcements",
        [typeof(AuditLog)] = "auditLogs",
        [typeof(Bill)] = "bills",
        [typeof(ChatMessage)] = "chatMessages",
        [typeof(MaintenanceRequest)] = "maintenanceRequests",
        [typeof(Notification)] = "notifications",
        [typeof(Occupancy)] = "occupancies",
        [typeof(Payment)] = "payments",
        [typeof(PaymentAllocation)] = "paymentAllocations",
        // Internal workflow records for users who request a password reset from the login page.
        [typeof(PasswordResetRequest)] = "passwordResetRequests",
        [typeof(Review)] = "reviews",
        [typeof(Tariff)] = "tariffs",
        [typeof(Unit)] = "units",
        [typeof(Vehicle)] = "vehicles",
        // These collections are server-only. Firestore security rules do not expose them.
        [typeof(ApplicationUser)] = "_identityUsers",
        [typeof(IdentityRole)] = "_identityRoles",
        [typeof(IdentityUserRole<string>)] = "_identityUserRoles"
    };

    private readonly FirestoreDb _database;
    private readonly ILogger<FirestorePersistenceService> _logger;

    public FirestorePersistenceService(FirestoreDb database, ILogger<FirestorePersistenceService> logger)
    {
        _database = database;
        _logger = logger;
    }

    /// <summary>
    /// True only while HydrateAsync's own SaveChangesAsync call is in flight. The rows being
    /// saved at that point were just read from Firestore, so FirestoreSaveChangesInterceptor
    /// must not write them straight back out again.
    /// </summary>
    public bool IsHydrating { get; private set; }

    public async Task HydrateAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        foreach (var mapping in CollectionNames)
        {
            var snapshot = await _database.Collection(mapping.Value).GetSnapshotAsync(cancellationToken);
            foreach (var document in snapshot.Documents)
            {
                var entity = Activator.CreateInstance(mapping.Key);
                if (entity is null)
                {
                    throw new InvalidOperationException($"Unable to create {mapping.Key.Name} while loading Firestore data.");
                }

                PopulateEntity(entity, document.ToDictionary());
                context.Add(entity);
            }
        }

        if (context.ChangeTracker.HasChanges())
        {
            IsHydrating = true;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                IsHydrating = false;
            }
        }
    }

    public async Task PersistChangesAsync(IEnumerable<EntityEntry> entries, CancellationToken cancellationToken = default)
    {
        if (IsHydrating)
        {
            return;
        }

        foreach (var entry in entries.Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (!CollectionNames.TryGetValue(entry.Metadata.ClrType, out var collectionName))
            {
                continue;
            }

            var documentId = GetDocumentId(entry.Entity);
            var document = _database.Collection(collectionName).Document(documentId);

            if (entry.State == EntityState.Deleted)
            {
                await document.DeleteAsync(cancellationToken: cancellationToken);
                if (entry.Entity is ApplicationUser deletedUser)
                {
                    await _database.Collection("users").Document(deletedUser.Id).DeleteAsync(cancellationToken: cancellationToken);
                }
                continue;
            }

            await document.SetAsync(ToDocument(entry.Entity), SetOptions.Overwrite, cancellationToken);
            if (entry.Entity is ApplicationUser user)
            {
                await _database.Collection("users").Document(user.Id)
                    .SetAsync(ToPublicUserProfile(user), SetOptions.Overwrite, cancellationToken);
            }
        }
    }

    public async Task EnsureMetadataAsync(CancellationToken cancellationToken = default)
    {
        var timestamp = Timestamp.GetCurrentTimestamp();
        await _database.Collection("settings").Document("app").SetAsync(new Dictionary<string, object>
        {
            ["appName"] = "Bricks And Books",
            ["schemaVersion"] = 3,
            ["supportedRoles"] = new[] { "student", "staff", "financialOfficer", "admin" },
            ["updatedAt"] = timestamp
        }, SetOptions.MergeAll, cancellationToken);

        await _database.Collection("schemaMetadata").Document("v3").SetAsync(new Dictionary<string, object>
        {
            ["version"] = 3,
            ["projectId"] = _database.ProjectId,
            ["databaseId"] = _database.DatabaseId,
            ["collections"] = CollectionNames.Values.Where(name => !name.StartsWith('_')).Append("users").Append("settings").Append("schemaMetadata").OrderBy(name => name).ToArray(),
            ["updatedAt"] = timestamp
        }, SetOptions.MergeAll, cancellationToken);
    }

    private static string GetDocumentId(object entity)
    {
        if (entity is IdentityUserRole<string> userRole)
        {
            return $"{userRole.UserId}__{userRole.RoleId}";
        }

        var id = entity.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entity)?.ToString();
        if (string.IsNullOrWhiteSpace(id) || id == "0")
        {
            throw new InvalidOperationException($"A non-empty Id is required before persisting {entity.GetType().Name} to Firestore.");
        }

        return id;
    }

    private static Dictionary<string, object> ToDocument(object entity)
    {
        var document = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in GetPersistableProperties(entity.GetType()))
        {
            var value = property.GetValue(entity);
            document[ToCamelCase(property.Name)] = ToFirestoreValue(value, property.PropertyType)!;
        }
        return document;
    }

    private static Dictionary<string, object> ToPublicUserProfile(ApplicationUser user)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["email"] = user.Email ?? string.Empty,
            ["userName"] = user.UserName ?? string.Empty,
            ["firstName"] = user.FirstName ?? string.Empty,
            ["lastName"] = user.LastName ?? string.Empty,
            ["studentNumber"] = user.StudentNumber ?? string.Empty,
            ["employeeNumber"] = user.EmployeeNumber ?? string.Empty,
            ["isStudent"] = user.IsStudent,
            ["isStaff"] = user.IsStaff,
            ["isFinancialOfficer"] = user.IsFinancialOfficer,
            ["isAdmin"] = user.IsAdmin,
            ["role"] = GetPrimaryRole(user),
            ["updatedAt"] = Timestamp.GetCurrentTimestamp()
        };
    }

    private static string GetPrimaryRole(ApplicationUser user) => user.IsAdmin
        ? "admin"
        : user.IsFinancialOfficer
            ? "financialOfficer"
            : user.IsStaff
                ? "staff"
                : "student";

    private static IEnumerable<PropertyInfo> GetPersistableProperties(Type entityType) => entityType
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
        .Where(property => IsFirestoreScalar(property.PropertyType));

    private static bool IsFirestoreScalar(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType.IsPrimitive
               || effectiveType.IsEnum
               || effectiveType == typeof(string)
               || effectiveType == typeof(decimal)
               || effectiveType == typeof(DateTime)
               || effectiveType == typeof(DateTimeOffset)
               || effectiveType == typeof(Guid)
               || effectiveType == typeof(byte[]);
    }

    private static object? ToFirestoreValue(object? value, Type propertyType)
    {
        if (value is null)
        {
            return null;
        }

        var effectiveType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (effectiveType.IsEnum)
        {
            return value.ToString();
        }

        return value switch
        {
            decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
            DateTime dateTimeValue => dateTimeValue.Kind == DateTimeKind.Utc
                ? dateTimeValue
                : dateTimeValue.ToUniversalTime(),
            DateTimeOffset dateTimeOffsetValue => dateTimeOffsetValue.UtcDateTime,
            Guid guidValue => guidValue.ToString("D"),
            _ => value
        };
    }

    private static void PopulateEntity(object entity, IReadOnlyDictionary<string, object> document)
    {
        foreach (var property in GetPersistableProperties(entity.GetType()))
        {
            if (!document.TryGetValue(ToCamelCase(property.Name), out var rawValue))
            {
                continue;
            }

            property.SetValue(entity, FromFirestoreValue(rawValue, property.PropertyType));
        }
    }

    private static object? FromFirestoreValue(object? rawValue, Type propertyType)
    {
        if (rawValue is null)
        {
            return null;
        }

        var effectiveType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (effectiveType.IsEnum)
        {
            return Enum.Parse(effectiveType, rawValue.ToString()!, ignoreCase: true);
        }

        if (effectiveType == typeof(DateTime))
        {
            return rawValue switch
            {
                Timestamp timestamp => timestamp.ToDateTime(),
                DateTime dateTime => dateTime,
                _ => DateTime.Parse(rawValue.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            };
        }

        if (effectiveType == typeof(DateTimeOffset))
        {
            return rawValue switch
            {
                Timestamp timestamp => new DateTimeOffset(timestamp.ToDateTime()),
                DateTimeOffset dateTimeOffset => dateTimeOffset,
                DateTime dateTime => new DateTimeOffset(dateTime),
                _ => DateTimeOffset.Parse(rawValue.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            };
        }

        if (effectiveType == typeof(decimal))
        {
            return decimal.Parse(rawValue.ToString()!, CultureInfo.InvariantCulture);
        }

        if (effectiveType == typeof(Guid))
        {
            return Guid.Parse(rawValue.ToString()!);
        }

        if (effectiveType == typeof(byte[]) && rawValue is ByteString bytes)
        {
            return bytes.ToByteArray();
        }

        return Convert.ChangeType(rawValue, effectiveType, CultureInfo.InvariantCulture);
    }

    private static string ToCamelCase(string value) => string.IsNullOrEmpty(value)
        ? value
        : char.ToLowerInvariant(value[0]) + value[1..];
}
