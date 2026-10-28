namespace AI_Examiner.Models.DTOs
{
    public class StudentExamPaperQuestionDto
    {
        public int Id { get; set; }
        public int Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public decimal Marks { get; set; }
        public bool IsRequired { get; set; }
        public List<StudentQuestionOptionDto> Options { get; set; } = new();
    }
}
