namespace Travel.Domain.Models
{
    /// <summary>A driver an internal movement is assigned to. Only active drivers are offered.</summary>
    public class Driver : BaseModel
    {
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
