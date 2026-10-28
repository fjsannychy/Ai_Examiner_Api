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
    public class QuestionsController : ControllerBase
    {
        private readonly DapperContext _context;
        private readonly IWebHostEnvironment _env; // <-- 1. Added environment service

        public QuestionsController(DapperContext context, IWebHostEnvironment env) // <-- 1. Injected here
        {
            _context = context;
            _env = env;
        }

        // =========================================================================
        // 1. CREATE QUESTION SET (e.g., "Set A", "Set B")
        // =========================================================================
        [HttpPost("sets")]
        public async Task<IActionResult> CreateSet([FromBody] QuestionSet model)
        {
            try
            {
                if (string.IsNullOrEmpty(model.Name))
                    return BadRequest("Question Set name is required.");

                using var connection = _context.CreateConnection();

                var sql = @"
                    INSERT INTO QuestionSets (ExamId, Name, Status) 
                    VALUES (@ExamId, @Name, @Status);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int setId = await connection.ExecuteScalarAsync<int>(sql, model);
                return Ok(new { message = "Question set created.", setId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error creating question set: {ex.Message}");
            }
        }

        // =========================================================================
        // 2. GET QUESTION SETS BY EXAM
        // =========================================================================
        [HttpGet("sets/exam/{examId}")]
        public async Task<IActionResult> GetSetsByExam(int examId)
        {
            try
            {
                using var connection = _context.CreateConnection();
                var sets = await connection.QueryAsync<QuestionSet>(
                    "SELECT * FROM QuestionSets WHERE ExamId = @ExamId", new { ExamId = examId });

                return Ok(sets);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching question sets: {ex.Message}");
            }
        }

        // =========================================================================
        // 3. CREATE MANUAL QUESTION (With Optional Options for MCQ & Direct PC Image Upload)
        // =========================================================================
        [HttpPost]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreateQuestion([FromForm] CreateQuestionRequest dto)
        {
            // Fallback: If ASP.NET model binder didn't populate Options from form-data, check if raw JSON was passed
            if ((dto.Options == null || !dto.Options.Any()) && Request.Form.ContainsKey("Options"))
            {
                var rawOptions = Request.Form["Options"].ToString();
                if (!string.IsNullOrWhiteSpace(rawOptions))
                {
                    try
                    {
                        // In case user entered multiple unbracketed objects: { ... }{ ... }
                        // wrap them into a valid JSON array if necessary
                        var trimmed = rawOptions.Trim();
                        if (!trimmed.StartsWith("["))
                        {
                            trimmed = "[" + trimmed.Replace("}{", "},{").Replace("} {", "},{") + "]";
                        }

                        dto.Options = System.Text.Json.JsonSerializer.Deserialize<List<CreateQuestionOptionDto>>(
                            trimmed,
                            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        ) ?? new List<CreateQuestionOptionDto>();
                    }
                    catch
                    {
                        // Leave as empty list to trigger the validation error below
                    }
                }
            }

            // Validate based on the Enum
            if (dto.Type == (int)QuestionType.MCQ || dto.Type == (int)QuestionType.TrueFalse)
            {
                if (dto.Options == null || dto.Options.Count < 2)
                    return BadRequest("MCQ or True/False questions must have at least 2 options.");

                if (!dto.Options.Any(o => o.IsCorrect))
                    return BadRequest("At least one option must be marked as correct (isCorrect: true).");
            }

            // 1. Save PC Image to wwwroot if user selected a file
            string imageUrl = string.Empty;

            if (dto.ImageFile != null && dto.ImageFile.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var extension = Path.GetExtension(dto.ImageFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                    return BadRequest("Only image files (.jpg, .jpeg, .png, .webp) are allowed.");

                var rootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var folderPath = Path.Combine(rootPath, "uploads", "questions");

                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                var uniqueFileName = $"{Guid.NewGuid()}{extension}";
                var fullPath = Path.Combine(folderPath, uniqueFileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await dto.ImageFile.CopyToAsync(stream);
                }

                imageUrl = $"/uploads/questions/{uniqueFileName}";
            }

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                // 2. Insert Question with the uploaded image URL
                // If GroupId is 0 or null, insert NULL
                var groupIdValue = (dto.GroupId == null || dto.GroupId == 0) ? (int?)null : dto.GroupId;

                var questionSql = @"
            INSERT INTO Questions (SetId, GroupId, Type, Title, Description, ImageUrl, Marks, IsRequired)
            VALUES (@SetId, @GroupId, @Type, @Title, @Description, @ImageUrl, @Marks, @IsRequired);
            SELECT CAST(SCOPE_IDENTITY() as int);";

                var questionId = await connection.ExecuteScalarAsync<int>(questionSql, new
                {
                    dto.SetId,
                    GroupId = groupIdValue,
                    dto.Type,
                    dto.Title,
                    dto.Description,
                    ImageUrl = imageUrl,
                    dto.Marks,
                    dto.IsRequired
                }, transaction);

                // 3. Insert Options
                if (dto.Type == (int)QuestionType.MCQ || dto.Type == (int)QuestionType.TrueFalse)
                {
                    var optionSql = @"
                INSERT INTO QuestionOptions (QuestionId, Title, ImageUrl, IsCorrect)
                VALUES (@QuestionId, @Title, @ImageUrl, @IsCorrect);";

                    foreach (var opt in dto.Options!)
                    {
                        await connection.ExecuteAsync(optionSql, new
                        {
                            QuestionId = questionId,
                            opt.Title,
                            opt.ImageUrl,
                            opt.IsCorrect
                        }, transaction);
                    }
                }

                transaction.Commit();
                return Ok(new { message = "Question created successfully.", questionId, imageUrl });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, $"Error creating question: {ex.Message}");
            }
        }
        // =========================================================================
        // 4. GET ALL QUESTIONS IN A SET
        // =========================================================================
        [HttpGet("set/{setId}")]
        public async Task<IActionResult> GetQuestionsBySet(int setId)
        {
            try
            {
                using var connection = _context.CreateConnection();

                var questions = (await connection.QueryAsync<Question>(
                    "SELECT * FROM Questions WHERE SetId = @SetId", new { SetId = setId })).ToList();

                var questionIds = questions.Select(q => q.Id).ToList();

                IEnumerable<QuestionOption> options = new List<QuestionOption>();
                if (questionIds.Any())
                {
                    options = await connection.QueryAsync<QuestionOption>(
                        "SELECT * FROM QuestionOptions WHERE QuestionId IN @Ids", new { Ids = questionIds });
                }

                var response = questions.Select(q => new
                {
                    Question = q,
                    Options = options.Where(o => o.QuestionId == q.Id).ToList()
                });

                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching questions: {ex.Message}");
            }
        }

        // =========================================================================
        // 5. DELETE A QUESTION
        // =========================================================================
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteQuestion(int id)
        {
            try
            {
                using var connection = _context.CreateConnection();
                var affectedRows = await connection.ExecuteAsync(
                    "DELETE FROM Questions WHERE Id = @Id", new { Id = id });

                if (affectedRows == 0)
                    return NotFound(new { message = "Question not found." });

                return Ok(new { message = "Question deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting question: {ex.Message}");
            }
        }
    }
}