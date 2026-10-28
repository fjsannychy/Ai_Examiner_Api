namespace AI_Examiner.Models.DTOs
{
    public class MyExamDto
    {
        public int ParticipantId { get; set; }
        public int SetId { get; set; }
        public int Type { get; set; }
        public int ParticipantStatus { get; set; }
        public int ResultStatus { get; set; }
        public string SetName { get; set; } = string.Empty;
        public string ExamName { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
