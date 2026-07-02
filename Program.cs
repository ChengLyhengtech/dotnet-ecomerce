using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.Filters;
using Ecomerce_dotnet.Hubs;
using Ecomerce_dotnet.Models;
using Ecomerce_dotnet.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Replaces the broken placeholder with the binary array target handler
    options.OperationFilter<SwaggerFileArrayFilter>();
});

// 1. Add Identity Services
builder.Services.AddIdentity<User, IdentityRole<Guid>>(options => {
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
})
.AddEntityFrameworkStores<ApplicationDbContext>();

// Add services to the container.
builder.Services.AddScoped<IFileService, FileService>();

// 1. ADD THE CORS POLICY (Put this BEFORE builder.Build())
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontendOnly", policy =>
    {
        policy.WithOrigins("http://127.0.0.1:5501") // Replace with your frontend URL if different
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});


// Add the background service to the container
builder.Services.AddHostedService<OrderExpiryWorker>();
builder.Services.AddSingleton<BakongQrService>();
builder.Services.AddHttpClient();
builder.Services.AddSignalR();

// 2. Add JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.ASCII.GetBytes(jwtSettings["Key"]!);

builder.Services.AddAuthentication(options => {
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options => {
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});

var app = builder.Build();
// This line MUST be here to serve files from wwwroot!
// 2. ACTIVATE THE CORS MIDDLEWARE (Put this AFTER app.UseRouting() but BEFORE app.MapControllers())
app.UseCors("AllowFrontendOnly");
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
// 3. Enable Auth Middleware (Order matters!)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<OrderHub>("/orderHub");

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

    // Define the application roles you need
    string[] roles = { "Admin", "Manager", "Customer" };

    foreach (var roleName in roles)
    {
        // If the role doesn't exist in AspNetRoles, create it
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid> { Name = roleName });
        }
    }
}

app.Run();
