namespace Travel.Presentation.ViewModels
{
    /// <summary>
    /// The domestic-flight modal on <c>BlockService/Details</c>, posted as a plain multipart form (the
    /// files travel with it). Every rule is checked in <c>BlockServiceController.ValidateFlight</c> —
    /// the modal's own pre-check only saves the operator a round trip that would empty the dropzone.
    /// </summary>
    public class BlockFlightInput
    {
        public string? FlightNumber { get; set; }
        public DateOnly? FlightDate { get; set; }
        public TimeOnly? FlightTime { get; set; }

        /// <summary>Repeated <c>name="BookingIds"</c> checkboxes — never <c>asp-for</c>.</summary>
        public List<int> BookingIds { get; set; } = new();

        /// <summary>Edit only: the existing files the operator ticked for removal.</summary>
        public List<int> RemoveFileIds { get; set; } = new();
    }
}
