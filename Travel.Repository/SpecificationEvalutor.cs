using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Travel.Domain.Models;
using Travel.Domain.Specifications;

namespace Travel.Repository
{
    public class SpecificationEvalutor<T> where T : BaseModel
    {
        //Function To build Query
        /// [ _dbContext.Products.Where(P => P.Id == id).Include(P => P.ProductType).Include(P => P.ProductBrand); ]
        public static IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecifications<T> spec)
        {
            var query = inputQuery; //_dbContext.Set<T>()
            if (spec.Criteria is not null)
            {
                query = query.Where(spec.Criteria); // _dbContext.Set<T>().Where(P => P.Id == id)
            }
            if (spec.OrderBy is not null)
            {
                query = query.OrderBy(spec.OrderBy); // _dbContext.Set<T>().Where(P => P.Id == id).OrderBy(P => P.Name)
            }
            if (spec.OrderByDesc is not null)
            {
                query = query.OrderByDescending(spec.OrderByDesc); // _dbContext.Set<T>().Where(P => P.Id == id).OrderByDescending(P => P.Name)
            }
            if (spec.IsPaginationEnabled)
            {
                query = query.Skip(spec.Skip).Take(spec.Take); // _dbContext.Set<T>().Where(P => P.Id == id).OrderByDescending(P => P.Name).Skip(40).Take(10)
            }
            query = spec.Includes.Aggregate(query, (CurrentQuery, IncludeExpression) => CurrentQuery.Include(IncludeExpression)); ///_dbContext.Products.Where(P => P.Id == id).Include(P => P.ProductType).Include(P => P.ProductBrand);
            query = spec.IncludeStrings.Aggregate(query, (CurrentQuery, IncludeString) => CurrentQuery.Include(IncludeString)); ///// String-based includes (handles ThenInclude chains)
            return query;
        }
    }
}
