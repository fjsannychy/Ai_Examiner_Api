namespace AI_Examiner.Models.DTOs
{
    public class SaveAnswerDto
    {
        public int ParticipantId { get; set; }
        public int QuestionId { get; set; }
        public int? OptionId { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
    }
}
