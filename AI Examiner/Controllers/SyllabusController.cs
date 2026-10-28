using AI_Examiner.Data;
using AI_Examiner.Models.Entities;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Examiner.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SyllabusController : ControllerBase
    {
        private readonly DapperContext _context;

        public SyllabusController(DapperContext context)
        {
            _context = context;
        }

        // =========================================================================
        // 1. ADD SYLLABUS TOPIC
        // =========================================================================
        [HttpPost]
        public async Task<IActionResult> AddSyllabus([FromBody] Syllabus model)
        {
            try
            {
                if (string.IsNullOrEmpty(model.TopicName))
                    return BadRequest("Topic name is required.");

                using var connection = _context.CreateConnection();

                var sql = @"
                    INSERT INTO Syllabus (ExamId, TopicName, Description, MaxMarks, MarkValueType, MaxQuestions, Status)
                    VALUES (@ExamId, @TopicName, @Description, @MaxMarks, @MarkValueType, @MaxQuestions, @Status);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int syllabusId = await connection.ExecuteScalarAsync<int>(sql, model);

                return Ok(new { message = "Syllabus topic added successfully.", syllabusId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error adding syllabus topic: {ex.Message}");
            }
        }

        // =========================================================================
        // 2. GET SYLLABUS BY EXAM
        // =========================================================================
        [HttpGet("exam/{examId}")]
        public async Task<IActionResult> GetByExamId(int examId)
        {
            try
            {
                using var connection = _context.CreateConnection();
                var sql = "SELECT * FROM Syllabus WHERE ExamId = @ExamId";

                var list = await connection.QueryAsync<Syllabus>(sql, new { ExamId = examId });
                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching syllabus: {ex.Message}");
            }
        }

        // =========================================================================
        // 3. DELETE SYLLABUS TOPIC
        // =========================================================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                using var connection = _context.CreateConnection();
                var affectedRows = await connection.ExecuteAsync("DELETE FROM Syllabus WHERE Id = @Id", new { Id = id });

                if (affectedRows == 0)
                    return NotFound(new { message = "Syllabus topic not found." });

                return Ok(new { message = "Syllabus topic deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting syllabus topic: {ex.Message}");
            }
        }
    }
}