using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public interface IAccommodationRepository : IRepositoryBase<Accommodation>
    {
        // We can add specific methods for Accommodation if needed.
        // For now, we'll just use the base.
    }
}