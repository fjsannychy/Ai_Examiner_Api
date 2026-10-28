using AI_Examiner.Data;
using AI_Examiner.Models.DTOs;
using AI_Examiner.Models.Entities;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI_Examiner.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ExamsController : ControllerBase
    {
        private readonly DapperContext _context;

        public ExamsController(DapperContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var idClaim = User.FindFirst("Id")?.Value;
            return int.TryParse(idClaim, out var id) ? id : 0;
        }

        // =========================================================================
        // 1. CREATE EXAM
        // =========================================================================
        [HttpPost]
        public async Task<IActionResult> CreateExam([FromBody] CreateExamRequest request)
        {
            try
            {
                int userId = GetCurrentUserId();
                if (userId == 0)
                    return Unauthorized("User identity not found in token.");

                if (string.IsNullOrEmpty(request.Name))
                    return BadRequest("Exam name is required.");

                using var connection = _context.CreateConnection();

                var sql = @"
                    INSERT INTO Exams (Name, Description, StartDate, EndDate, ResultPublishDate, AllowGrading, OwnerUserId, MaxMarks, MinimumMarksRequired, Status)
                    VALUES (@Name, @Description, @StartDate, @EndDate, @ResultPublishDate, @AllowGrading, @OwnerUserId, @MaxMarks, @MinimumMarksRequired, @Status);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int examId = await connection.ExecuteScalarAsync<int>(sql, new
                {
                    request.Name,
                    request.Description,
                    request.StartDate,
                    request.EndDate,
                    request.ResultPublishDate,
                    request.AllowGrading,
                    OwnerUserId = userId,
                    request.MaxMarks,
                    request.MinimumMarksRequired,
                    request.Status
                });

                return Ok(new { message = "Exam created successfully.", examId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while creating the exam: {ex.Message}");
            }
        }

        // =========================================================================
        // 2. GET CURRENT USER'S EXAMS
        // =========================================================================
        [HttpGet]
        public async Task<IActionResult> GetMyExams()
        {
            try
            {
                int userId = GetCurrentUserId();
                if (userId == 0)
                    return Unauthorized("User identity not found in token.");

                using var connection = _context.CreateConnection();
                var sql = "SELECT * FROM Exams WHERE OwnerUserId = @UserId ORDER BY Id DESC";

                var exams = await connection.QueryAsync<Exam>(sql, new { UserId = userId });
                return Ok(exams);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching exams: {ex.Message}");
            }
        }

        // =========================================================================
        // 3. GET EXAM DETAILS BY ID
        // =========================================================================
        [HttpGet("{id}")]
        public async Task<IActionResult> GetExamById(int id)
        {
            try
            {
                using var connection = _context.CreateConnection();

                var exam = await connection.QueryFirstOrDefaultAsync<Exam>(
                    "SELECT * FROM Exams WHERE Id = @Id", new { Id = id });

                if (exam == null)
                    return NotFound(new { message = "Exam not found." });

                var syllabus = await connection.QueryAsync<Syllabus>(
                    "SELECT * FROM Syllabus WHERE ExamId = @ExamId", new { ExamId = id });

                var grades = await connection.QueryAsync<Grade>(
                    "SELECT * FROM Grades WHERE ExamId = @ExamId ORDER BY Min DESC", new { ExamId = id });

                var questionSets = await connection.QueryAsync<QuestionSet>(
                    "SELECT * FROM QuestionSets WHERE ExamId = @ExamId", new { ExamId = id });

                return Ok(new
                {
                    Exam = exam,
                    Syllabus = syllabus,
                    Grades = grades,
                    QuestionSets = questionSets
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching exam details: {ex.Message}");
            }
        }

        // =========================================================================
        // 4. UPDATE EXAM
        // =========================================================================
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateExam(int id, [FromBody] CreateExamRequest request)
        {
            try
            {
                int userId = GetCurrentUserId();
                using var connection = _context.CreateConnection();

                var sql = @"
                    UPDATE Exams 
                    SET Name = @Name,
                        Description = @Description,
                        StartDate = @StartDate,
                        EndDate = @EndDate,
                        ResultPublishDate = @ResultPublishDate,
                        AllowGrading = @AllowGrading,
                        MaxMarks = @MaxMarks,
                        MinimumMarksRequired = @MinimumMarksRequired,
                        Status = @Status
                    WHERE Id = @Id AND OwnerUserId = @UserId";

                var affectedRows = await connection.ExecuteAsync(sql, new
                {
                    request.Name,
                    request.Description,
                    request.StartDate,
                    request.EndDate,
                    request.ResultPublishDate,
                    request.AllowGrading,
                    request.MaxMarks,
                    request.MinimumMarksRequired,
                    request.Status,
                    Id = id,
                    UserId = userId
                });

                if (affectedRows == 0)
                    return BadRequest(new { message = "Update failed or unauthorized." });

                return Ok(new { message = "Exam updated successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error updating exam: {ex.Message}");
            }
        }

        // =========================================================================
        // 5. DELETE EXAM
        // =========================================================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteExam(int id)
        {
            try
            {
                int userId = GetCurrentUserId();
                using var connection = _context.CreateConnection();

                var sql = "DELETE FROM Exams WHERE Id = @Id AND OwnerUserId = @UserId";
                var affectedRows = await connection.ExecuteAsync(sql, new { Id = id, UserId = userId });

                if (affectedRows == 0)
                    return NotFound(new { message = "Exam not found or unauthorized." });

                return Ok(new { message = "Exam deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting exam: {ex.Message}");
            }
        }
    }
}