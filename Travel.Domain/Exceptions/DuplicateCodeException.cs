namespace Travel.Domain.Exceptions
{
    /// <summary>
    /// Raised when a generated entity code collides with one already in the database — two callers read the
    /// same last row before either saved. Translated from the SQL unique-index violation by
    /// <c>UnitOfWork.CompleteAsync</c>; the create path retries with a fresh code.
    /// </summary>
    public class DuplicateCodeException : Exception
    {
        public DuplicateCodeException(string message) : base(message) { }

        public DuplicateCodeException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
