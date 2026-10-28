using AI_Examiner.Data;
using AI_Examiner.Models.DTOs; // Required for EmailSettings mapping
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. ADD SERVICES TO THE CONTAINER
// ==========================================

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy
               .AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
        });
});

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<DapperContext>();

// 📧 REGISTER AND BIND EMAIL SETTINGS
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// ==========================================
// 2. CONFIGURE JWT AUTHENTICATION
// ==========================================
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection("Jwt");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["Key"]!)
            ),
            ClockSkew = TimeSpan.Zero // Removes default 5-minute expiration delay
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();

// ==========================================
// 3. CONFIGURE SWAGGER (Updated for Swashbuckle v10+)
// ==========================================
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AI Examiner API",
        Version = "v1"
    });

    // 🔐 JWT Bearer Definition
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your raw JWT token value directly. (Do not type 'Bearer ' manually)"
    });

    // ✅ Swashbuckle v10+ delegate requirement construction
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

// ==========================================
// 4. CONFIGURE THE HTTP REQUEST PIPELINE
// ==========================================
var app = builder.Build();

app.UseCors("AllowAll");
app.UseStaticFiles(); // Serves static assets and uploaded files from wwwroot

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication(); // 🔐 Identifies WHO the user is
app.UseAuthorization();  // 🔐 Identifies WHAT the user can do

app.MapControllers();

app.Run();