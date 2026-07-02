using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IFileService _fileService; // Inject the service interface here

    public CategoriesController(ApplicationDbContext context, IFileService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    // 1. READ ALL (GET /api/categories)
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var categories = await _context.Categories.ToListAsync();
        return Ok(categories); // Returns JSON with string image paths
    }

    // 2. READ ONE (GET /api/categories/{id})
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null) return NotFound("Category not found.");
        return Ok(category);
    }

    // 3. CREATE (POST /api/categories)
    [HttpPost]
    public async Task<IActionResult> Create([FromForm] CategoryCreateDto dto)
    {
        if (dto.Image == null || dto.Image.Length == 0)
            return BadRequest("A valid category image is required.");

        // Simply call the helper service and name the target folder "categories"
        string dbImageUrl = await _fileService.SaveFileAsync(dto.Image, "categories");

        var category = new Category
        {
            Name = dto.Name,
            ImageUrl = dbImageUrl
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = category.Id }, category);
    }

    // 4. UPDATE (PUT /api/categories/{id})
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromForm] CategoryUpdateDto dto)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null) return NotFound("Category not found.");

        category.Name = dto.Name;

        // Only update the file if a new one was actually uploaded
        if (dto.Image != null && dto.Image.Length > 0)
        {
            // Delete old physical file to keep server clean
            _fileService.DeletePhysicalFile(category.ImageUrl);

            // Save new file
            category.ImageUrl = await _fileService.SaveFileAsync(dto.Image, "categories");
        }

        _context.Entry(category).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        return Ok(category);
    }

    // 5. DELETE (DELETE /api/categories/{id})
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null) return NotFound("Category not found.");

        // Delegate file cleanup task cleanly
        _fileService.DeletePhysicalFile(category.ImageUrl);

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}