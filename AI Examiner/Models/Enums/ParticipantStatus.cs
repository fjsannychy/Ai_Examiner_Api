namespace AI_Examiner.Models.Enums
{
    public enum ParticipantStatus
    {
        Invited = 1,
        Accepted = 2,
        Attended = 3,       // In-progress
        Completed = 4,      // Submitted
        Suspended = 5,      // Declined / Suspended
        Absent = 6
    }
}
