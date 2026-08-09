using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Authorization;
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
        private readonly IDistributedCache _cache;
        private const string FlatCacheKey = "categories:flat";
        private const string TreeCacheKey = "categories:tree";

        public CategoriesController(ApplicationDbContext context, IFileService fileService, IDistributedCache cache)
        {
            _context = context;
            _fileService = fileService;
            _cache = cache;
        }

        // ========================================================
        // 1. GET: api/categories (Flat List with Parent/SubCategory Info)
        // ========================================================
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool? rootOnly, [FromQuery] Guid? parentCategoryId)
        {
            // If filtering by specific parameters, bypass static cache
            if (!rootOnly.HasValue && !parentCategoryId.HasValue)
            {
                var cachedJson = await _cache.GetStringAsync(FlatCacheKey);
                if (!string.IsNullOrEmpty(cachedJson))
                {
                    var cachedResponse = JsonSerializer.Deserialize<List<CategoryResponseDto>>(cachedJson);
                    return Ok(cachedResponse);
                }
            }

            var query = _context.Categories
                .Include(c => c.ParentCategory)
                .Include(c => c.SubCategories)
                .AsQueryable();

            if (rootOnly.HasValue && rootOnly.Value)
            {
                query = query.Where(c => c.ParentCategoryId == null);
            }
            else if (parentCategoryId.HasValue)
            {
                query = query.Where(c => c.ParentCategoryId == parentCategoryId.Value);
            }

            var categories = await query
                .Select(c => new CategoryResponseDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ImageUrl = c.ImageUrl,
                    ParentCategoryId = c.ParentCategoryId,
                    ParentCategoryName = c.ParentCategory != null ? c.ParentCategory.Name : null,
                    ProductCount = _context.Products.Count(p => p.CategoryId == c.Id && !p.IsDeleted),
                    SubCategoryCount = c.SubCategories.Count
                })
                .OrderBy(c => c.ParentCategoryName)
                .ThenBy(c => c.Name)
                .ToListAsync();

            if (!rootOnly.HasValue && !parentCategoryId.HasValue)
            {
                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _cache.SetStringAsync(FlatCacheKey, JsonSerializer.Serialize(categories), cacheOptions);
            }

            return Ok(categories);
        }

        // ========================================================
        // 2. GET: api/categories/tree (Hierarchical Mega-Menu Tree)
        // ========================================================
        [HttpGet("tree")]
        public async Task<IActionResult> GetTree()
        {
            var cachedJson = await _cache.GetStringAsync(TreeCacheKey);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                var cachedResponse = JsonSerializer.Deserialize<List<CategoryTreeDto>>(cachedJson);
                return Ok(cachedResponse);
            }

            // Load all categories with products into memory to construct recursive tree nodes
            var allCategories = await _context.Categories
                .Include(c => c.Products)
                .ToListAsync();

            var rootCategories = allCategories
                .Where(c => c.ParentCategoryId == null)
                .OrderBy(c => c.Name)
                .Select(c => BuildCategoryTree(c, allCategories))
                .ToList();

            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            };
            await _cache.SetStringAsync(TreeCacheKey, JsonSerializer.Serialize(rootCategories), cacheOptions);

            return Ok(rootCategories);
        }

        // ========================================================
        // 3. GET: api/categories/{id} (Single Category Detail)
        // ========================================================
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var category = await _context.Categories
                .Include(c => c.ParentCategory)
                .Include(c => c.SubCategories)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null) return NotFound("Target category entity not found.");

            var response = new CategoryResponseDto
            {
                Id = category.Id,
                Name = category.Name,
                ImageUrl = category.ImageUrl,
                ParentCategoryId = category.ParentCategoryId,
                ParentCategoryName = category.ParentCategory?.Name,
                ProductCount = await _context.Products.CountAsync(p => p.CategoryId == category.Id && !p.IsDeleted),
                SubCategoryCount = category.SubCategories.Count,
                SubCategories = category.SubCategories.Select(sub => new CategoryResponseDto
                {
                    Id = sub.Id,
                    Name = sub.Name,
                    ImageUrl = sub.ImageUrl,
                    ParentCategoryId = sub.ParentCategoryId,
                    ParentCategoryName = category.Name,
                    ProductCount = _context.Products.Count(p => p.CategoryId == sub.Id && !p.IsDeleted),
                    SubCategoryCount = _context.Categories.Count(c => c.ParentCategoryId == sub.Id)
                }).ToList()
            };

            return Ok(response);
        }

        // ========================================================
        // 4. POST: api/categories (Create Root or Sub-Category)
        // ========================================================
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Create([FromForm] CategoryCreateDto dto)
        {
            // Validate Parent Category if provided
            if (dto.ParentCategoryId.HasValue)
            {
                var parentExists = await _context.Categories.AnyAsync(c => c.Id == dto.ParentCategoryId.Value);
                if (!parentExists)
                {
                    return BadRequest("The specified ParentCategoryId does not exist.");
                }
            }

            string? dbImageUrl = null;
            if (dto.Image != null && dto.Image.Length > 0)
            {
                dbImageUrl = await _fileService.SaveFileAsync(dto.Image, "categories");
            }

            var category = new Category
            {
                Name = dto.Name,
                ImageUrl = dbImageUrl,
                ParentCategoryId = dto.ParentCategoryId
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            await InvalidateCategoryCacheAsync();

            return CreatedAtAction(nameof(GetById), new { id = category.Id }, category);
        }

        // ========================================================
        // 5. PUT: api/categories/{id} (Update Category)
        // ========================================================
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Update(Guid id, [FromForm] CategoryUpdateDto dto)
        {
            var category = await _context.Categories.FindAsync(id);
            if (category == null) return NotFound("Target category not found.");

            // Cyclic & Self Parent Guard
            if (dto.ParentCategoryId.HasValue)
            {
                if (dto.ParentCategoryId.Value == id)
                {
                    return BadRequest("A category cannot be set as its own parent.");
                }

                var parentExists = await _context.Categories.AnyAsync(c => c.Id == dto.ParentCategoryId.Value);
                if (!parentExists)
                {
                    return BadRequest("The specified ParentCategoryId does not exist.");
                }

                bool isCyclic = await IsDescendantAsync(id, dto.ParentCategoryId.Value);
                if (isCyclic)
                {
                    return BadRequest("Invalid hierarchy: Cannot assign a descendant subcategory as the new parent category.");
                }
            }

            category.Name = dto.Name;
            category.ParentCategoryId = dto.ParentCategoryId;

            if (dto.Image != null && dto.Image.Length > 0)
            {
                if (!string.IsNullOrEmpty(category.ImageUrl))
                {
                    _fileService.DeletePhysicalFile(category.ImageUrl);
                }
                category.ImageUrl = await _fileService.SaveFileAsync(dto.Image, "categories");
            }

            _context.Entry(category).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await InvalidateCategoryCacheAsync();

            return Ok(category);
        }

        // ========================================================
        // 6. DELETE: api/categories/{id} (Delete Category)
        // ========================================================
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var category = await _context.Categories
                .Include(c => c.SubCategories)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null) return NotFound("Category entity not found.");

            // SubCategory Relational Guard
            if (category.SubCategories != null && category.SubCategories.Any())
            {
                return BadRequest("Cannot delete a category that has subcategories attached. Please reassign or delete the child subcategories first.");
            }

            // Active Products Guard
            var hasAssociatedProducts = await _context.Products.AnyAsync(p => p.CategoryId == id && !p.IsDeleted);
            if (hasAssociatedProducts)
            {
                return BadRequest("Cannot delete a category that contains active products. Reassign or delete those products first.");
            }

            if (!string.IsNullOrEmpty(category.ImageUrl))
            {
                _fileService.DeletePhysicalFile(category.ImageUrl);
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            await InvalidateCategoryCacheAsync();

            return NoContent();
        }

        // ========================================================
        // PRIVATE HELPER METHODS
        // ========================================================

        private CategoryTreeDto BuildCategoryTree(Category category, List<Category> allCategories)
        {
            var children = allCategories
                .Where(c => c.ParentCategoryId == category.Id)
                .OrderBy(c => c.Name)
                .Select(c => BuildCategoryTree(c, allCategories))
                .ToList();

            int directProducts = category.Products != null ? category.Products.Count(p => !p.IsDeleted) : 0;
            int totalProducts = directProducts + children.Sum(c => c.TotalProductCount);

            return new CategoryTreeDto
            {
                Id = category.Id,
                Name = category.Name,
                ImageUrl = category.ImageUrl,
                ParentCategoryId = category.ParentCategoryId,
                DirectProductCount = directProducts,
                TotalProductCount = totalProducts,
                SubCategories = children
            };
        }

        private async Task<bool> IsDescendantAsync(Guid categoryId, Guid potentialParentId)
        {
            var currentId = (Guid?)potentialParentId;
            while (currentId.HasValue)
            {
                if (currentId.Value == categoryId) return true;

                var parent = await _context.Categories
                    .Where(c => c.Id == currentId.Value)
                    .Select(c => c.ParentCategoryId)
                    .FirstOrDefaultAsync();

                currentId = parent;
            }
            return false;
        }

        private async Task InvalidateCategoryCacheAsync()
        {
            try
            {
                await _cache.RemoveAsync(FlatCacheKey);
                await _cache.RemoveAsync(TreeCacheKey);
            }
            catch
            {
                // Silently swallow cache errors if Redis is unreachable
            }
        }
    }
}