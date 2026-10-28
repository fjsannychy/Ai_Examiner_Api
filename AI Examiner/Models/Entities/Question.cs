namespace AI_Examiner.Models.Entities
{
    public class Question
    {
        public int Id { get; set; }
        public int SetId { get; set; }
        public int? GroupId { get; set; }
        public int Type { get; set; } // 1: MCQ, 2: Short, 3: Description, 4: DescriptionWithImage
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public decimal Marks { get; set; }
        public bool IsRequired { get; set; }
    }
}
