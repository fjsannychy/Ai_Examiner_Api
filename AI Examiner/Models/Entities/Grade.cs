namespace AI_Examiner.Models.Entities
{
    public class Grade
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public decimal CGPA { get; set; }
        public bool IsPassGrade { get; set; }
    }
}
