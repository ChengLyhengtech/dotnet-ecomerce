using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Filters;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("ApiPolicy")]
    public class BannersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileService _fileService;
        private readonly IDistributedCache _cache;

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        public BannersController(ApplicationDbContext context, IFileService fileService, IDistributedCache cache)
        {
            _context = context;
            _fileService = fileService;
            _cache = cache;
        }

        // ========================================================
        // 1. PUBLIC ENDPOINTS (No Authorization / Unrestricted Access)
        // ========================================================

        // GET: api/Banners?isActive=true&position=1
        [HttpGet]
        public async Task<IActionResult> GetBanners(
            [FromQuery] bool? isActive,
            [FromQuery] BannerPosition? position)
        {
            string cacheKey = GetCacheKey(isActive, position);
            string? cachedBanners = await _cache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedBanners))
            {
                var deserializedBanners = JsonSerializer.Deserialize<List<Banner>>(cachedBanners);
                return Ok(deserializedBanners);
            }

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

        // ========================================================
        // 2. ADMINISTRATIVE ACTIONS (Secured via Granular RBAC)
        // ========================================================

        // POST: api/banners (Create a banner with dual Desktop & Mobile image uploads)
        [HttpPost]
        [HasPermission("banners:create")]
        public async Task<IActionResult> Create([FromForm] BannerCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (dto.DesktopImage == null || dto.DesktopImage.Length == 0)
                return BadRequest(new { message = "Desktop banner image file is required." });

            if (dto.MobileImage == null || dto.MobileImage.Length == 0)
                return BadRequest(new { message = "Mobile banner image file is required." });

            if (!IsValidImageExtension(dto.DesktopImage))
            {
                return BadRequest(new { message = $"Invalid Desktop image extension. Allowed types: {string.Join(", ", AllowedExtensions)}" });
            }

            if (!IsValidImageExtension(dto.MobileImage))
            {
                return BadRequest(new { message = $"Invalid Mobile image extension. Allowed types: {string.Join(", ", AllowedExtensions)}" });
            }

            string desktopImageUrl = await _fileService.SaveFileAsync(dto.DesktopImage, "banners");
            string mobileImageUrl = await _fileService.SaveFileAsync(dto.MobileImage, "banners");

            var banner = new Banner
            {
                Name = dto.Name.Trim(),
                LinkUrl = dto.LinkUrl.Trim(),
                Position = dto.Position,
                SortOrder = dto.SortOrder,
                DesktopImageUrl = desktopImageUrl,
                MobileImageUrl = mobileImageUrl,
                IsActive = true
            };

            _context.Banners.Add(banner);
            await _context.SaveChangesAsync();

            await ClearBannerCacheAsync();

            return CreatedAtAction(nameof(GetById), new { id = banner.Id }, banner);
        }

        // PUT: api/banners/{id}
        [HttpPut("{id:guid}")]
        [HasPermission("banners:update")]
        public async Task<IActionResult> Update(Guid id, [FromForm] BannerUpdateDto dto)
        {
            var banner = await _context.Banners.FindAsync(id);
            if (banner == null) return NotFound("Banner campaign not found.");

            banner.Name = dto.Name.Trim();
            banner.LinkUrl = dto.LinkUrl.Trim();
            banner.Position = dto.Position;
            banner.SortOrder = dto.SortOrder;
            banner.IsActive = dto.IsActive;

            // Process optional Desktop Image update
            if (dto.DesktopImage != null && dto.DesktopImage.Length > 0)
            {
                if (!IsValidImageExtension(dto.DesktopImage))
                {
                    return BadRequest(new { message = $"Invalid Desktop image extension. Allowed types: {string.Join(", ", AllowedExtensions)}" });
                }

                if (!string.IsNullOrEmpty(banner.DesktopImageUrl))
                {
                    _fileService.DeletePhysicalFile(banner.DesktopImageUrl);
                }

                banner.DesktopImageUrl = await _fileService.SaveFileAsync(dto.DesktopImage, "banners");
            }

            // Process optional Mobile Image update
            if (dto.MobileImage != null && dto.MobileImage.Length > 0)
            {
                if (!IsValidImageExtension(dto.MobileImage))
                {
                    return BadRequest(new { message = $"Invalid Mobile image extension. Allowed types: {string.Join(", ", AllowedExtensions)}" });
                }

                if (!string.IsNullOrEmpty(banner.MobileImageUrl))
                {
                    _fileService.DeletePhysicalFile(banner.MobileImageUrl);
                }

                banner.MobileImageUrl = await _fileService.SaveFileAsync(dto.MobileImage, "banners");
            }

            _context.Entry(banner).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await ClearBannerCacheAsync();

            return Ok(banner);
        }

        // PATCH: api/banners/{id}/toggle-active
        [HttpPatch("{id:guid}/toggle-active")]
        [HasPermission("banners:update")]
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

        // DELETE: api/banners/{id}
        [HttpDelete("{id:guid}")]
        [HasPermission("banners:delete")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var banner = await _context.Banners.FindAsync(id);
            if (banner == null) return NotFound();

            if (!string.IsNullOrEmpty(banner.DesktopImageUrl))
            {
                _fileService.DeletePhysicalFile(banner.DesktopImageUrl);
            }

            if (!string.IsNullOrEmpty(banner.MobileImageUrl))
            {
                _fileService.DeletePhysicalFile(banner.MobileImageUrl);
            }

            _context.Banners.Remove(banner);
            await _context.SaveChangesAsync();

            await ClearBannerCacheAsync();

            return NoContent();
        }

        private static bool IsValidImageExtension(IFormFile file)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            return !string.IsNullOrEmpty(extension) && AllowedExtensions.Contains(extension);
        }

        private static string GetCacheKey(bool? isActive, BannerPosition? position)
        {
            string activeKey = isActive.HasValue ? isActive.Value.ToString().ToLower() : "all";
            string posKey = position.HasValue ? position.Value.ToString().ToLower() : "all";
            return $"banners:active={activeKey}:pos={posKey}";
        }

        private async Task ClearBannerCacheAsync()
        {
            var positions = Enum.GetValues<BannerPosition>();

            var keysToRemove = new List<string>
            {
                GetCacheKey(null, null),
                GetCacheKey(true, null),
                GetCacheKey(false, null)
            };

            foreach (var pos in positions)
            {
                keysToRemove.Add(GetCacheKey(null, pos));
                keysToRemove.Add(GetCacheKey(true, pos));
                keysToRemove.Add(GetCacheKey(false, pos));
            }

            var tasks = keysToRemove.Select(key => _cache.RemoveAsync(key));
            await Task.WhenAll(tasks);
        }
    }
}