namespace AI_Examiner.Models.DTOs
{
    public class CreateQuestionRequest
    {
        public int SetId { get; set; }
        public int? GroupId { get; set; }
        public int Type { get; set; } // 1: MCQ, 2: Short, 3: Description, 4: DescriptionWithImage
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public IFormFile? ImageFile { get; set; }
        public decimal Marks { get; set; }
        public bool IsRequired { get; set; }
        public List<CreateQuestionOptionDto> Options { get; set; } = new();
    }
}
