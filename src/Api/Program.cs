using System.Text;
using Api.Auth;
using Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Amazon.S3;
using Amazon.SQS;
using Amazon;
using Microsoft.OpenApi.Models;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

var dotenvPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".env"));
builder.Configuration.AddInMemoryCollection(LoadDotEnv(dotenvPath));

builder.Services.AddControllers();  // scope of the controllers is set to "transient", which means a new instance of each controller will be created for every HTTP request. This is the default behavior in ASP.NET Core, and it allows for better performance and scalability, as controllers are lightweight and can be easily instantiated for each request without the overhead of managing their lifecycle.
builder.Services.AddEndpointsApiExplorer(); // used for Swagger, it discovers API endpoints, and generates metadata for them, which is then used to create interactive API documentation. It allows developers to easily test and understand the API's capabilities through a user-friendly interface provided by Swagger UI.

builder.Services.AddHttpClient<IInsightsGenerationService, OpenAiRagInsightsGenerationService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var timeout = int.TryParse(config["LLM_TIMEOUT_SECONDS"], out var seconds)
        ? Math.Clamp(seconds, 10, 180)
        : 45;
    client.Timeout = TimeSpan.FromSeconds(timeout);
});

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "API", Version = "v1" });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
}); // used to generate Swagger/OpenAPI documentation for the API. It scans the API's controllers and actions, and produces a JSON document that describes the API's endpoints, request/response models, and other relevant information. This documentation can then be consumed by tools like Swagger UI to create interactive API documentation.

// DB
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? builder.Configuration["CONNSTR_HOST"]
    ?? throw new InvalidOperationException("Missing connection string. Set ConnectionStrings:Default or CONNSTR_HOST.");
builder.Services.AddDbContext<AppDbContext>(options =>  // scope of the DbContext is set to "scoped", which means a new instance of AppDbContext will be created for each HTTP request. This is a common practice for DbContext in web applications, as it ensures that each request has its own instance of the context, preventing issues with concurrent access and ensuring proper disposal of resources.
    options.UseNpgsql(connectionString));   // UseNpgsql is an extension method provided by the Npgsql.EntityFrameworkCore.PostgreSQL package, which allows Entity Framework Core to work with PostgreSQL databases. It configures the DbContext to use PostgreSQL as the database provider, and it takes a connection string as a parameter to establish the connection to the database. In this case, it retrieves the connection string named "Default" from the application's configuration settings.

// Identity
builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// JWT auth
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? builder.Configuration["JWT_KEY"]
    ?? throw new InvalidOperationException("Missing Jwt:Key");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? builder.Configuration["JWT_ISSUER"]
    ?? "aihealth.local";
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? builder.Configuration["JWT_AUDIENCE"]
    ?? "aihealth.local";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new AmazonS3Client(
        config["AWS_ACCESS_KEY_ID"],
        config["AWS_SECRET_ACCESS_KEY"],
        RegionEndpoint.GetBySystemName(config["AWS_REGION"])
    );
});

// Amazon
builder.Services.AddSingleton<IAmazonSQS>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new AmazonSQSClient(
        config["AWS_ACCESS_KEY_ID"],
        config["AWS_SECRET_ACCESS_KEY"],
        RegionEndpoint.GetBySystemName(config["AWS_REGION"])
    );
});

builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var region = builder.Configuration["AWS_REGION"] ?? "us-east-1";
    return new AmazonS3Client(RegionEndpoint.GetBySystemName(region));
});

builder.Services.AddSingleton<IAmazonSQS>(_ =>
{
    var region = builder.Configuration["AWS_REGION"] ?? "us-east-1";
    return new AmazonSQSClient(RegionEndpoint.GetBySystemName(region));
});

builder.Services.AddAuthorization();

var corsAllowedOrigins = (builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
    {
        if (corsAllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsAllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        // Fallback for first-time deployments when an explicit origin is not set yet.
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
await RoleSeeder.SeedAsync(app.Services);

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("FrontendCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

app.Run();

static IDictionary<string, string> LoadDotEnv(string path)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (!File.Exists(path)) return values;

    foreach (var line in File.ReadAllLines(path))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

        var idx = trimmed.IndexOf('=');
        if (idx <= 0) continue;

        var key = trimmed[..idx].Trim();
        var value = trimmed[(idx + 1)..].Trim();
        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            value = value[1..^1];
        }

        values[key] = value;
    }

    return values;
}