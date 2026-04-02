using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using YourProject.Data;
using YourProject.Hubs;
using YourProject.Middleware;
using IO.Ably;

var builder = WebApplication.CreateBuilder(args);

// ─── ABLY ─────────────────────────────────────────────────────────────────────
string ablyKey = builder.Configuration["Ably:ApiKey"]
                 ?? throw new Exception("Ably API Key is missing in appsettings.json");
builder.Services.AddSingleton<AblyRest>(new AblyRest(ablyKey));

// ─── DATABASE ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ─── HTTP CLIENT (for reCAPTCHA verification) ─────────────────────────────────
builder.Services.AddHttpClient();

// ─── JWT AUTHENTICATION ───────────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
             ?? throw new Exception("JWT Key is missing in appsettings.json");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer              = builder.Configuration["Jwt:Issuer"],
        ValidAudience            = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew                = TimeSpan.Zero  // No grace period — tokens expire exactly at 30 min
    };

    // Read JWT from HttpOnly cookie instead of Authorization header
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            ctx.Token = ctx.Request.Cookies["jwt"];
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ─── SERVICES ─────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddScoped<YourProject.Services.ReportService>();

// ─── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowNextJS", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// ─── MIDDLEWARE PIPELINE ──────────────────────────────────────────────────────
// Order matters: Routing → CORS → Auth → Authorization → custom middleware
app.UseRouting();
app.UseCors("AllowNextJS");
app.UseAuthentication();   // validates JWT cookie on every request
app.UseAuthorization();
app.UseAuditLogging();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<AttendanceHub>("/hubs/attendance");

app.Run();