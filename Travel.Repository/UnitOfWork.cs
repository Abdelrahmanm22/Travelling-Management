using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using Travel.Domain;
using Travel.Domain.Models;
using Travel.Domain.Repositories;
using Travel.Repository.Data;

namespace Travel.Repository
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly TravelContext dbContext;
        private Hashtable _repositories;
        private IDbContextTransaction? _currentTransaction;

        public UnitOfWork(TravelContext dbContext)
        {
            this.dbContext = dbContext;
            _repositories = new Hashtable();
        }

        public async Task<int> CompleteAsync()
        {
            return await dbContext.SaveChangesAsync();
        }

        // ─── Transaction Management ────────────────────────────────────────────
        // Ensures a group of DB operations behaves as one atomic unit:
        //   Either all operations succeed (CommitTransactionAsync),
        //   or all operations fail and rollback (RollbackTransactionAsync).

        public async Task BeginTransactionAsync()
        {
            _currentTransaction = await dbContext.Database.BeginTransactionAsync();
        }

        public async Task CommitTransactionAsync()
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.CommitAsync();
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.RollbackAsync();
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }

        // ─── Dispose ──────────────────────────────────────────────────────────

        public async ValueTask DisposeAsync()
        {
            // Rollback any open transaction before disposing to prevent connection leaks
            if (_currentTransaction != null)
            {
                await _currentTransaction.RollbackAsync();
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
            await dbContext.DisposeAsync();
        }

        public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : BaseModel
        {
            var type = typeof(TEntity).Name; // Get the name of the entity type (e.g., "Category", "Item", etc.)
            if (!_repositories.ContainsKey(type))
            {
                var Repo = new GenericRepository<TEntity>(dbContext); // Create a new instance of the generic repository for the specified entity type.
                _repositories.Add(type, Repo); // Add the repository instance to the hashtable with the entity type name as the key.
            }
            return _repositories[type] as IGenericRepository<TEntity>; // Return the repository instance from the hashtable, cast to the appropriate type.
        }
    }
}
