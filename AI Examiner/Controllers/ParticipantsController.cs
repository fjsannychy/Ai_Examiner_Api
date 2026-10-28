using System.Security.Claims;
using AI_Examiner.Data;
using AI_Examiner.Models.DTOs;
using AI_Examiner.Models.Entities;
using AI_Examiner.Models.Enums;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Examiner.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ParticipantsController : ControllerBase
    {
        private readonly DapperContext _context;

        public ParticipantsController(DapperContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst("Id")?.Value
                          ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return int.TryParse(idClaim, out var id) ? id : 0;
        }

        // =========================================================================
        // 1. FACULTY: Assign Students to Question Set
        // =========================================================================
        [HttpPost("assign")]
        public async Task<IActionResult> AssignStudents([FromBody] AssignStudentsDto dto)
        {
            if (dto.UserIds == null || !dto.UserIds.Any())
                return BadRequest("Provide at least one UserId to invite.");

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var sql = @"
                    IF NOT EXISTS (SELECT 1 FROM Participants WHERE SetId = @SetId AND UserId = @UserId)
                    BEGIN
                        INSERT INTO Participants (SetId, Type, UserId, Status, ResultStatus)
                        VALUES (@SetId, @Type, @UserId, @Status, @ResultStatus);
                    END";

                foreach (var uid in dto.UserIds)
                {
                    var participant = new Participant
                    {
                        SetId = dto.SetId,
                        Type = dto.Type,
                        UserId = uid,
                        Status = (int)ParticipantStatus.Invited,
                        ResultStatus = (int)ResultStatus.Inactive
                    };

                    await connection.ExecuteAsync(sql, participant, transaction);
                }

                transaction.Commit();
                return Ok(new { message = "Students invited successfully." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, $"Error assigning students: {ex.Message}");
            }
        }

        // =========================================================================
        // 2. STUDENT: Get My Assigned Exams
        // =========================================================================
        [HttpGet("my-exams")]
        public async Task<IActionResult> GetMyExams()
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == 0)
                return Unauthorized("Unable to resolve student identity from JWT token.");

            using var connection = _context.CreateConnection();

            var sql = @"
                SELECT 
                    p.Id AS ParticipantId,
                    p.SetId,
                    p.Type,
                    p.Status AS ParticipantStatus,
                    p.ResultStatus,
                    qs.Name AS SetName,
                    e.Name AS ExamName,
                    e.StartDate,
                    e.EndDate
                FROM Participants p
                INNER JOIN QuestionSets qs ON p.SetId = qs.Id
                INNER JOIN Exams e ON qs.ExamId = e.Id
                WHERE p.UserId = @UserId";

            var exams = await connection.QueryAsync<MyExamDto>(sql, new { UserId = currentUserId });
            return Ok(exams);
        }

        // =========================================================================
        // 3. STUDENT: Respond to Invitation (Accept or Decline)
        // =========================================================================
        [HttpPost("respond")]
        public async Task<IActionResult> RespondInvitation([FromBody] RespondInvitationDto dto)
        {
            var currentUserId = GetCurrentUserId();

            using var connection = _context.CreateConnection();

            var participant = await connection.QueryFirstOrDefaultAsync<Participant>(
                "SELECT * FROM Participants WHERE Id = @Id AND UserId = @UserId",
                new { Id = dto.ParticipantId, UserId = currentUserId });

            if (participant == null)
                return NotFound("Participant record not found or does not belong to you.");

            if (participant.Status != (int)ParticipantStatus.Invited)
                return BadRequest("You can only respond to pending invitations (Status: Invited).");

            var targetStatus = dto.Accept
                ? ParticipantStatus.Accepted
                : ParticipantStatus.Suspended;

            await connection.ExecuteAsync(
                "UPDATE Participants SET Status = @Status WHERE Id = @Id",
                new { Status = (int)targetStatus, Id = dto.ParticipantId });

            return Ok(new
            {
                message = dto.Accept ? "Invitation accepted." : "Invitation declined.",
                status = (int)targetStatus,
                statusName = targetStatus.ToString()
            });
        }

        // =========================================================================
        // 4. STUDENT: Start Exam (Attended / In-Progress)
        // =========================================================================
        [HttpPost("start/{participantId}")]
        public async Task<IActionResult> StartExam(int participantId)
        {
            var currentUserId = GetCurrentUserId();

            using var connection = _context.CreateConnection();

            var participant = await connection.QueryFirstOrDefaultAsync<Participant>(
                "SELECT * FROM Participants WHERE Id = @Id AND UserId = @UserId",
                new { Id = participantId, UserId = currentUserId });

            if (participant == null)
                return NotFound("Participant record not found.");

            if (participant.Status == (int)ParticipantStatus.Attended)
                return Ok(new { message = "Exam already in progress.", setId = participant.SetId, participantId });

            if (participant.Status != (int)ParticipantStatus.Accepted)
                return BadRequest("You must accept the invitation before starting the exam.");

            await connection.ExecuteAsync(
                "UPDATE Participants SET Status = @Status WHERE Id = @Id",
                new { Status = (int)ParticipantStatus.Attended, Id = participantId });

            return Ok(new
            {
                message = "Exam started. Good luck!",
                setId = participant.SetId,
                participantId
            });
        }
        // =========================================================================
        // 5. STUDENT: Get Exam Paper (Questions & Options WITHOUT revealing answers)
        // =========================================================================
        [HttpGet("exam-paper/{participantId}")]
        public async Task<IActionResult> GetExamPaper(int participantId)
        {
            var currentUserId = GetCurrentUserId();

            using var connection = _context.CreateConnection();

            // 1. Validate that the participant belongs to this student
            var participant = await connection.QueryFirstOrDefaultAsync<Participant>(
                "SELECT * FROM Participants WHERE Id = @Id AND UserId = @UserId",
                new { Id = participantId, UserId = currentUserId });

            if (participant == null)
                return NotFound("Exam session not found.");

            // 2. Security check: Only allow viewing questions if exam is actively in progress
            if (participant.Status != (int)ParticipantStatus.Attended)
                return BadRequest("Cannot access exam paper. You must start the exam first, and it must be in progress.");

            // 3. Fetch questions assigned to this student's QuestionSet
            var questionsSql = @"
        SELECT Id, Type, Title, Description, ImageUrl, Marks, IsRequired 
        FROM Questions 
        WHERE SetId = @SetId";

            var questions = (await connection.QueryAsync<StudentExamPaperQuestionDto>(
                questionsSql, new { SetId = participant.SetId })).ToList();

            if (!questions.Any())
                return Ok(questions);

            var questionIds = questions.Select(q => q.Id).ToList();

            // 4. Fetch options WITHOUT IsCorrect (critical for exam security)
            var optionsSql = @"
        SELECT Id, QuestionId, Title, ImageUrl 
        FROM QuestionOptions 
        WHERE QuestionId IN @Ids";

            var rawOptions = await connection.QueryAsync<dynamic>(optionsSql, new { Ids = questionIds });

            // Group options under their respective questions
            foreach (var q in questions)
            {
                q.Options = rawOptions
                    .Where(o => (int)o.QuestionId == q.Id)
                    .Select(o => new StudentQuestionOptionDto
                    {
                        Id = (int)o.Id,
                        Title = (string)o.Title,
                        ImageUrl = (string)o.ImageUrl
                    }).ToList();
            }

            return Ok(new
            {
                participantId = participant.Id,
                setId = participant.SetId,
                totalQuestions = questions.Count,
                questions = questions
            });
        }
    }
}