using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;

namespace BricksAndBooks.Infrastructure;

/// <summary>Mirrors only the source demonstration card metadata; it is not a payment gateway.</summary>
public sealed class FirebaseCardService
{
    private readonly AppDbContext _db;
    public FirebaseCardService(AppDbContext db) => _db=db;
    public async Task<FirebaseActionResult> RegisterAsync(IReadOnlyDictionary<string,JsonElement> fields,ApplicationUser user,ClaimsPrincipal principal)
    {
        if(!principal.IsInRole("Student")&&!principal.IsInRole("Staff"))return new(false,403,"Only resident accounts may register demonstration cards.");
        string Text(string k)=>NativeFirebaseQueries.Text(fields,k);
        var digits=new string(Text("cardNumber").Where(char.IsDigit).ToArray());
        var cvc=Text("cardCvc");var holder=Text("cardholderName").Trim();var expiry=Text("cardExpiry");
        if(holder.Length is <1 or >100 || digits.Length is <13 or >19 || cvc.Length is <3 or >4 || !cvc.All(char.IsDigit)
            ||!DateTime.TryParseExact(expiry,"MM/yy",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
            ||date<new DateTime(DateTime.UtcNow.Year,DateTime.UtcNow.Month,1))return new(false,400,"Provide a cardholder name, 13–19 digit demonstration card number, valid MM/YY expiry, and 3–4 digit security code.");
        var salt=RandomNumberGenerator.GetBytes(16);
        var card=new SavedPaymentCard{UserId=user.Id,CardholderName=holder,LastFour=digits[^4..],Expiry=expiry,CvcSalt=Convert.ToBase64String(salt),CvcHash=Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(cvc,salt,120000,HashAlgorithmName.SHA256,32))};
        _db.SavedPaymentCards.Add(card);await _db.SaveChangesAsync();
        return new(true,200,"Masked demonstration card saved.",NativeFirebaseQueries.Safe(card));
    }
}
