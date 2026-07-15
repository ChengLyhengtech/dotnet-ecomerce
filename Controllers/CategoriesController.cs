using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Mvc;
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
    public class CategoriesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileService _fileService;
        private readonly IDistributedCache _cache; // Added Redis dependency
        private const string CacheKey = "categories:list"; // Static signature key

        public CategoriesController(ApplicationDbContext context, IFileService fileService, IDistributedCache cache)
        {
            _context = context;
            _fileService = fileService;
            _cache = cache;
        }

        // 1. READ ALL (GET /api/categories with dynamic Redis Caching & ProductCount)
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            // Step A: Check local Redis memory
            var cachedJson = await _cache.GetStringAsync(CacheKey);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                Console.WriteLine("CACHE HIT: Returning categories from Redis.");
                var cachedResponse = JsonSerializer.Deserialize<List<CategoryResponseDto>>(cachedJson);
                return Ok(cachedResponse);
            }

            // Step B: CACHE MISS - Query SQL Server
            // Count active, non-soft-deleted products dynamically grouped by Category
            var categories = await _context.Categories
                .Select(c => new CategoryResponseDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ImageUrl = c.ImageUrl,
                    ProductCount = _context.Products.Count(p => p.CategoryId == c.Id && !p.IsDeleted)
                })
                .ToListAsync();

            // Step C: Save fresh structured payload back to Redis for 5 minutes
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            };
            await _cache.SetStringAsync(CacheKey, JsonSerializer.Serialize(categories), cacheOptions);

            return Ok(categories);
        }

        // 2. READ ONE (GET /api/categories/{id})
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var category = await _context.Categories
                .Select(c => new CategoryResponseDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ImageUrl = c.ImageUrl,
                    ProductCount = _context.Products.Count(p => p.CategoryId == c.Id && !p.IsDeleted)
                })
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null) return NotFound("Category not found.");
            return Ok(category);
        }

        // 3. CREATE (POST /api/categories)
        [HttpPost]
        public async Task<IActionResult> Create([FromForm] CategoryCreateDto dto)
        {
            if (dto.Image == null || dto.Image.Length == 0)
                return BadRequest("A valid category image is required.");

            string dbImageUrl = await _fileService.SaveFileAsync(dto.Image, "categories");

            var category = new Category
            {
                Name = dto.Name,
                ImageUrl = dbImageUrl
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            // INVALIDATION: Evict the stale list cache
            await _cache.RemoveAsync(CacheKey);

            return CreatedAtAction(nameof(GetById), new { id = category.Id }, category);
        }

        // 4. UPDATE (PUT /api/categories/{id})
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromForm] CategoryUpdateDto dto)
        {
            var category = await _context.Categories.FindAsync(id);
            if (category == null) return NotFound("Category not found.");

            category.Name = dto.Name;

            if (dto.Image != null && dto.Image.Length > 0)
            {
                _fileService.DeletePhysicalFile(category.ImageUrl);
                category.ImageUrl = await _fileService.SaveFileAsync(dto.Image, "categories");
            }

            _context.Entry(category).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            // INVALIDATION: Evict stale categories and general product listings caches to be safe
            await _cache.RemoveAsync(CacheKey);

            return Ok(category);
        }

        // 5. DELETE (DELETE /api/categories/{id})
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var category = await _context.Categories.FindAsync(id);
            if (category == null) return NotFound("Category not found.");

            // Business Check: Prevent breaking database integrity if products exist under this category
            var hasAssociatedProducts = await _context.Products.AnyAsync(p => p.CategoryId == id && !p.IsDeleted);
            if (hasAssociatedProducts)
            {
                return BadRequest("Cannot delete a category that contains active products. Reassign or archive the products first.");
            }

            _fileService.DeletePhysicalFile(category.ImageUrl);
            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            // INVALIDATION: Wipe stale lists out of memory
            await _cache.RemoveAsync(CacheKey);

            return NoContent();
        }
    }
}