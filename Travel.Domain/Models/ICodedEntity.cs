namespace Travel.Domain.Models
{
    /// <summary>
    /// Marker for entities that carry an auto-generated sequential business code
    /// (e.g. "MUSB-000001"). Issued once on create by
    /// <see cref="Services.CodeGeneration.ICodeGeneratorService"/> and never reassigned.
    /// </summary>
    public interface ICodedEntity
    {
        string Code { get; set; }
    }
}
