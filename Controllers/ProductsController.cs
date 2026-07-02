using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.DTOs;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileService _fileService; // Reusing your file service!


        public ProductsController(ApplicationDbContext context, IFileService fileService)
        {
            _context = context;
            _fileService = fileService;
        }

        // 1. GET: api/products (Catalog view with dynamic filters)
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] Guid? categoryId, [FromQuery] bool? hasDiscount, [FromQuery] string? search)
        {
            // Always keep soft-deleted products hidden from public visibility
            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Variants)
                .Where(p => !p.IsDeleted)
                .AsQueryable();

            // Filter by Category
            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            // Filter by Discount status
            if (hasDiscount.HasValue && hasDiscount.Value)
            {
                query = query.Where(p => p.DiscountPercentage > 0);
            }

            // Filter by Search Term
            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(lowerSearch) ||
                                         p.Description.ToLower().Contains(lowerSearch));
            }

            var products = await query.ToListAsync();

            // Get all images corresponding to these products to avoid N+1 query loops
            var productIds = products.Select(p => p.Id).ToList();
            var images = await _context.ProductImages
                .Where(img => productIds.Contains(img.ProductId))
                .ToListAsync();

            // Map entities to calculated safe Response DTOs
            var response = products.Select(p => new ProductResponseDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                BasePrice = p.BasePrice,
                DiscountPercentage = p.DiscountPercentage,
                CategoryId = p.CategoryId,
                CategoryName = p.Category?.Name ?? "Unknown",
                TotalAvailableStock = p.Variants.Sum(v => v.PhysicalQuantity - v.ReservedQuantity),
                Variants = p.Variants.Select(v => new ProductVariantDto
                {
                    Id = v.Id,
                    Color = v.Color,
                    Size = v.Size,
                    AvailableStock = Math.Max(0, v.PhysicalQuantity - v.ReservedQuantity),
                    Sku = v.Sku
                }).ToList(),
                Images = images.Where(img => img.ProductId == p.Id).Select(img => new ProductImageDto
                {
                    Id = img.Id,
                    ImageUrl = img.ImageUrl,
                    IsPrimary = img.IsPrimary,
                    ProductVariantId = img.ProductVariantId
                }).ToList()
            }).ToList();

            return Ok(response);
        }

        // 2. GET: api/products/{id} (Detailed product view)
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var product = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

            if (product == null)
            {
                return NotFound("Product not found or has been discontinued.");
            }

            var images = await _context.ProductImages
                .Where(img => img.ProductId == product.Id)
                .ToListAsync();

            var response = new ProductResponseDto
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                BasePrice = product.BasePrice,
                DiscountPercentage = product.DiscountPercentage,
                CategoryId = product.CategoryId,
                CategoryName = product.Category?.Name ?? "Unknown",
                TotalAvailableStock = product.Variants.Sum(v => v.PhysicalQuantity - v.ReservedQuantity),
                Variants = product.Variants.Select(v => new ProductVariantDto
                {
                    Id = v.Id,
                    Color = v.Color,
                    Size = v.Size,
                    AvailableStock = Math.Max(0, v.PhysicalQuantity - v.ReservedQuantity),
                    Sku = v.Sku
                }).ToList(),
                Images = images.Select(img => new ProductImageDto
                {
                    Id = img.Id,
                    ImageUrl = img.ImageUrl,
                    IsPrimary = img.IsPrimary,
                    ProductVariantId = img.ProductVariantId
                }).ToList()
            };

            return Ok(response);
        }

        // 5. POST: api/products (Administrative Product Provisioning)
        [HttpPost]
        //[Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromForm] ProductCreateDto dto)
        {
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId);
            if (!categoryExists)
            {
                return BadRequest("The provided CategoryId does not exist.");
            }

            var incomingSkus = dto.Variants.Select(v => v.Sku).ToList();
            if (incomingSkus.Count != incomingSkus.Distinct().Count())
            {
                return BadRequest("Duplicate SKUs detected within the request payload.");
            }

            var duplicateDbSku = await _context.ProductVariants.AnyAsync(v => incomingSkus.Contains(v.Sku));
            if (duplicateDbSku)
            {
                return BadRequest("One or more SKUs provided are already assigned to existing inventory items.");
            }

            var uploadedFilePaths = new List<string>();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var product = new Product
                {
                    Name = dto.Name,
                    Description = dto.Description,
                    BasePrice = dto.BasePrice,
                    DiscountPercentage = dto.DiscountPercentage,
                    CategoryId = dto.CategoryId,
                    IsDeleted = false
                };

                _context.Products.Add(product);

                var skuToVariantIdMap = new Dictionary<string, Guid>();

                foreach (var vDto in dto.Variants)
                {
                    var variant = new ProductVariant
                    {
                        ProductId = product.Id,
                        Color = vDto.Color,
                        Size = vDto.Size,
                        PhysicalQuantity = vDto.InitialPhysicalQuantity,
                        ReservedQuantity = 0,
                        Sku = vDto.Sku
                        // RowVersion is handled automatically by EF Core/SQL Server, no assignment needed here!
                    };

                    _context.ProductVariants.Add(variant);
                    skuToVariantIdMap.Add(variant.Sku, variant.Id);
                }

                // Process Binary Files using the correct DTO properties
                if (dto.Images != null)
                {
                    for (int i = 0; i < dto.Images.Count; i++)
                    {
                        var file = dto.Images[i];
                        var targetSku = (dto.ImageTargetSkus != null && dto.ImageTargetSkus.Count > i) ? dto.ImageTargetSkus[i] : null;
                        var isPrimary = (dto.ImageIsPrimary != null && dto.ImageIsPrimary.Count > i) ? dto.ImageIsPrimary[i] : false;

                        Guid? explicitVariantId = null;

                        if (!string.IsNullOrWhiteSpace(targetSku))
                        {
                            if (skuToVariantIdMap.TryGetValue(targetSku, out var variantId))
                            {
                                explicitVariantId = variantId;
                            }
                            else
                            {
                                return BadRequest($"Image target SKU '{targetSku}' does not match any variants provided.");
                            }
                        }

                        // Save file physically using your FileService inside a 'products' subfolder
                        string savedFilePath = await _fileService.SaveFileAsync(file, "products");
                        uploadedFilePaths.Add(savedFilePath);

                        var productImage = new ProductImage
                        {
                            ProductId = product.Id,
                            ImageUrl = savedFilePath,
                            IsPrimary = isPrimary,
                            ProductVariantId = explicitVariantId
                        };

                        _context.ProductImages.Add(productImage);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return CreatedAtAction("GetById", new { id = product.Id }, new { message = "Product created successfully.", productId = product.Id });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();

                foreach (var path in uploadedFilePaths)
                {
                    _fileService.DeletePhysicalFile(path);
                }

                throw;
            }
        }


        // 3. PUT: api/products/{id} (Administrative details modification)
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")] // Enforces professional access rules
        public async Task<IActionResult> Update(Guid id, [FromBody] ProductUpdateDto dto)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

            if (product == null)
            {
                return NotFound("Product not found.");
            }

            // Verify the target category is valid before shifting the entity dependency
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId);
            if (!categoryExists)
            {
                return BadRequest("Invalid CategoryId provided.");
            }

            // Update core attributes
            product.Name = dto.Name;
            product.Description = dto.Description;
            product.BasePrice = dto.BasePrice;
            product.DiscountPercentage = dto.DiscountPercentage;
            product.CategoryId = dto.CategoryId;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Product core settings updated successfully." });
        }

        // 4. DELETE: api/products/{id} (Professional Soft Delete)
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SoftDelete(Guid id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound("Product not found.");
            }

            if (product.IsDeleted)
            {
                return BadRequest("Product is already soft-deleted.");
            }

            // Execute professional soft delete modification
            product.IsDeleted = true;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Product successfully archived and removed from customer view catalog." });
        }
    }
}
