using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/admin/[controller]")]
    public class UsersController : ControllerBase
    {
        // Inject UserManager instead of just the DB Context
        private readonly UserManager<User> _userManager;

        public UsersController(UserManager<User> userManager)
        {
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
       [FromQuery] int pageNumber = 1,
       [FromQuery] int pageSize = 10)
        {
            // 1. Fetch the paginated user list from the database
            var users = await _userManager.Users
                .OrderByDescending(u => u.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var response = new List<UserResponseDto>();

            // 2. Loop through each user to attach their roles
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user); // Hits the join table

                response.Add(new UserResponseDto
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email ?? string.Empty,
                    IsBanned = user.IsBanned,
                    CreatedAt = user.CreatedAt,
                    Roles = roles.ToList() // This puts ["Admin"] or ["Customer"] in the JSON!
                });
            }

            return Ok(response);
        }

        // 2. CREATE ACCOUNT WITH ROLE
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UserCreateAdminDto dto)
        {
            var existingUser = await _userManager.FindByEmailAsync(dto.Email);
            if (existingUser != null) return BadRequest("Email already registered.");

            var user = new User
            {
                UserName = dto.Email, // Identity requires UserName to be set
                Email = dto.Email,
                FullName = dto.FullName,
                IsBanned = false,
                CreatedAt = DateTime.UtcNow
            };

            // UserManager automatically hashes the password for you!
            var result = await _userManager.CreateAsync(user, dto.Password);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            // Assign the role using Identity's native system
            await _userManager.AddToRoleAsync(user, dto.Role);

            return Ok(user);
        }

        // 3. UPDATE USER ROLE
        [HttpPut("{id:guid}/role")]
        public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UserUpdateRoleDto dto)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            // Remove from existing roles first
            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

            // Add to the new role
            await _userManager.AddToRoleAsync(user, dto.Role);

            return NoContent();
        }

        // 4. BAN / UNBAN USER
        [HttpPut("{id:guid}/ban")]
        public async Task<IActionResult> ToggleBan(Guid id, [FromBody] UserBanDto dto)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            // This works now because we added IsBanned to your User model!
            user.IsBanned = dto.IsBanned;

            await _userManager.UpdateAsync(user);

            return Ok(new { message = dto.IsBanned ? "User banned." : "User unbanned." });
        }
    }
}
