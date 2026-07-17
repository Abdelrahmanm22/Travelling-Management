using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Travel.Domain.Models;
using Travel.Domain.Repositories;

namespace Travel.Domain
{
    public interface IUnitOfWork : IAsyncDisposable
    {
        IGenericRepository<TEntity> Repository<TEntity>() where TEntity : BaseModel; // Generic method to get repository for any entity type
        Task<int> CompleteAsync();
        /* Transaction management — ensures a group of DB operations behaves as one atomic unit.
         Either all operations succeed (Commit), or all operations fail and rollback (Rollback).
        */
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
    }
}
