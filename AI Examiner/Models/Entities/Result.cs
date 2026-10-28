namespace AI_Examiner.Models.Entities
{
    public class Result
    {
        public int Id { get; set; }
        public int ParticipantId { get; set; }
        public int QuestionId { get; set; }
        public decimal Marks { get; set; }
        public string ReasonOfMarking { get; set; } = string.Empty;
    }
}
