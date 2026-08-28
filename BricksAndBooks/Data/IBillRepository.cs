using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public interface IBillRepository : IRepositoryBase<Bill>
    {
        // We might want to get bills for a specific unit or user, but we can do that in the service layer or controller.
        // For now, we stick to the base.
    }
}