using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Travel.Domain.Models;

namespace Travel.Domain.Specifications
{
    public class BaseSpecifications<T> : ISpecifications<T> where T : BaseModel
    {
        public Expression<Func<T, bool>> Criteria { get; set; }
        public List<Expression<Func<T, object>>> Includes { get; set; } = new List<Expression<Func<T, object>>>(); // instead of repeating this line in both constructors, we can initialize it here directly. This way, it will be initialized regardless of which constructor is used.
        public List<string> IncludeStrings { get; set; } = new List<string>();
        public Expression<Func<T, object>> OrderBy { get; set; }
        public Expression<Func<T, object>> OrderByDesc { get; set; }
        public int Take { get; set; }
        public int Skip { get; set; }
        public bool IsPaginationEnabled { get; set; }

        //Get All
        public BaseSpecifications()
        {
            //Includes = new List<Expression<Func<T, object>>>();
        }
        //Get By Id
        public BaseSpecifications(Expression<Func<T, bool>> cr)
        {
            Criteria = cr;
            //Includes = new List<Expression<Func<T, object>>>();
        }
        public void AddInclude(string includeString)
        {
            IncludeStrings.Add(includeString);
        }
        public void SetOrderBy(Expression<Func<T, object>> orderByExpression)
        {
            OrderBy = orderByExpression;
        }
        public void SetOrderByDesc(Expression<Func<T, object>> orderByDescExpression)
        {
            OrderByDesc = orderByDescExpression;
        }
        public void ApplyPagination(int skip, int take)
        {
            Skip = skip;
            Take = take;
            IsPaginationEnabled = true;
        }
    }
}
