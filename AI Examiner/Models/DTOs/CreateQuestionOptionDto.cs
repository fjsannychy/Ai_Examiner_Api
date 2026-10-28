namespace AI_Examiner.Models.DTOs
{
    public class CreateQuestionOptionDto
    {
        public string Title { get; set; } = string.Empty;
        public string? ImageUrl { get; set; } = string.Empty;
        public bool IsCorrect { get; set; }

    }
}
