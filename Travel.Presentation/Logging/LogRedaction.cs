using System.Text.RegularExpressions;

namespace Travel.Presentation.Logging
{
    /// <summary>
    /// Log files keep no guest personal data (see Logging in CLAUDE.md), but a mail server's error text
    /// can quote the address it rejected. Anything shaped like an email becomes "[address]".
    /// </summary>
    public static class LogRedaction
    {
        private static readonly Regex EmailLike = new(@"[^\s@<>]+@[^\s@<>]+", RegexOptions.Compiled);

        public static string Emails(string? text) => string.IsNullOrEmpty(text) ? string.Empty : EmailLike.Replace(text, "[address]");
    }
}
