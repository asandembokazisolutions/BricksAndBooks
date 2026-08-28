using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Data
{
    public class RepositoryBase<T> : IRepositoryBase<T> where T : class
    {
        protected BricksAndBooks.Data.DataAccess.AppDbContext _dbContext;

        public RepositoryBase(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public T FindByCondition(object id)
        {
            return _dbContext.Set<T>().Find(id);
        }

        public IQueryable<T> FindAll()
        {
            return _dbContext.Set<T>().AsNoTracking();
        }

        public IQueryable<T> FindByCondition(System.Linq.Expressions.Expression<Func<T, bool>> expression)
        {
            return _dbContext.Set<T>().Where(expression).AsNoTracking();
        }

        public void Create(T entity)
        {
            _dbContext.Set<T>().Add(entity);
        }

        public void Update(T entity)
        {
            _dbContext.Set<T>().Update(entity);
        }

        public void Delete(T entity)
        {
            _dbContext.Set<T>().Remove(entity);
        }
    }
}