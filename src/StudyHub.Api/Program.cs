using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Live;
using StudyHub.Api.Practice;
using StudyHub.Api.Security;
using StudyHub.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ----- configuration -----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be set to a random secret of at least 32 characters (use the Jwt__Key environment variable).");
}

// The database address comes from ConnectionStrings:Default, or from DATABASE_URL on a hosting provider.
var databaseAddress = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(databaseAddress))
{
    databaseAddress = builder.Configuration["DATABASE_URL"];
}
var connectionString = DatabaseUrl.ToConnectionString(databaseAddress ?? string.Empty);

// Hosting providers choose the port and pass it in the PORT variable.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// ----- services -----
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IMaterialService, MaterialService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IQuizService, QuizService>();
builder.Services.AddScoped<IGameHistoryService, GameHistoryService>();
builder.Services.AddScoped<IPracticeService>(sp => new PracticeService(
    sp.GetRequiredService<AppDbContext>(),
    sp.GetRequiredService<PracticeRegistry>(),
    sp.GetRequiredService<IGameHistoryService>()));

// Solo practice runs in progress live in memory, like live games.
builder.Services.AddSingleton<PracticeRegistry>();

// Live quizzes: SignalR keeps a connection open to each browser, and the registry holds the running games.
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameRegistry>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // A live connection can't send the login token in a header, so the browser
        // puts it in the address instead. Accept it there, but only for the hubs.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
// Admins are the people whose email is listed in Admin:Emails. The "Admin" policy protects the admin routes.
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.Section));
var adminOptions = builder.Configuration.GetSection(AdminOptions.Section).Get<AdminOptions>() ?? new AdminOptions();
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AdminOptions.Policy, policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context => adminOptions.IsAdmin(context.User.GetEmail()))));

builder.Services.AddControllers();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// ----- pipeline -----
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // JSON description of the API at /openapi/v1.json
}

// Apply database migrations on startup when enabled (on in Development and in docker-compose).
if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// Add the starter notes once, so a new site isn't empty. On by default in Development;
// on a hosting provider set Database__SeedSampleNotes=true.
if (app.Configuration.GetValue("Database:SeedSampleNotes", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    var added = await SampleData.SeedAsync(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        scope.ServiceProvider.GetRequiredService<IPasswordHasher>());
    if (added > 0)
    {
        app.Logger.LogInformation("Added {Count} starter notes.", added);
    }
}

// Serve the website (wwwroot/index.html, styles.css, app.js).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<QuizHub>("/hubs/quiz");
app.MapHealthChecks("/health");

app.Run();

// Lets future integration tests reference the app's entry point.
public partial class Program;
