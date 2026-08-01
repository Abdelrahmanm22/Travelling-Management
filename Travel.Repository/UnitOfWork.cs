using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Travel.Domain;
using Travel.Domain.Exceptions;
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
            try
            {
                return await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsCodeUniqueViolation(ex))
            {
                throw new DuplicateCodeException(
                    "The generated code was taken by a concurrent operation.", ex);
            }
        }

        /// <summary>
        /// A unique-index violation on a coded entity's Code column — SQL Server 2601 (unique index) or
        /// 2627 (unique constraint). The message carries the index name, which is why the code indexes are
        /// named explicitly ("IX_Bookings_Code") in their configs: it keeps this test from mistaking an
        /// unrelated unique index (e.g. IX_Packages_Name) for a code collision.
        /// </summary>
        private static bool IsCodeUniqueViolation(DbUpdateException ex)
            => ex.InnerException is SqlException sql
               && (sql.Number == 2601 || sql.Number == 2627)
               && sql.Message.Contains("_Code", StringComparison.OrdinalIgnoreCase);

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
