namespace Travel.Domain.Services.CodeGeneration
{
    /// <summary>
    /// One prefix per coded entity type — the assumption that lets
    /// <see cref="ICodeGeneratorService"/> read a single row to find the last issued number.
    /// </summary>
    public static class CodePrefixes
    {
        public const string Booking = "MUSB";
        public const string Package = "MUSP";
    }
}
