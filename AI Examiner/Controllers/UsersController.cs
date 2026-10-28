using AI_Examiner.Data;
using AI_Examiner.Models.DTOs;
using AI_Examiner.Models.Entities;
using Dapper;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MimeKit;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Ocsp;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace AI_Examiner.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly DapperContext _context;
        private readonly IConfiguration _configuration;
        private readonly EmailSettings _settings;

        public UsersController(DapperContext context, IConfiguration configuration, IOptions<EmailSettings> settings)
        {
            _context = context;
            _configuration = configuration;
            _settings = settings.Value;
        }

        // =========================================================================
        // 1. REGISTER USER WITH PROFILE IMAGE
        // =========================================================================
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromForm] RegisterRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.UserName))
                    return BadRequest("Username is required.");

                // Save profile image file if provided
                string profileImageUrl = await SaveFile(request.ProfileImage, "profiles") ?? string.Empty;

                using var connection = _context.CreateConnection();

                // 1. Check if user already exists
                var checkSql = "SELECT COUNT(1) FROM Users WHERE UserName = @UserName";
                var userExists = await connection.ExecuteScalarAsync<bool>(checkSql, new { UserName = request.UserName });

                if (userExists)
                    return BadRequest("User already exists!");

                // 2. Insert User (UserStatus = 0 for Pending Verification)
                var insertUserSql = @"
                    INSERT INTO Users (UserName, PasswordHash, FirstName, LastName, ProfileImageUrl, ContactNumber, Address, UserStatus) 
                    VALUES (@UserName, @PasswordHash, @FirstName, @LastName, @ProfileImageUrl, @ContactNumber, @Address, @UserStatus);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int userId = await connection.ExecuteScalarAsync<int>(insertUserSql, new
                {
                    UserName = request.UserName,
                    PasswordHash = GetMd5Hash(request.Password),
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    ProfileImageUrl = profileImageUrl,
                    ContactNumber = request.ContactNumber,
                    Address = request.Address,
                    UserStatus = 0
                });

                // 3. Send Verification Email
                try
                {
                    var rawTokenPayload = JsonConvert.SerializeObject(new { Id = userId, Date = DateTime.UtcNow });
                    var encryptedToken = Encrypt(rawTokenPayload);

                    await SendVerificationEmail(request.UserName, encryptedToken);

                    return Ok(new { message = "Registration successful! Please check your email to verify your account.", userId });
                }
                catch (Exception emailEx)
                {
                    return StatusCode(500, $"User was created, but verification email failed to send: {emailEx.Message}");
                }
            }
            catch (Exception globalEx)
            {
                return StatusCode(500, $"An unexpected error occurred: {globalEx.Message}");
            }
        }

        // =========================================================================
        // 2. VERIFY USER ACCOUNT
        // =========================================================================
        [HttpGet("verify-user")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyUser([FromQuery] string token)
        {
            if (string.IsNullOrEmpty(token))
                return BadRequest("Verification token is empty.");

            try
            {
                string cleanedToken = token.Replace(" ", "+");
                var decryptedPayload = Decrypt(cleanedToken);
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(decryptedPayload);

                if (data == null || !data.ContainsKey("Id"))
                    return BadRequest("Verification Failed: Invalid token structure.");

                using var connection = _context.CreateConnection();
                var user = await connection.QueryFirstOrDefaultAsync<User>(
                    "SELECT * FROM Users WHERE Id = @Id AND UserStatus = 0",
                    new { Id = data["Id"] }
                );

                if (user == null)
                    return BadRequest("Verification Failed: Account is already active or does not exist.");

                var affectedRows = await connection.ExecuteAsync(
                    "UPDATE Users SET UserStatus = @Status WHERE Id = @Id",
                    new { Status = 1, Id = user.Id }
                );

                if (affectedRows == 0)
                    return BadRequest("Verification Failed during database update.");

                return Ok("Account verified successfully! You can now log in.");
            }
            catch (Exception ex)
            {
                return BadRequest($"Verification Failed: Invalid or corrupted token ({ex.Message}).");
            }
        }

        // =========================================================================
        // 3. LOGIN USER
        // =========================================================================
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request)
        {
            using var connection = _context.CreateConnection();

            var sql = "SELECT * FROM Users WHERE UserName = @Username AND PasswordHash = @Password AND UserStatus = 1";

            var user = await connection.QueryFirstOrDefaultAsync<User>(sql, new
            {
                Username = request.UserName,
                Password = GetMd5Hash(request.Password)
            });

            if (user == null)
                return Unauthorized("Invalid credentials or account not verified.");

            var accessToken = GenerateAccessToken(user);
            var refreshToken = GenerateRefreshToken(user.Id);

            var insertRefreshTokenSql = @"
                INSERT INTO RefreshTokens (Token, UserId, ExpiresAt, IsRevoked) 
                VALUES (@Token, @UserId, @ExpiresAt, @IsRevoked);";

            await connection.ExecuteAsync(insertRefreshTokenSql, refreshToken);

            return Ok(new
            {
                accessToken,
                refreshToken = refreshToken.Token,
                username = user.UserName,
                profileImageUrl = user.ProfileImageUrl
            });
        }

        // =========================================================================
        // 4. GET CURRENT USER PROFILE
        // =========================================================================
        [HttpGet("profile")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var username = User.Identity?.Name;

                if (string.IsNullOrEmpty(username))
                    return Unauthorized("User identity not found in token.");

                using var connection = _context.CreateConnection();

                var sql = @"
                    SELECT Id, UserName, FirstName, LastName, ProfileImageUrl, ContactNumber, Address, UserStatus 
                    FROM Users 
                    WHERE UserName = @UserName";

                var user = await connection.QueryFirstOrDefaultAsync<User>(sql, new { UserName = username });

                if (user == null)
                    return NotFound("User profile not found.");

                return Ok(user);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error fetching profile: {ex.Message}");
            }
        }
        // =========================================================================
        // 5. UPDATE USER PROFILE
        // PUT: api/users/5
        // =========================================================================
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateUser(int id, [FromForm] RegisterRequest user)
        {
            using var connection = _context.CreateConnection();

            // 1. Check if user exists
            var existingUser = await connection.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id", new { Id = id });

            if (existingUser == null)
            {
                return NotFound(new { message = "User not found" });
            }
            string profileImageUrl = "";
            if (user.ProfileImage!=null)
            {
                 profileImageUrl = await SaveFile(user.ProfileImage, "profiles") ?? string.Empty;
            }
            // 2. Retain existing profile image if the incoming one is null or empty
            string finalProfileImageUrl = !string.IsNullOrEmpty(profileImageUrl)
                ? profileImageUrl
                : existingUser.ProfileImageUrl;

            // 3. Update database (Keeping UserStatus unchanged so you don't get locked out)
            var sql = @"UPDATE Users 
        SET FirstName = @FirstName,
            LastName = @LastName,
            ContactNumber = @ContactNumber,
            Address = @Address,
            ProfileImageUrl = @ProfileImageUrl
        WHERE Id = @Id";

            var affectedRows = await connection.ExecuteAsync(sql, new
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                ContactNumber = user.ContactNumber,
                Address = user.Address,
                ProfileImageUrl = finalProfileImageUrl,
                Id = id
            });

            if (affectedRows == 0) return BadRequest(new { message = "Update failed" });

            return Ok(new { message = "Profile updated successfully.", profileImageUrl = finalProfileImageUrl });
        }
        // =========================================================================
        // 6. REFRESH TOKEN
        // =========================================================================
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> RefreshAsync([FromBody] TokenRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.RefreshToken))
                return BadRequest("Invalid token request payload.");

            using var connection = _context.CreateConnection();

            var sql = "SELECT * FROM RefreshTokens WHERE Token = @Token AND IsRevoked = 0 AND ExpiresAt > @CurrentUTC";
            var storedToken = await connection.QueryFirstOrDefaultAsync<RefreshToken>(sql, new
            {
                Token = request.RefreshToken,
                CurrentUTC = DateTime.UtcNow
            });

            if (storedToken == null)
                return Unauthorized("Invalid or expired refresh token.");

            var userSql = "SELECT * FROM Users WHERE Id = @UserId";
            var user = await connection.QueryFirstOrDefaultAsync<User>(userSql, new { UserId = storedToken.UserId });

            if (user == null)
                return Unauthorized("User account associated with this token is no longer available.");

            await connection.ExecuteAsync("DELETE FROM RefreshTokens WHERE Token = @Token", new { Token = storedToken.Token });

            var newRefreshToken = GenerateRefreshToken(user.Id);

            var insertRefreshTokenSql = @"INSERT INTO RefreshTokens (Token, UserId, ExpiresAt, IsRevoked) 
                                          VALUES (@Token, @UserId, @ExpiresAt, @IsRevoked);";
            await connection.ExecuteAsync(insertRefreshTokenSql, newRefreshToken);

            var newAccessToken = GenerateAccessToken(user);

            return Ok(new
            {
                accessToken = newAccessToken,
                refreshToken = newRefreshToken.Token
            });
        }

        // =========================================================================
        // 7. LOGOUT USER
        // =========================================================================
        [HttpPost("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout([FromBody] TokenRequest request)
        {
            using var connection = _context.CreateConnection();
            var deleteSql = "DELETE FROM RefreshTokens WHERE Token = @Token";
            await connection.ExecuteAsync(deleteSql, new { Token = request.RefreshToken });

            return Ok(new { message = "Logged out successfully" });
        }

        // =========================================================================
        // 8. DELETE USER
        // =========================================================================
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> Delete(int id)
        {
            using var connection = _context.CreateConnection();

            await connection.ExecuteAsync("DELETE FROM RefreshTokens WHERE UserId = @Id", new { Id = id });

            var sql = "DELETE FROM Users WHERE Id = @Id";
            var affectedRows = await connection.ExecuteAsync(sql, new { Id = id });

            if (affectedRows == 0) return NotFound(new { message = "User not found" });

            return Ok(new { message = "User deleted successfully" });
        }

        // =========================================================================
        // UTILITIES & SECURITY
        // =========================================================================
        private async Task<string?> SaveFile(IFormFile? file, string subFolder)
        {
            if (file == null || file.Length == 0) return null;

            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", subFolder);
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            var filePath = Path.Combine(folderPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/{subFolder}/{fileName}";
        }

        private string GenerateAccessToken(User user)
        {
            var jwt = _configuration.GetSection("Jwt");

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim("Id", user.Id.ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"] ?? "YourSuperSecretSecurityKeyThatIsLongEnough123!"));

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"] ?? "AIExaminerApi",
                audience: jwt["Audience"] ?? "AIExaminerUsers",
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(jwt["AccessTokenExpiryMinutes"] != null ? int.Parse(jwt["AccessTokenExpiryMinutes"]!) : 15),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private RefreshToken GenerateRefreshToken(int userId)
        {
            var jwt = _configuration.GetSection("Jwt");
            var randomBytes = RandomNumberGenerator.GetBytes(64);

            return new RefreshToken
            {
                UserId = userId,
                Token = Convert.ToBase64String(randomBytes),
                ExpiresAt = DateTime.UtcNow.AddDays(jwt["RefreshTokenExpiryDays"] != null ? int.Parse(jwt["RefreshTokenExpiryDays"]!) : 7),
                IsRevoked = false
            };
        }

        private string Encrypt(string plainText)
        {
            var encryption = _configuration.GetSection("Encryption");
            using var aes = Aes.Create();

            var salt = Encoding.UTF8.GetBytes(encryption["Salt"] ?? "egrykladsg");
            var password = encryption["Key"] ?? "wsertgahsdfegrykladsgjdgrteu";

            aes.Key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 10000, HashAlgorithmName.SHA256, 32);
            aes.IV = Rfc2898DeriveBytes.Pbkdf2(password, salt, 10000, HashAlgorithmName.SHA256, 16);

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        private string Decrypt(string cipherText)
        {
            var encryption = _configuration.GetSection("Encryption");
            using var aes = Aes.Create();

            var salt = Encoding.UTF8.GetBytes(encryption["Salt"] ?? "egrykladsg");
            var password = encryption["Key"] ?? "wsertgahsdfegrykladsgjdgrteu";

            aes.Key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 10000, HashAlgorithmName.SHA256, 32);
            aes.IV = Rfc2898DeriveBytes.Pbkdf2(password, salt, 10000, HashAlgorithmName.SHA256, 16);

            var buffer = Convert.FromBase64String(cipherText);
            using var ms = new MemoryStream(buffer);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);

            return sr.ReadToEnd();
        }

        [NonAction]
        public async Task SendVerificationEmail(string toEmail, string token)
        {
            string encodedToken = WebUtility.UrlEncode(token);
            var link = $"{_settings.VerficationURL}?token={encodedToken}";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("AI Examiner Support", _settings.Email));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Verify Your Account - AI Examiner";

            message.Body = new TextPart("html")
            {
                Text = $@"
                    <div style='font-family: Arial, sans-serif; padding: 25px; border: 1px solid #ddd; max-width: 500px; border-radius: 8px;'>
                        <h3 style='color: #2c3e50;'>Welcome to AI Examiner!</h3>
                        <p>Please activate your account by verifying your email address below:</p>
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{link}' style='background-color: #27ae60; color: white; padding: 12px 25px; text-decoration: none; border-radius: 4px; font-weight: bold;'>Verify Email Address</a>
                        </p>
                    </div>"
            };

            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(_settings.Host, _settings.Port, false);
            await client.AuthenticateAsync(_settings.Email, _settings.AppPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        private static string GetMd5Hash(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}