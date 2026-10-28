namespace AI_Examiner.Models.DTOs
{
    public class UserUpdate
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public IFormFile? ProfileImage { get; set; }
    }
}
