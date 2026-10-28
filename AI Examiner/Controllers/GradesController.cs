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
    public class GradesController : ControllerBase
    {
        private readonly DapperContext _context;

        public GradesController(DapperContext context)
        {
            _context = context;
        }

        // =========================================================================
        // 1. CONFIGURE GRADES FOR AN EXAM (Replace all grades for that exam)
        // =========================================================================
        [HttpPost("exam/{examId}")]
        public async Task<IActionResult> SetExamGrades(int examId, [FromBody] List<Grade> grades)
        {
            if (grades == null || !grades.Any())
                return BadRequest("Grade list cannot be empty.");

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Clear any existing grades for this exam
                await connection.ExecuteAsync(
                    "DELETE FROM Grades WHERE ExamId = @ExamId",
                    new { ExamId = examId },
                    transaction
                );

                var insertSql = @"
                    INSERT INTO Grades (ExamId, Name, Min, Max, CGPA, IsPassGrade)
                    VALUES (@ExamId, @Name, @Min, @Max, @CGPA, @IsPassGrade);";

                foreach (var grade in grades)
                {
                    grade.ExamId = examId;
                    await connection.ExecuteAsync(insertSql, grade, transaction);
                }

                transaction.Commit();
                return Ok(new { message = "Grades configured successfully." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, $"Error saving grade rules: {ex.Message}");
            }
        }

        // =========================================================================
        // 2. GET GRADES BY EXAM
        // =========================================================================
        [HttpGet("exam/{examId}")]
        public async Task<IActionResult> GetByExam(int examId)
        {
            try
            {
                using var connection = _context.CreateConnection();
                var sql = "SELECT * FROM Grades WHERE ExamId = @ExamId ORDER BY Min DESC";

                var grades = await connection.QueryAsync<Grade>(sql, new { ExamId = examId });
                return Ok(grades);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching grades: {ex.Message}");
            }
        }
    }
}