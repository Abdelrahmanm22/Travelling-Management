namespace Travel.Presentation.Logging
{
    /// <summary>
    /// Opts an action out of <see cref="ModelStateLoggingFilter"/> because it logs its own rejections
    /// with more context than the form errors carry (Login: which of the three reasons it was).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SkipModelStateLogAttribute : Attribute
    {
    }
}
