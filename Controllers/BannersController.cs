using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BannersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileService _fileService; // Reusing your file service!

        public BannersController(ApplicationDbContext context, IFileService fileService)
        {
            _context = context;
            _fileService = fileService;
        }

        // 1. PUBLIC ENDPOINT: Get active banners optimized for the frontend
        // GET: api/Banners?isActive=true&position=1
        [HttpGet]
        public async Task<IActionResult> GetBanners(
            [FromQuery] bool? isActive,
            [FromQuery] BannerPosition? position)
        {
            // 1. Start with an un-executed queryable context
            var query = _context.Banners.AsQueryable();

            // 2. Conditionally apply the IsActive filter if it's provided in the URL
            if (isActive.HasValue)
            {
                query = query.Where(b => b.IsActive == isActive.Value);
            }

            // 3. Conditionally apply the Position filter if it's provided in the URL
            if (position.HasValue)
            {
                query = query.Where(b => b.Position == position.Value);
            }

            // 4. Apply standard sorting and execute the query against the database
            var banners = await query
                .OrderBy(b => b.Position)
                .ThenBy(b => b.SortOrder)
                .ToListAsync();

            return Ok(banners);
        }

        // 2. ADMIN ENDPOINT: Create a banner with image upload
        [HttpPost]
        public async Task<IActionResult> Create([FromForm] BannerCreateDto dto)
        {
            if (dto.ImageFile == null || dto.ImageFile.Length == 0)
                return BadRequest("An image file is required.");

            // Save image to wwwroot/uploads/banners
            string imageUrl = await _fileService.SaveFileAsync(dto.ImageFile, "banners");

            var banner = new Banner
            {
                Name = dto.Name,
                LinkUrl = dto.LinkUrl,
                Position = dto.Position,
                SortOrder = dto.SortOrder,
                ImageUrl = imageUrl,
                IsActive = true
            };

            _context.Banners.Add(banner);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetBanners), new { id = banner.Id }, banner);
        }

        // 3. ADMIN ENDPOINT: Delete banner & clean up physical file
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var banner = await _context.Banners.FindAsync(id);
            if (banner == null) return NotFound();

            // Optional: Add a method in your FileService to delete files, e.g.,
            // _fileService.DeleteFile(banner.ImageUrl);

            _context.Banners.Remove(banner);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
