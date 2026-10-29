using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Filters;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("ApiPolicy")]
    public class BrandsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileService _fileService;
        private readonly IDistributedCache _cache;
        private const string CacheKey = "brands:list"; // Static signature for brand matrix cache

        public BrandsController(ApplicationDbContext context, IFileService fileService, IDistributedCache cache)
        {
            _context = context;
            _fileService = fileService;
            _cache = cache;
        }

        // 1. READ ALL (GET /api/brands - High-performance Redis Cache Layer)
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            // Check Redis Memory
            var cachedJson = await _cache.GetStringAsync(CacheKey);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                Console.WriteLine("CACHE HIT: Returning brand list from Redis.");
                var cachedResponse = JsonSerializer.Deserialize<List<Brand>>(cachedJson);
                return Ok(cachedResponse);
            }

            // CACHE MISS - Pull directly from SQL Server
            var brands = await _context.Brands
                .OrderBy(b => b.Name)
                .ToListAsync();

            // Save payload to Redis for 10 minutes
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            };
            await _cache.SetStringAsync(CacheKey, JsonSerializer.Serialize(brands), cacheOptions);

            return Ok(brands);
        }

        // 2. READ ONE (GET /api/brands/{id})
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var brand = await _context.Brands.FindAsync(id);
            if (brand == null) return NotFound("Brand entity not found.");

            return Ok(brand);
        }

        // 3. CREATE (POST /api/brands - Multipart form upload)
        [HttpPost]
        [HasPermission("brands:create")]
        public async Task<IActionResult> Create([FromForm] BrandCreateDto dto)
        {
            // Business Rule: Ensure brand name is unique to avoid identity cluttering
            var exists = await _context.Brands.AnyAsync(b => b.Name.ToLower() == dto.Name.ToLower());
            if (exists) return BadRequest($"A manufacturer brand named '{dto.Name}' already exists.");

            if (dto.LogoImage == null || dto.LogoImage.Length == 0)
                return BadRequest("A valid branding logo file asset is required.");

            // Physically process the logo file using your asset pipeline
            string dbLogoPath = await _fileService.SaveFileAsync(dto.LogoImage, "brands");

            var brand = new Brand
            {
                Name = dto.Name,
                LogoUrl = dbLogoPath
            };

            _context.Brands.Add(brand);
            await _context.SaveChangesAsync();

            // CACHE INVALIDATION: Wipe stale memory structures immediately
            await _cache.RemoveAsync(CacheKey);

            return CreatedAtAction(nameof(GetById), new { id = brand.Id }, brand);
        }

        // 4. UPDATE (PUT /api/brands/{id})
        [HttpPut("{id}")]
        [HasPermission("brands:update")]
        public async Task<IActionResult> Update(Guid id, [FromForm] BrandUpdateDto dto)
        {
            var brand = await _context.Brands.FindAsync(id);
            if (brand == null) return NotFound("Target brand not found.");

            // Validate duplicate branding names during shifting renames
            if (!string.Equals(brand.Name, dto.Name, StringComparison.OrdinalIgnoreCase))
            {
                var nameExists = await _context.Brands.AnyAsync(b => b.Name.ToLower() == dto.Name.ToLower());
                if (nameExists) return BadRequest($"The brand name '{dto.Name}' is already taken.");
            }

            brand.Name = dto.Name;

            // Only clear and process files if a fresh binary logo was passed
            if (dto.LogoImage != null && dto.LogoImage.Length > 0)
            {
                if (!string.IsNullOrEmpty(brand.LogoUrl))
                {
                    _fileService.DeletePhysicalFile(brand.LogoUrl);
                }
                brand.LogoUrl = await _fileService.SaveFileAsync(dto.LogoImage, "brands");
            }

            _context.Entry(brand).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            // CACHE INVALIDATION: Clear state lists out of Redis
            await _cache.RemoveAsync(CacheKey);

            return Ok(brand);
        }

        // 5. DELETE (DELETE /api/brands/{id})
        [HttpDelete("{id}")]
        [HasPermission("brands:delete")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var brand = await _context.Brands.FindAsync(id);
            if (brand == null) return NotFound("Brand context missing.");

            // CRITICAL RELATIONAL GUARD: Prevent breaking database foreign keys 
            // if active products are currently pointing to this manufacturer brand.
            var hasProductsBound = await _context.Products.AnyAsync(p => p.BrandId == id && !p.IsDeleted);
            if (hasProductsBound)
            {
                return BadRequest("Cannot delete this brand because active product inventory items are currently linked to it. Reassign or delete those products first.");
            }

            if (!string.IsNullOrEmpty(brand.LogoUrl))
            {
                _fileService.DeletePhysicalFile(brand.LogoUrl);
            }

            _context.Brands.Remove(brand);
            await _context.SaveChangesAsync();

            // CACHE INVALIDATION: Erase old cache references
            await _cache.RemoveAsync(CacheKey);

            return NoContent();
        }
    }
}