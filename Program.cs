using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.Filters;
using Ecomerce_dotnet.Hubs;
using Ecomerce_dotnet.Models;
using Ecomerce_dotnet.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// 1. Add services to the container
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<SwaggerFileArrayFilter>();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your valid JWT token."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
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
});

// 2. Identity Services
builder.Services.AddIdentity<User, Role>(options => {
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// 3. Application Services
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// 4. CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontendOnly", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://localhost:3000", "http://localhost:61463", "https://clh168-eccommerce.vercel.app", "http://localhost:49281")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 5. Redis Cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("RedisConnection") ?? "localhost:6379";
    options.InstanceName = "Ecom_";
});

// 6. Background Services & SignalR
builder.Services.AddHostedService<OrderExpiryWorker>();
builder.Services.AddSingleton<BakongQrService>();
builder.Services.AddHttpClient();
builder.Services.AddSignalR();

// 7. Antiforgery
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    // 20 KB = 20 * 1024 bytes = 20,480 bytes
    serverOptions.Limits.MaxRequestBodySize = 20 * 1024;
});

// 8. JWT Authentication Scheme Configuration
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);
builder.Services.AddAuthentication(options => {
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options => {
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];

            // If the request is for our SignalR hubs, read the token from query string
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) &&
                (path.StartsWithSegments("/hubs/notifications") || path.StartsWithSegments("/hubs") || path.StartsWithSegments("/orderHub")))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

// 9. Rate Limiting Configuration (Sliding Window with Hybrid Partitioning)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Policy: "ApiPolicy" - Sliding Window Limiter (100 req / 1 min, 4 segments, queue limit 2)
    options.AddPolicy("ApiPolicy", httpContext =>
    {
        // Hybrid Partitioning Strategy:
        // 1. Authenticated User -> Partition Key based on unique UserId (ClaimTypes.NameIdentifier)
        // 2. Anonymous Visitor  -> Partition Key based on Client Remote IP Address
        string partitionKey;
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            partitionKey = $"user_{userId}";
        }
        else
        {
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
            partitionKey = !string.IsNullOrEmpty(clientIp) ? $"ip_{clientIp}" : "ip_unknown";
        }

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey,
            key => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                QueueLimit = 2,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });

    // Policy: "LoginPolicy" - Strict Sliding Window Limiter (5 req / 1 min, 0 queue) to mitigate brute-force attacks
    options.AddPolicy("LoginPolicy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var partitionKey = !string.IsNullOrEmpty(clientIp) ? $"login_ip_{clientIp}" : "login_ip_unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey,
            key => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                QueueLimit = 0, // Fail immediately without queueing for authentication attempts
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });

    // Custom Rejection Handler with Warning Log and Friendly JSON Response
    options.OnRejected = async (context, cancellationToken) =>
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiting");

        var user = context.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Anonymous";
        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        logger.LogWarning("Rate limit exceeded for User: {User}, IP: {IP}, Path: {Path}",
            user, ip, context.HttpContext.Request.Path);

        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            statusCode = StatusCodes.Status429TooManyRequests,
            message = "Too many requests. Please slow down and try again later.",
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    };
});

// -------------------------------------------------------------
// Application Pipeline
// -------------------------------------------------------------
var app = builder.Build();

// Automatically apply EF Core migrations and run the seeder
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ApplicationDbContext>();
    await context.Database.MigrateAsync();
    await DbSeeder.SeedAsync(services);
}

app.UseCors("AllowFrontendOnly");
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Authentication MUST be executed before Rate Limiting so HttpContext.User is populated for hybrid partitioning
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<OrderHub>("/orderHub");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();