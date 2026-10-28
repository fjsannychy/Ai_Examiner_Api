namespace AI_Examiner.Models.DTOs
{
    public class CreateExamRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime? ResultPublishDate { get; set; }
        public bool AllowGrading { get; set; }
        public decimal MaxMarks { get; set; }
        public int? MinimumMarksRequired { get; set; }
        public int Status { get; set; } = 1;
    }
}
