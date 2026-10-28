namespace AI_Examiner.Models.DTOs
{
    public class EmailSettings
    {
        public string Email { get; set; } = string.Empty;
        public string AppPassword { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string VerficationURL { get; set; } = string.Empty;
    }
}
