namespace AI_Examiner.Models.Entities
{
    public class Participant
    {
        public int Id { get; set; }
        public int SetId { get; set; }
        public int Type { get; set; } // 1: Online, 2: Offline
        public int UserId { get; set; }
        public decimal? TotalMarks { get; set; }
        public decimal? CGPA { get; set; }
        public string? Grade { get; set; }
        public string? PdfAnswerSheetURL { get; set; }
        public int Status { get; set; } // ParticipantStatus enum (1: Invited, 2: Accepted, 3: Attended, 4: Completed, 5: Suspended, 6: Absent)
        public int ResultStatus { get; set; } // ResultStatus enum (1: Inactive, 2: Processing, 3: Published, 4: Suspended)
        public bool? IsPassed { get; set; }
    }
}
