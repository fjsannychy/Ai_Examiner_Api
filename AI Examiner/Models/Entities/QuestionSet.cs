namespace AI_Examiner.Models.Entities
{
    public class QuestionSet
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Status { get; set; } // 1: Active, 2: Draft, 3: Archived
    }
}
