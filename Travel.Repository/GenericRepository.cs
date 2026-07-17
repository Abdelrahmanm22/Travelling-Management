using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Travel.Domain.Models;
using Travel.Domain.Repositories;
using Travel.Domain.Specifications;
using Travel.Repository.Data;

namespace Travel.Repository
{
    public class GenericRepository<T> : IGenericRepository<T> where T : BaseModel
    {
        private readonly TravelContext dbContext;

        public GenericRepository(TravelContext dbContext)
        {
            this.dbContext = dbContext;
        }
        public async Task<IEnumerable<T>> GetAllAsync(ISpecifications<T> Spec)
        {
            return await SpecificationEvalutor<T>.GetQuery(dbContext.Set<T>(), Spec).ToListAsync();
        }

        public async Task<T> GetByIdAsync(ISpecifications<T> Spec)
        {
            return await SpecificationEvalutor<T>.GetQuery(dbContext.Set<T>(), Spec).FirstOrDefaultAsync();
        }
        public async Task AddAsync(T entity)
        {
            await dbContext.Set<T>().AddAsync(entity);
        }

        public void Update(T entity)
        {
            dbContext.Set<T>().Update(entity);
        }

        public void Delete(T entity)
        {
            dbContext.Set<T>().Remove(entity);
        }
    }
}
