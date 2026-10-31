using Microsoft.Extensions.Logging;
using Travel.Domain;
using Travel.Domain.Exceptions;
using Travel.Domain.Models;
using Travel.Domain.Services.CodeGeneration;
using Travel.Domain.Specifications.CodeGeneration;

namespace Travel.Services.CodeGeneration
{
    /// <inheritdoc cref="ICodeGeneratorService" />
    public class CodeGeneratorService : ICodeGeneratorService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<CodeGeneratorService> _logger;

        public CodeGeneratorService(IUnitOfWork unitOfWork, ILogger<CodeGeneratorService> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<string> GenerateCodeAsync<TEntity>(string prefix)
            where TEntity : BaseModel, ICodedEntity
        {
            // One row, not the whole table. See LastCodedEntitySpec for why it orders by Id, not Code.
            var last = await _unitOfWork.Repository<TEntity>()
                .GetByIdAsync(new LastCodedEntitySpec<TEntity>());

            var prefixWithDash = $"{prefix}-";

            var lastNumber = last?.Code is not null
                             && last.Code.StartsWith(prefixWithDash)
                             && int.TryParse(last.Code[prefixWithDash.Length..], out var parsed)
                ? parsed
                : 0;

            var next = lastNumber + 1;

            // 6-digit padding for numbers within 1–999,999; plain number above that.
            return next <= 999_999
                ? $"{prefixWithDash}{next:D6}"
                : $"{prefixWithDash}{next}";
        }

        /// <inheritdoc />
        public async Task AddWithCodeAsync<TEntity>(TEntity entity, string prefix, int maxAttempts = 3)
            where TEntity : BaseModel, ICodedEntity
        {
            // Added once — a failed SaveChanges leaves the entity (and its graph) tracked as Added,
            // so a retry only needs a fresh code.
            await _unitOfWork.Repository<TEntity>().AddAsync(entity);

            for (var attempt = 1; ; attempt++)
            {
                entity.Code = await GenerateCodeAsync<TEntity>(prefix);

                try
                {
                    await _unitOfWork.CompleteAsync();
                    return;
                }
                catch (DuplicateCodeException) when (attempt < maxAttempts)
                {
                    // Another caller took this code between our read and our write — try the next one.
                    _logger.LogWarning("{Entity} code {Code} was taken concurrently; retrying (attempt {Attempt} of {MaxAttempts})",
                        typeof(TEntity).Name, entity.Code, attempt, maxAttempts);
                }
            }
        }
    }
}
