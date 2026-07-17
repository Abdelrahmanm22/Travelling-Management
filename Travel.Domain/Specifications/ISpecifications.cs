using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications
{
    public interface ISpecifications<T> where T : BaseModel
    {
        //Main Query for example ==> _dbContext.Products.Where(P => P.Id == id).Include(P => P.ProductType).Include(P => P.ProductBrand);

        // Signature of property for ==> where condition [Where(P => P.Id == id)]
        public Expression<Func<T, bool>> Criteria { get; set; }
        // Signature of property for ==> List of Include [Include(P => P.ProductType).Include(P => P.ProductBrand)]
        public List<Expression<Func<T, object>>> Includes { get; set; }
        // string includes  
        public List<string> IncludeStrings { get; set; }
        // Signature of property for ==> [OrderBy(P=>P.Name)]
        public Expression<Func<T, object>> OrderBy { get; set; }
        // Signature of property for ==> [OrderByDesc(P=>P.Name)]
        public Expression<Func<T, object>> OrderByDesc { get; set; }

        // Signature of property for Take
        public int Take { get; set; }
        // Signature of property for Skip
        public int Skip { get; set; }
        // Signature of property for IsPagingEnabled
        public bool IsPaginationEnabled { get; set; }
    }
}
