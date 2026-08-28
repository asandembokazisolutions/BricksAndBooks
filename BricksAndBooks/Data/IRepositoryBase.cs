using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BricksAndBooks.Data
{
    public interface IRepositoryBase<T> where T : class
    {
        T FindByCondition(object id);
        IQueryable<T> FindAll();
        IQueryable<T> FindByCondition(System.Linq.Expressions.Expression<Func<T, bool>> expression);
        void Create(T entity);
        void Update(T entity);
        void Delete(T entity);
    }
}