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
    public class AnswersController : ControllerBase
    {
        private readonly DapperContext _context;

        public AnswersController(DapperContext context)
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
        // 1. STUDENT: Save or Update Answer (Upsert during active exam)
        // =========================================================================
        [HttpPost("save")]
        public async Task<IActionResult> SaveAnswer([FromBody] SaveAnswerDto dto)
        {
            var currentUserId = GetCurrentUserId();

            using var connection = _context.CreateConnection();

            // Validate that the participant session belongs to this user
            var participant = await connection.QueryFirstOrDefaultAsync<Participant>(
                "SELECT * FROM Participants WHERE Id = @Id AND UserId = @UserId",
                new { Id = dto.ParticipantId, UserId = currentUserId });

            if (participant == null)
                return NotFound("Participant record not found.");

            if (participant.Status == (int)ParticipantStatus.Completed)
                return BadRequest("Cannot save answers. The exam has already been submitted and locked.");

            if (participant.Status != (int)ParticipantStatus.Attended)
                return BadRequest("Cannot save answers. The exam is not currently in progress (Status must be Attended).");

            // Map DTO to Answer Entity
            var answerEntity = new Answer
            {
                ParticipantId = dto.ParticipantId,
                QuestionId = dto.QuestionId,
                OptionId = dto.OptionId,
                Description = dto.Description,
                ImageUrl = dto.ImageUrl
            };

            var upsertSql = @"
                IF EXISTS (SELECT 1 FROM Answers WHERE ParticipantId = @ParticipantId AND QuestionId = @QuestionId)
                BEGIN
                    UPDATE Answers 
                    SET OptionId = @OptionId, 
                        Description = @Description, 
                        ImageUrl = @ImageUrl
                    WHERE ParticipantId = @ParticipantId AND QuestionId = @QuestionId;
                END
                ELSE
                BEGIN
                    INSERT INTO Answers (ParticipantId, QuestionId, OptionId, Description, ImageUrl)
                    VALUES (@ParticipantId, @QuestionId, @OptionId, @Description, @ImageUrl);
                END";

            await connection.ExecuteAsync(upsertSql, answerEntity);

            return Ok(new { message = "Answer saved successfully." });
        }

        // =========================================================================
        // 2. STUDENT: Get all saved answers to restore form state
        // =========================================================================
        [HttpGet("my-answers/{participantId}")]
        public async Task<IActionResult> GetMyAnswers(int participantId)
        {
            var currentUserId = GetCurrentUserId();

            using var connection = _context.CreateConnection();

            var participant = await connection.QueryFirstOrDefaultAsync<Participant>(
                "SELECT * FROM Participants WHERE Id = @Id AND UserId = @UserId",
                new { Id = participantId, UserId = currentUserId });

            if (participant == null)
                return NotFound("Participant record not found.");

            var answers = await connection.QueryAsync<SavedAnswerResponseDto>(
                "SELECT Id, ParticipantId, QuestionId, OptionId, Description, ImageUrl FROM Answers WHERE ParticipantId = @ParticipantId",
                new { ParticipantId = participantId });

            return Ok(answers);
        }

        // =========================================================================
        // 3. STUDENT: Submit & Finalize Exam (Locks participant session)
        // =========================================================================
        [HttpPost("submit/{participantId}")]
        public async Task<IActionResult> SubmitExam(int participantId)
        {
            var currentUserId = GetCurrentUserId();

            using var connection = _context.CreateConnection();

            var participant = await connection.QueryFirstOrDefaultAsync<Participant>(
                "SELECT * FROM Participants WHERE Id = @Id AND UserId = @UserId",
                new { Id = participantId, UserId = currentUserId });

            if (participant == null)
                return NotFound("Participant record not found.");

            if (participant.Status == (int)ParticipantStatus.Completed)
                return BadRequest("This exam has already been submitted.");

            if (participant.Status != (int)ParticipantStatus.Attended)
                return BadRequest("Cannot submit an exam that is not currently in progress.");

            // Update status to Completed (4)
            await connection.ExecuteAsync(
                "UPDATE Participants SET Status = @Status WHERE Id = @Id",
                new { Status = (int)ParticipantStatus.Completed, Id = participantId });

            return Ok(new { message = "Exam submitted and locked successfully." });
        }
    }
}