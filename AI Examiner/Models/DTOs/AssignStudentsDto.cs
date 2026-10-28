namespace AI_Examiner.Models.DTOs
{
    public class AssignStudentsDto
    {
        public int SetId { get; set; }
        public int Type { get; set; } = 1; // 1: Online, 2: Offline
        public List<int> UserIds { get; set; } = new();
    }
}
