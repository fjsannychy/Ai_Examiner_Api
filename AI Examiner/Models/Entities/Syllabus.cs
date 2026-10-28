namespace AI_Examiner.Models.Entities
{
    public class Syllabus
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        public string TopicName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int? MaxMarks { get; set; }
        public int? MarkValueType { get; set; } // 1: Percentage, 2: Flat
        public int? MaxQuestions { get; set; }
        public int? Status { get; set; }
    }
}
