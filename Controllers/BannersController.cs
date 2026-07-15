using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BannersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileService _fileService;
        private readonly IDistributedCache _cache;

        public BannersController(ApplicationDbContext context, IFileService fileService, IDistributedCache cache)
        {
            _context = context;
            _fileService = fileService;
            _cache = cache;
        }

        // 1. PUBLIC ENDPOINT: Get active banners optimized with Redis Caching
        // GET: api/Banners?isActive=true&position=1
        [HttpGet]
        public async Task<IActionResult> GetBanners(
            [FromQuery] bool? isActive,
            [FromQuery] BannerPosition? position)
        {
            // Create a dynamic cache key based on the query parameters so filters don't conflict
            string cacheKey = $"banners:active={isActive?.ToString() ?? "all"}:pos={position?.ToString() ?? "all"}";

            // Attempt to fetch from Redis matrix
            string? cachedBanners = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedBanners))
            {
                var deserializedBanners = JsonSerializer.Deserialize<List<Banner>>(cachedBanners);
                return Ok(deserializedBanners);
            }

            // Cache miss - query the database
            var query = _context.Banners.AsQueryable();

            if (isActive.HasValue)
            {
                query = query.Where(b => b.IsActive == isActive.Value);
            }

            if (position.HasValue)
            {
                query = query.Where(b => b.Position == position.Value);
            }

            var banners = await query
                .OrderBy(b => b.Position)
                .ThenBy(b => b.SortOrder)
                .ToListAsync();

            // Store result in Redis with a sliding expiration (e.g., 1 hour)
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
                SlidingExpiration = TimeSpan.FromMinutes(20)
            };

            string serializedBanners = JsonSerializer.Serialize(banners);
            await _cache.SetStringAsync(cacheKey, serializedBanners, cacheOptions);

            return Ok(banners);
        }

        // GET: api/banners/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var banner = await _context.Banners.FindAsync(id);
            if (banner == null) return NotFound("Banner campaign not found.");

            return Ok(banner);
        }

        // 2. ADMIN ENDPOINT: Create a banner with image upload
        [HttpPost]
        public async Task<IActionResult> Create([FromForm] BannerCreateDto dto)
        {
            if (dto.ImageFile == null || dto.ImageFile.Length == 0)
                return BadRequest("An image file is required.");

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

            // Evict all potential banner combinations to ensure cache consistency
            await ClearBannerCacheAsync();

            return CreatedAtAction(nameof(GetBanners), new { id = banner.Id }, banner);
        }

        // 4. UPDATE (PUT /api/banners/{id})
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromForm] BannerUpdateDto dto)
        {
            var banner = await _context.Banners.FindAsync(id);
            if (banner == null) return NotFound("Banner campaign not found.");

            banner.Name = dto.Name;
            banner.LinkUrl = dto.LinkUrl;
            banner.Position = dto.Position;
            banner.SortOrder = dto.SortOrder;
            banner.IsActive = dto.IsActive;

            if (dto.ImageFile != null && dto.ImageFile.Length > 0)
            {
                // Ensure your FileService safely handles missing/null physical paths
                if (!string.IsNullOrEmpty(banner.ImageUrl))
                {
                    _fileService.DeletePhysicalFile(banner.ImageUrl);
                }
                banner.ImageUrl = await _fileService.SaveFileAsync(dto.ImageFile, "banners");
            }

            _context.Entry(banner).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await ClearBannerCacheAsync();

            return Ok(banner);
        }

        // 5. PATCH /api/banners/{id}/toggle-active
        [HttpPatch("{id}/toggle-active")]
        public async Task<IActionResult> ToggleActive(Guid id)
        {
            var banner = await _context.Banners.FindAsync(id);
            if (banner == null) return NotFound("Banner campaign target missing.");

            banner.IsActive = !banner.IsActive;

            if (banner.IsActive && banner.Position == BannerPosition.PromotionPopup)
            {
                var otherActivePopups = await _context.Banners
                    .Where(b => b.Id != id && b.Position == BannerPosition.PromotionPopup && b.IsActive)
                    .ToListAsync();

                foreach (var popup in otherActivePopups)
                {
                    popup.IsActive = false;
                }
            }

            await _context.SaveChangesAsync();
            await ClearBannerCacheAsync();

            return Ok(new { message = $"Banner campaign '{banner.Name}' status shifted to {(banner.IsActive ? "Active" : "Inactive")}.", isActive = banner.IsActive });
        }

        // 3. ADMIN ENDPOINT: Delete banner & clean up physical file
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var banner = await _context.Banners.FindAsync(id);
            if (banner == null) return NotFound();

            if (!string.IsNullOrEmpty(banner.ImageUrl))
            {
                _fileService.DeletePhysicalFile(banner.ImageUrl);
            }

            _context.Banners.Remove(banner);
            await _context.SaveChangesAsync();

            await ClearBannerCacheAsync();

            return NoContent();
        }

        // Helper method to completely purge related banner caches whenever modifications happen
        private async Task ClearBannerCacheAsync()
        {
            // Because Redis IDistributedCache doesn't natively support wildcards (like "banners:*") 
            // without custom Lua scripts or scanning keys, we explicitly target the most common variations 
            // used by your system to keep performance swift and clean.
            var positions = Enum.GetValues<BannerPosition>();

            var keysToRemove = new List<string>
            {
                "banners:active=all:pos=all",
                "banners:active=true:pos=all",
                "banners:active=false:pos=all"
            };

            foreach (var pos in positions)
            {
                keysToRemove.Add($"banners:active=all:pos={pos}");
                keysToRemove.Add($"banners:active=true:pos={pos}");
                keysToRemove.Add($"banners:active=false:pos={pos}");
            }

            var tasks = keysToRemove.Select(key => _cache.RemoveAsync(key));
            await Task.WhenAll(tasks);
        }
    }
}