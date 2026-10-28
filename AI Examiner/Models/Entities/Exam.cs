namespace AI_Examiner.Models.Entities
{
    public class Exam
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime? ResultPublishDate { get; set; }
        public bool AllowGrading { get; set; }
        public int OwnerUserId { get; set; }
        public decimal MaxMarks { get; set; }
        public int? MinimumMarksRequired { get; set; }
        public int Status { get; set; }
    }
}
