namespace AI_Examiner.Models.Entities
{
    public class Answer
    {
        public int Id { get; set; }
        public int ParticipantId { get; set; }
        public int QuestionId { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int? OptionId { get; set; }
    }
}
