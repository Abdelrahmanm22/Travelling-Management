namespace Travel.Domain.Models
{
    /// <summary>Reference list of guest nationalities, maintained by the operator and picked on a <see cref="Booking"/>.</summary>
    public class Nationality : BaseModel
    {
        public string Name { get; set; } = string.Empty;
    }
}
